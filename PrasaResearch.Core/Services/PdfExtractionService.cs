using System.Globalization;
using System.Text.RegularExpressions;
using PrasaResarch.Models;
using UglyToad.PdfPig;

namespace PrasaResarch.Services;
public class PdfExtractionService
{
    private readonly AppSettings settings;
    private readonly ExtractionDiagnostics diagnostics;
    private readonly IOfflineOcr ocr;
    public PdfExtractionService(AppSettings? settings = null, IOfflineOcr? ocr = null, ExtractionDiagnostics? diagnostics = null)
    {
        this.settings = settings ?? new(); this.diagnostics = diagnostics ?? new();
        this.ocr = ocr ?? new OfflineOcrService(this.settings, this.diagnostics);
    }
    public Extraction Extract(byte[] bytes, DateTime? expectedEnd, CancellationToken ct, Action<int, int>? pageProgress = null)
        => ExtractAsync(bytes, expectedEnd, ct, pageProgress).GetAwaiter().GetResult();
    public async Task<Extraction> ExtractAsync(byte[] bytes, DateTime? expectedEnd, CancellationToken ct, Action<int, int>? pageProgress = null, Action<string>? activity = null)
    {
        PdfContentValidator.Validate(bytes);
        PdfDocument parsed;
        try { parsed = PdfDocument.Open(bytes); }
        catch (Exception ex) { diagnostics.Write("pdf_parsing", "failed", ex.GetType().Name); throw new InvalidDataException("The PDF could not be parsed. The document may be corrupted, encrypted or unsupported.", ex); }
        using var pdf = parsed;
        if (pdf.NumberOfPages == 0) throw new InvalidDataException("The PDF has no pages.");
        var total = pdf.NumberOfPages;
        var coordinates = new Dictionary<int, IReadOnlyList<PdfWord>>();
        var texts = new Dictionary<int, string>();
        var missing = new HashSet<int>();
        var planner = new OcrPagePlanner(total);
        var enginePages = new HashSet<int>();
        var cachedPages = new HashSet<int>();
        int completed = 0, needed = 0, nativeSkipped = 0;
        string finalStatus = "Cancelled or interrupted", confidence = "";
        Extraction Finish(Extraction result) { finalStatus = result.Status; confidence = result.Confidence; return result; }
        try
        {
            diagnostics.Write("pdf_parsing", "opened", $"totalPages={total}; bytes={bytes.Length}; OCR budget={(settings.OcrAllPages ? "all" : settings.OcrMaxPages)}");
            // Inventory text first so contents references and statement headings can guide OCR order.
            foreach (var page in pdf.GetPages())
            {
                ct.ThrowIfCancellationRequested(); pageProgress?.Invoke(page.Number, total);
                var words = page.GetWords().Select(w => { ct.ThrowIfCancellationRequested(); return new PdfWord(w.Text, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom); }).ToArray();
                coordinates[page.Number] = words; texts[page.Number] = Text(words);
                planner.Observe(page.Number, texts[page.Number]);
                if (!HasUsableText(words)) missing.Add(page.Number); else nativeSkipped++;
                diagnostics.Write("pdf_text", missing.Contains(page.Number) ? "ocr_candidate" : "skipped_ocr", $"{words.Length} words", page.Number);
            }
            needed = missing.Count;
            var limit = settings.OcrAllPages ? needed : Math.Min(needed, Math.Max(0, settings.OcrMaxPages));
            bool usedOcr = false, disagreement = false, lowConfidence = false;
            var competing = new List<RevenueCandidate>();
            int confirmed = 0;
            if (needed > 0 && !ocr.IsAvailable(out var reason))
                return Finish(new(null, "", expectedEnd, "PDF text", "OCR Required", "OCR unavailable", $"{needed} of {total} pages require OCR (first: Page {missing.Min()}). {reason}"));
            while (missing.Count > 0 && completed < limit)
            {
                ct.ThrowIfCancellationRequested();
                var number = planner.Next(missing);
                activity?.Invoke($"OCR page {number}/{total} — {completed}/{needed} pages checked; {nativeSkipped} text pages skipped; {cachedPages.Count} cached");
                usedOcr = true;
                try
                {
                    var read = await ocr.ReadPageAsync(bytes, number, 6, ct);
                    if (read.FromCache) cachedPages.Add(number); else enginePages.Add(number);
                    coordinates[number] = read.Words; texts[number] = read.Text;
                    lowConfidence |= read.Confidence < 85;
                    planner.Observe(number, read.Text);
                    var candidate = new PdfTableRevenueReader().Extract([(number, read.Words)], expectedEnd);
                    if (candidate?.Status == "Found")
                    {
                        var check = await ocr.ReadPageAsync(bytes, number, 3, ct);
                        if (check.FromCache) cachedPages.Add(number); else enginePages.Add(number);
                        var second = new PdfTableRevenueReader().Extract([(number, check.Words)], expectedEnd);
                        if (second?.Status != "Found" || second.Value != candidate.Value || second.Currency != candidate.Currency)
                        { disagreement = true; competing.AddRange(new PdfTableRevenueReader().Analyze([(number, check.Words)]).Select(c => c with { Issue = "Alternate OCR pass: " + c.Issue })); }
                        else confirmed++;
                    }
                    missing.Remove(number); completed++;
                    diagnostics.Write("ocr_coverage", "page_completed", $"total={total}; checked={completed}; needed={needed}; nativeSkipped={nativeSkipped}; enginePages={enginePages.Count}; cachedPages={cachedPages.Count}; remaining={missing.Count}", number);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or DllNotFoundException or TypeInitializationException)
                {
                    diagnostics.Write("ocr", "failed", ex.GetType().Name, number);
                    return Finish(new(null, "", expectedEnd, "OCR", "Extraction Failed", "OCR failed", $"Page {number}: OCR failed ({ex.GetType().Name}). {completed}/{needed} OCR pages completed. Retry will reuse cached pages."));
                }
            }
            if (missing.Count > 0)
                return Finish(new(null, "", expectedEnd, "PDF + OCR", "Needs Review", "Incomplete page coverage", $"Checked {completed}/{needed} OCR pages in a {total}-page PDF; skipped {nativeSkipped} readable text pages. {missing.Count} pages remain (first: {string.Join(", ", missing.Order().Take(10))}). Enable Process all pages requiring OCR (maximum 0) in Settings, then Retry Extraction. No amount is published from partial coverage."));
            if (disagreement) return Finish(new Extraction(null, "", expectedEnd, "OCR table", "Needs Review", "OCR disagreement", "Independent OCR layout passes did not agree on the financial table. Verify the source document.") { Candidates = new PdfTableRevenueReader().Analyze(coordinates.Select(p => (p.Key, p.Value))).Concat(competing).ToList() });
            var result = new PdfTableRevenueReader().Extract(coordinates.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)), expectedEnd)
                ?? ExtractText(texts.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)), expectedEnd);
            if (texts.Values.Any(t => Regex.IsMatch(t, @"consolidated\s*(?:financial|accounts|income|statement|profit)|group\s*(?:financial|accounts|income|statement|profit)", RegexOptions.IgnoreCase))) result = result with { ConsolidatedAccounts = true };
            if (usedOcr && result.Status == "Not Disclosed" && lowConfidence) result = result with { Status = "Needs Review", Confidence = "Low confidence omission evidence" };
            if (usedOcr) result = result with { Method = "PDF + Tesseract OCR", Status = result.Status == "OCR Required" ? "Needs Review" : result.Status,
                Confidence = result.Status == "Found" && confirmed > 0 ? "High OCR confidence; two layout passes agree" : result.Confidence,
                Evidence = $"Coverage: {total}/{total} pages inspected; {completed} OCR pages; {nativeSkipped} native text pages skipped; {cachedPages.Count} cached pages.\n" + result.Evidence };
            activity?.Invoke($"Document checked: {total}/{total} pages; OCR {completed}; cached {cachedPages.Count}; {result.Status}");
            return Finish(result);
        }
        finally
        {
            diagnostics.Write("ocr_summary", finalStatus, $"totalPages={total}; inspectedTextPages={coordinates.Count}; ocrNeeded={needed}; ocrChecked={completed}; ocrProcessed={enginePages.Count}; cachePages={cachedPages.Count}; skippedTextPages={nativeSkipped}; remainingPages={missing.Count}; confidence={confidence}");
        }
    }
    public static bool HasUsableText(IReadOnlyList<PdfWord> words)
    {
        var text = string.Concat(words.Select(w => w.Text));
        return words.Count >= 6 && text.Length >= 40 && text.Count(char.IsLetterOrDigit) >= text.Length * .55 && !text.Contains('\uFFFD');
    }
    private static string Text(IEnumerable<PdfWord> words) => string.Join("\n", PdfTableRevenueReader.Lines(words).Select(l => string.Join(" ", l.Select(w => w.Text))));
    public Extraction ExtractText(IEnumerable<(int Number, string Text)> pages, DateTime? expectedEnd)
    {
        var all = pages.ToList();
        if (all.Sum(p => p.Text.Trim().Length) < 40) return new(null, "", expectedEnd, "PDF text", "OCR Required", "Missing text layer", "Little or no extractable text. Offline OCR is required for the scanned pages.");
        var evidence = all.SelectMany(p => p.Text.Split('\n').Where(l => Regex.IsMatch(l, @"^\s*(?:Total\s+revenue|Revenue(?:\s+from\s+contracts\s+with\s+customers)?|Turnover|Sales)\b", RegexOptions.IgnoreCase)).Select(l => $"Page {p.Number}: {l.Trim()}")).ToList();
        if (evidence.Count > 0) return new(null, DetectCurrency(string.Join("\n", all.Select(p => p.Text))), expectedEnd, "PDF text", "Needs Review", "Unvalidated table", string.Join("\n", evidence.Take(30)) + "\nVerify year-column alignment, currency, scale and consolidation basis in the source.");
        var text = string.Join("\n", all.Select(p => p.Text));
        if (Regex.IsMatch(text, @"(?:profit\s+and\s+loss\s+account|income\s+statement).{0,100}(?:not\s+(?:been\s+)?(?:filed|delivered)|not\s+required\s+(?:to\s+be\s+)?(?:filed|delivered)|omitted)", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            return new(null, "", expectedEnd, "PDF text", "Not Disclosed", "Explicit omission", "The filed accounts explicitly state that the profit and loss account is omitted/not filed.");
        return new(null, "", expectedEnd, "PDF text", "Needs Review", "No reliable revenue evidence", "No reliably identifiable turnover found. Missing labels alone do not establish non-disclosure.");
    }
    public static string DetectCurrency(string text) => Regex.IsMatch(text, @"\bGBP\b|\u00a3") ? "GBP" : Regex.IsMatch(text, @"\bUSD\b|US\$") ? "USD" : Regex.IsMatch(text, @"\bEUR\b|\u20ac") ? "EUR" : "";
}

