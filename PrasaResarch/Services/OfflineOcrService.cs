using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using PrasaResarch.Models;

namespace PrasaResarch.Services;
public record OcrPage(IReadOnlyList<PdfWord> Words, string Text, double Confidence, bool FromCache = false);
public interface IOfflineOcr
{
    bool IsAvailable(out string reason);
    Task<OcrPage> ReadPageAsync(byte[] pdf, int page, int segmentation, CancellationToken ct);
}
public sealed class OfflineOcrService(AppSettings settings, ExtractionDiagnostics diagnostics) : IOfflineOcr
{
    private static readonly SemaphoreSlim Workers = new(2);
    private static readonly SemaphoreSlim[] CacheGates = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1)).ToArray();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], string> Hashes = new();
    public bool IsAvailable(out string reason)
    {
        if (!settings.EnableOcr) { reason = "OCR is disabled in Settings."; return false; }
        if (!File.Exists(settings.TesseractPath)) { reason = "Install Tesseract 5 and set the executable path in Settings."; return false; }
        var data = Path.Combine(Path.GetDirectoryName(settings.TesseractPath)!, "tessdata", "eng.traineddata");
        if (!File.Exists(data)) { reason = "Tesseract English language data (tessdata/eng.traineddata) is missing."; return false; }
        reason = "Tesseract and English language data available."; return true;
    }
    public async Task<OcrPage> ReadPageAsync(byte[] pdf, int page, int segmentation, CancellationToken ct)
    {
        if (!IsAvailable(out var reason)) throw new InvalidOperationException(reason);
        var hash = Hashes.GetValue(pdf, bytes => Convert.ToHexString(SHA256.HashData(bytes)));
        var dataPath = Path.Combine(Path.GetDirectoryName(settings.TesseractPath)!, "tessdata", "eng.traineddata");
        var identity = $"ocr-v3-utf8-300dpi-eng|{hash}|{page}|{segmentation}|{settings.TesseractPath}|{File.GetLastWriteTimeUtc(settings.TesseractPath).Ticks}|{File.GetLastWriteTimeUtc(dataPath).Ticks}|{new FileInfo(dataPath).Length}";
        var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrasaResearch", "ocr-cache");
        Directory.CreateDirectory(cacheRoot);
        var cachePath = Path.Combine(cacheRoot, key + ".json");
        var gate = CacheGates[Convert.ToInt32(key[..2], 16) % CacheGates.Length];
        await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(cachePath))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<OcrPage>(await File.ReadAllTextAsync(cachePath, ct));
                    if (cached != null && cached.Words != null && cached.Text != null)
                    {
                        diagnostics.Write("ocr_cache", "hit", $"PSM {segmentation}; document hash {hash}", page);
                        return cached with { FromCache = true };
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException) { diagnostics.Write("ocr_cache", "invalid", ex.GetType().Name, page); }
            }
            await Workers.WaitAsync(ct);
            OcrPage result;
            try { result = await ReadUncachedAsync(pdf, page, segmentation, ct); }
            finally { Workers.Release(); }
            var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result), ct); File.Move(temporary, cachePath, true); }
            catch (IOException) { diagnostics.Write("ocr_cache", "write_failed", "OCR result retained in memory", page); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return result;
        }
        finally { gate.Release(); }
    }
    private async Task<OcrPage> ReadUncachedAsync(byte[] pdf, int page, int segmentation, CancellationToken ct)
    {
        var root = Path.GetFullPath(settings.TemporaryFolder);
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, "ocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var image = Path.Combine(directory, "page.png");
            using var stream = new MemoryStream(pdf, false);
            ct.ThrowIfCancellationRequested();
            PDFtoImage.Conversion.SavePng(image, stream, page: page - 1, options: new PDFtoImage.RenderOptions(Dpi: 300));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var start = new ProcessStartInfo(settings.TesseractPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            start.Environment["OMP_THREAD_LIMIT"] = "2";
            foreach (var arg in new[] { image, "stdout", "--tessdata-dir", Path.Combine(Path.GetDirectoryName(settings.TesseractPath)!, "tessdata"), "-l", "eng", "--psm", segmentation.ToString(CultureInfo.InvariantCulture), "tsv" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Tesseract could not be started.");
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                if (ct.IsCancellationRequested) throw;
                throw new TimeoutException("Tesseract exceeded the 45-second page limit.");
            }
            await error; // Drain stderr without logging document text or local command arguments.
            if (process.ExitCode != 0) throw new IOException($"Tesseract failed with exit code {process.ExitCode}. Check executable dependencies and English language data.");
            var result = ParseTsv(await output);
            diagnostics.Write("ocr", "completed", $"PSM {segmentation}; {result.Words.Count} words; mean confidence {result.Confidence:F1}", page);
            return result;
        }
        finally
        {
            // Only remove the unique working directory created by this invocation.
            var target = Path.GetFullPath(directory);
            if (target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(target, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { diagnostics.Write("ocr_cleanup", "failed", ex.GetType().Name, page); }
        }
    }
    public static OcrPage ParseTsv(string tsv)
    {
        var words = new List<PdfWord>(); var confidence = new List<double>();
        foreach (var line in tsv.Split('\n').Skip(1))
        {
            var cells = line.TrimEnd('\r').Split('\t');
            if (cells.Length < 12 || cells[0] != "5" || string.IsNullOrWhiteSpace(cells[11])) continue;
            if (!double.TryParse(cells[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var conf)) continue;
            if (!double.TryParse(cells[6], out var left) || !double.TryParse(cells[7], out var top) || !double.TryParse(cells[8], out var width) || !double.TryParse(cells[9], out var height)) continue;
            // Convert pixel positions to PDF points; PDF y increases upward.
            words.Add(new(cells[11], left * 72 / 300, (left + width) * 72 / 300, -(top + height) * 72 / 300, conf)); confidence.Add(conf);
        }
        return new(words, string.Join("\n", PdfTableRevenueReader.Lines(words).Select(l => string.Join(" ", l.Select(w => w.Text)))), confidence.Count == 0 ? 0 : confidence.Average());
    }
}
