using System.Text.Json;

namespace PrasaResarch.Services;
public sealed class ExtractionDiagnostics
{
    private static readonly object Gate = new();
    private readonly string path;
    public ExtractionDiagnostics(string? root = null)
    {
        root ??= PlatformPaths.Data;
        Directory.CreateDirectory(root); path = Path.Combine(root, "extraction.jsonl");
    }
    public void Write(string stage, string status, string detail, int? page = null)
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 5_000_000) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, stage, status, page, detail }) + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
public static class PdfContentValidator
{
    public static void Validate(byte[] bytes)
    {
        if (bytes.Length < 8 || !System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(1024, bytes.Length)).Contains("%PDF-", StringComparison.Ordinal))
            throw new InvalidDataException("The downloaded content is not a PDF (missing PDF signature). An HTML/JSON error response may have been returned.");
        if (!System.Text.Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 4096), Math.Min(4096, bytes.Length)).Contains("%%EOF", StringComparison.Ordinal))
            throw new InvalidDataException("The PDF download appears incomplete: its end marker is missing.");
    }
}

