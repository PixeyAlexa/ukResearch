using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PrasaResarch.Models;
using UglyToad.PdfPig;

namespace PrasaResarch.Services;
public class XbrlExtractionService
{
    private static readonly HashSet<string> Concepts = new(StringComparer.OrdinalIgnoreCase)
    { "Turnover", "Revenue", "RevenueFromContractsWithCustomers", "RevenueFromContractsWithCustomersExcludingAssessedTax", "SalesRevenueNet", "RevenueTotal", "SalesRevenueGoodsNet" };
    public Extraction Extract(byte[] bytes, DateTime? expectedEnd)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 90_000_000 });
        var doc = XDocument.Load(reader);
        var contexts = doc.Descendants().Where(e => e.Name.LocalName == "context" && e.Attribute("id") != null).ToDictionary(e => (string)e.Attribute("id")!);
        var units = doc.Descendants().Where(e => e.Name.LocalName == "unit" && e.Attribute("id") != null).ToDictionary(e => (string)e.Attribute("id")!);
        var candidates = new List<(decimal Value, string Currency, DateTime End, string Evidence)>();
        var evidence = new List<RevenueCandidate>();
        bool uncertain = false;
        foreach (var fact in doc.Descendants())
        {
            var concept = ((string?)fact.Attribute("name") ?? fact.Name.LocalName).Split(':').Last();
            if (!Concepts.Contains(concept) || fact.Attribute("contextRef") is not { } contextRef) continue;
            if (fact.Attribute("continuedAt") != null || (fact.Name.NamespaceName.Contains("inlineXBRL", StringComparison.Ordinal) && fact.Name.LocalName != "nonFraction"))
            { uncertain = true; continue; }
            if (!contexts.TryGetValue(contextRef.Value, out var context)) { uncertain = true; continue; }
            // Segment/scenario facts represent products, regions, or a different consolidation basis.
            var members = context.Descendants().Where(e => e.Name.LocalName == "explicitMember").ToList();
            if (context.Descendants().Any(e => e.Name.LocalName == "typedMember") || members.Any(m => !((string?)m.Attribute("dimension") ?? "").Contains("Consolidation", StringComparison.OrdinalIgnoreCase))) continue;
            var entity = members.Any(m => Regex.IsMatch(m.Value.Split(':').Last(), @"^(?:ConsolidatedGroup|Consolidated|Group)Member$", RegexOptions.IgnoreCase)) ? "Group" : members.Any(m => Regex.IsMatch(m.Value.Split(':').Last(), @"^(?:Company|ParentCompany|Parent|Separate|Individual|Unconsolidated)Member$", RegexOptions.IgnoreCase)) ? "Company" : "Unknown";
            var endText = context.Descendants().FirstOrDefault(e => e.Name.LocalName == "endDate")?.Value;
            var startText = context.Descendants().FirstOrDefault(e => e.Name.LocalName == "startDate")?.Value;
            if (!DateTime.TryParse(endText, CultureInfo.InvariantCulture, out var end) || !DateTime.TryParse(startText, CultureInfo.InvariantCulture, out var start) || (end-start).Days < 1) { uncertain = true; continue; }
            if (expectedEnd.HasValue && end.Date != expectedEnd.Value.Date) continue;
            if (fact.Attributes().Any(a => a.Name.LocalName == "nil" && a.Value is "true" or "1")) continue;
            if (!units.TryGetValue((string?)fact.Attribute("unitRef") ?? "", out var unit)) { uncertain = true; continue; }
            var measures = unit.Descendants().Where(e => e.Name.LocalName == "measure").ToList();
            if (measures.Count != 1 || unit.Descendants().Any(e => e.Name.LocalName == "divide")) { uncertain = true; continue; }
            var currency = measures[0].Value.Split(':').Last().ToUpperInvariant();
            var measureParts = measures[0].Value.Split(':');
            if (measureParts.Length != 2 || measures[0].GetNamespaceOfPrefix(measureParts[0])?.NamespaceName != "http://www.xbrl.org/2003/iso4217") { uncertain = true; continue; }
            if (!Regex.IsMatch(currency, "^[A-Z]{3}$")) { uncertain = true; continue; }
            var format = ((string?)fact.Attribute("format") ?? "").Split(':').Last().ToLowerInvariant();
            var raw = string.Concat(fact.DescendantNodes().OfType<XText>().Where(t => !t.Ancestors().Any(a => a.Name.LocalName == "exclude")).Select(t => t.Value)).Trim();
            if (format is "num-comma-decimal" or "numcommadecimal") raw = raw.Replace(".", "").Replace(",", ".");
            else if (format.Length > 0 && format is not "num-dot-decimal" and not "numdotdecimal") { uncertain = true; continue; }
            raw = raw.Replace(",", "").Replace(" ", "").Replace("\u00a0", "");
            if (!decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowParentheses, CultureInfo.InvariantCulture, out var value)) { uncertain = true; continue; }
            if (fact.Attribute("scale") is { } scaleAttribute)
            {
                if (!int.TryParse(scaleAttribute.Value, out var scale)) { uncertain = true; continue; }
                if (scale is < -12 or > 12) { uncertain = true; continue; }
                value *= (decimal)Math.Pow(10, scale);
            }
            if (fact.Attribute("sign") is { } sign && sign.Value is not "+" and not "-") { uncertain = true; continue; }
            if ((string?)fact.Attribute("sign") == "-") value = -Math.Abs(value);
            candidates.Add((value, currency, end, $"Fact {concept}; context {contextRef.Value}; period {start:yyyy-MM-dd} to {end:yyyy-MM-dd}; unit {currency}; raw {raw}; scale {(string?)fact.Attribute("scale") ?? "0"}"));
            evidence.Add(new() { FactReference = $"{concept}/{contextRef.Value}", Year = end.Year, Amount = value, Currency = currency, Entity = entity, Exact = true, Statement = true, Aligned = true, Confidence = 100, RawText = raw, Headings = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}" });
        }
        if (candidates.Count == 0) return new(null, "", expectedEnd, "XBRL", "Needs Review", "Unvalidated", uncertain ? "Revenue facts could not be validated." : "No supported unsegmented revenue fact for this reporting period.");
        if (uncertain) return new Extraction(null, "", expectedEnd, "XBRL", "Needs Review", "Incomplete facts", "At least one candidate revenue fact could not be validated alongside the parsed facts. Review the document.") { Candidates = evidence };
        var latest = candidates.Max(c => c.End);
        var current = candidates.Where(c => c.End == latest).DistinctBy(c => (c.Value, c.Currency)).ToList();
        if (current.Count != 1) return new(null, "", latest, "XBRL", "Needs Review", "Ambiguous", "Conflicting revenue facts: " + string.Join("; ", current.Select(c => c.Evidence))) { Candidates = evidence };
        var selected = current[0];
        return new(selected.Value, selected.Currency, selected.End, "XBRL", "Found", "Validated structured fact", selected.Evidence) { Candidates = evidence };
    }
}
public class CompaniesHouseDocumentService(ICompaniesHouseApi api)
{
    private readonly ExtractionDiagnostics diagnostics = new();
    public async Task<(string ContentUrl, string[] Types)> MetadataAsync(string url, CancellationToken ct, string? companyNumber = null)
    {
        using var json = await api.GetJsonAsync(url, ct); var r = json.RootElement;
        if (companyNumber != null && r.TryGetProperty("company_number", out var registered) && registered.GetString() is { Length: > 0 } owner && owner != companyNumber)
            throw new InvalidDataException("The accounts metadata belongs to a different company. Document processing was stopped.");
        var types = r.TryGetProperty("resources", out var resources) ? resources.EnumerateObject().Select(p => p.Name).ToArray() : [];
        diagnostics.Write("document_metadata", "verified", $"Company {companyNumber}; advertised types: {string.Join(", ", types)}");
        var content = r.GetProperty("links").GetProperty("document").GetString() ?? throw new InvalidDataException("No document content link is available.");
        return (new Uri(new Uri("https://document-api.company-information.service.gov.uk"), content).AbsoluteUri, types);
    }
    public async Task<byte[]> DownloadAsync(string url, string type, CancellationToken ct, Action<DownloadProgress>? progress = null)
    {
        try
        {
            var bytes = await api.DownloadWithProgressAsync(url, type, progress == null ? null : new CallbackProgress<DownloadProgress>(progress), ct);
            if (type == "application/pdf") PdfContentValidator.Validate(bytes);
            diagnostics.Write("document_download", "validated", $"Type {type}; {bytes.Length} bytes; SHA256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))}");
            return bytes;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { diagnostics.Write("document_download", "failed", ex.GetType().Name); throw; }
    }
}
public interface ICompanyProcessor { Task<CompanyResult> ProcessAsync(string number, CancellationToken ct); }
public interface IProcessingProgressSource { event Action<CompanyResult>? ResultUpdated; }
public class RevenueExtractionService(ICompaniesHouseApi api, CompaniesHouseDocumentService documents, XbrlExtractionService xbrl, PdfExtractionService pdf, SettingsService settings, TimeSpan? operationTimeout = null) : ICompanyProcessor, IProcessingProgressSource
{
    private readonly ExtractionDiagnostics diagnostics = new(settings.Root);
    public event Action<CompanyResult>? ResultUpdated;
    private void DownloadUpdate(CompanyResult result, DownloadProgress update)
    {
        var size = update.Total is > 0 ? $"{update.Received / 1048576d:F1} / {update.Total.Value / 1048576d:F1} MB ({100d * update.Received / update.Total.Value:F0}%)" : $"{update.Received / 1048576d:F1} MB";
        Report(result, $"Downloading accounts — {update.Activity}; {size}");
    }
    private void Report(CompanyResult result, string stage) { result.Stage = stage; diagnostics.Write("processing", "running", $"{result.CompanyNumber}: {stage}"); ResultUpdated?.Invoke(result.Snapshot()); }
    public async Task<CompanyResult> ProcessAsync(string number, CancellationToken ct)
    {
        var callerToken = ct;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var searchBudget = operationTimeout ?? (settings.Settings.EnableOcr && settings.Settings.OcrAllPages ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(settings.Settings.EnableOcr ? 6 : 2));
        if (searchBudget != Timeout.InfiniteTimeSpan) deadline.CancelAfter(searchBudget);
        ct = deadline.Token;
        var result = new CompanyResult { CompanyNumber = number };
        try
        {
            Report(result, "Retrieving company profile");
            var profile = await api.GetProfileAsync(number, ct);
            result.CompanyName = profile.Name; result.CompanyStatus = profile.Status; result.CompanyType = profile.Type; result.Address = profile.Address;
            Report(result, "Finding latest accounts filing");
            var filing = AccountsFilingService.Latest(await api.GetFilingsAsync(number, ct));
            if (filing == null) { result.Status = "No Accounts"; result.Evidence = "No relevant annual accounts filing was found."; return result; }
            result.FilingDate = filing.FilingDate; result.PeriodEnd = filing.PeriodEnd;
            result.SourceUrl = $"https://find-and-update.company-information.service.gov.uk/company/{number}/filing-history/{Uri.EscapeDataString(filing.Id)}/document";
            if (filing.PeriodEnd == null) { result.Status = "Needs Review"; result.Evidence = "The most recent accounts filing has no identifiable reporting period. Older accounts were not substituted."; return result; }
            if (filing.MetadataUrl == null) { result.Status = "Document Unavailable"; return result; }
            result.MetadataUrl = filing.MetadataUrl;
            Report(result, "Checking available document formats");
            var metadata = await documents.MetadataAsync(filing.MetadataUrl, ct, number);
            Extraction? extraction = null;
            foreach (var type in new[] { "application/xhtml+xml", "application/xml" }.Where(metadata.Types.Contains))
            {
                Report(result, "Downloading structured accounts");
                try
                {
                    var bytes = await documents.DownloadAsync(metadata.ContentUrl, type, ct, update => DownloadUpdate(result, update));
                    Report(result, "Reading structured revenue facts");
                    extraction = await Task.Run(() => xbrl.Extract(bytes, filing.PeriodEnd), ct);
                    extraction = new RevenueValidationService().Validate(extraction);
                    diagnostics.Write("structured_identification", extraction.Status, extraction.Confidence);
                }
                catch (Exception ex) when (ex is XmlException or InvalidOperationException or OverflowException or ApiException or IOException)
                {
                    diagnostics.Write("structured_parsing", "failed", ex.GetType().Name);
                    var unavailable = ex is ApiException apiError && apiError.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NotAcceptable;
                    extraction = new(null, "", filing.PeriodEnd, "XBRL", unavailable ? "Document Unavailable" : "Extraction Failed", "", "Structured document could not be read safely; attempting another advertised representation.");
                }
                if (extraction.Status == "Verified") break;
            }
            if (extraction?.Status != "Verified" && metadata.Types.Contains("application/pdf"))
            {
                Report(result, "Downloading accounts PDF");
                var bytes = await documents.DownloadAsync(metadata.ContentUrl, "application/pdf", ct, update => DownloadUpdate(result, update));
                result.DocumentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                var sourceFolder = Path.Combine(settings.Root, "source-documents"); Directory.CreateDirectory(sourceFolder);
                result.LocalDocumentPath = Path.Combine(sourceFolder, result.DocumentHash + ".pdf");
                var validSavedCopy = File.Exists(result.LocalDocumentPath) && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(result.LocalDocumentPath, ct))) == result.DocumentHash;
                if (!validSavedCopy)
                {
                    var temporary = result.LocalDocumentPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { await File.WriteAllBytesAsync(temporary, bytes, ct); File.Move(temporary, result.LocalDocumentPath, true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                if (settings.Settings.SaveSourcePdfs) { Directory.CreateDirectory(settings.Settings.DownloadFolder); await File.WriteAllBytesAsync(Path.Combine(settings.Settings.DownloadFolder, $"{number}-{filing.PeriodEnd:yyyyMMdd}-{filing.Id}.pdf"), bytes, ct); }
                var pdfResult = await Task.Run(() => pdf.ExtractAsync(bytes, filing.PeriodEnd, ct, (page, total) => Report(result, $"Reading PDF text page {page} of {total}"), activity => Report(result, activity)), ct);
                // Preserve structured ambiguity if the PDF offers no stronger evidence.
                extraction = extraction?.Confidence == "Ambiguous" ? pdfResult with { Candidates = pdfResult.Candidates.Concat(extraction.Candidates).ToList(), Evidence = extraction.Evidence + "\n" + pdfResult.Evidence } : pdfResult;
            }
            extraction ??= new(null, "", filing.PeriodEnd, "", "Document Unavailable", "", "No supported XML, XHTML or PDF representation is advertised by the document metadata.");
            extraction = new RevenueValidationService().Validate(extraction);
            result.Candidates = extraction.Candidates; result.ReportingEntity = extraction.ReportingEntity; result.ConsolidatedAccounts = extraction.ConsolidatedAccounts;
            result.Revenue = extraction.Value; result.Currency = extraction.Currency; result.PeriodEnd = extraction.PeriodEnd;
            result.Method = extraction.Method; result.Status = extraction.Status; result.Confidence = extraction.Confidence; result.Evidence = extraction.Evidence;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            result.Status = "Needs Review";
            result.Evidence = deadline.IsCancellationRequested
                ? $"The {searchBudget.TotalSeconds:N0}-second company search limit was reached during: {result.Stage}. Company details were retained. Retry or download the source for manual review."
                : "Document analysis was interrupted. Download the source to review it manually, or retry extraction.";
        }
        catch (Exception ex)
        {
            result.Status = ex is ApiException apiError && apiError.StatusCode == System.Net.HttpStatusCode.NotFound && result.MetadataUrl.Length > 0 ? "Document Unavailable" : "Extraction Failed";
            diagnostics.Write("processing", "failed", $"Company {number}; stage {result.Stage}; {ex.GetType().Name}");
            result.Error = ex is ApiException or IOException or TimeoutException or InvalidOperationException ? ex.Message : "The document could not be processed. Review the original filing or retry.";
            try { await File.AppendAllTextAsync(Path.Combine(settings.Root, "failures.log"), $"{DateTimeOffset.UtcNow:O} {number} {ex.GetType().Name} {result.Status}\n", CancellationToken.None); }
            catch (IOException) { /* Failure to write diagnostics must not discard the result. */ }
        }
        result.Stage = result.Status;
        return result;
    }
}

