using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PrasaResarch.Models;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Changed(name); return true; }
}
public static class CompanyNumber
{
    public static string Normalize(string value)
    {
        var n = Regex.Replace(value ?? "", @"\s", "").ToUpperInvariant();
        if (Regex.IsMatch(n, @"^\d{1,8}$")) n = n.PadLeft(8, '0');
        if (!Regex.IsMatch(n, @"^(\d{8}|R\d{7}|[A-Z]{2}\d{6}|[A-Z]{2}\d{5}[A-Z]|[A-Z]{2}\d{4}[A-Z]{2})$"))
            throw new ArgumentException("Enter a company number with up to eight digits, or a valid eight-character prefixed number.");
        return n;
    }
}
public class AppSettings : Observable
{
    public int TimeoutSeconds { get; set; } = 60;
    public int Concurrency { get; set; } = 3;
    public int RequestsPerFiveMinutes { get; set; } = 550;
    public string DownloadFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "PrasaResearch");
    public string TemporaryFolder { get; set; } = Path.Combine(Path.GetTempPath(), "PrasaResearch");
    public bool SaveSourcePdfs { get; set; }
    public bool EnableOcr { get; set; } = true;
    public string TesseractPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe");
    private int ocrMaxPages;
    public int OcrMaxPages { get => ocrMaxPages; set { if (Set(ref ocrMaxPages, value)) Changed(nameof(OcrAllPages)); } }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool OcrAllPages { get => OcrMaxPages == 0; set => OcrMaxPages = value ? 0 : (OcrMaxPages > 0 ? OcrMaxPages : 30); }
    public string Theme { get; set; } = "Midnight";
}
public record CompanyProfile(string Number, string Name, string Status, string Type, string Address);
public record AccountsFiling(string Id, DateTime FilingDate, DateTime? PeriodEnd, string Description, string? MetadataUrl);
public record Extraction(decimal? Value, string Currency, DateTime? PeriodEnd, string Method, string Status, string Confidence, string Evidence)
{
    public List<RevenueCandidate> Candidates { get; init; } = [];
    public string ReportingEntity { get; init; } = "Unknown";
    public bool ConsolidatedAccounts { get; init; }
}
public class CompanyResult
{
    public CompanyResult Snapshot() => (CompanyResult)MemberwiseClone();
    public string Stage { get; set; } = "Queued";
    public string CompanyNumber { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CompanyStatus { get; set; } = "";
    public string CompanyType { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal? Revenue { get; set; }
    public string Currency { get; set; } = "";
    public int? FinancialYear => PeriodEnd?.Year;
    public DateTime? PeriodEnd { get; set; }
    public DateTime? FilingDate { get; set; }
    public string Method { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string Confidence { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string MetadataUrl { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Error { get; set; } = "";
    public List<RevenueCandidate> Candidates { get; set; } = [];
    public string ReportingEntity { get; set; } = "Unknown";
    public bool ConsolidatedAccounts { get; set; }
    public string DocumentHash { get; set; } = "";
    public string LocalDocumentPath { get; set; } = "";
    public string PrimaryStatus => Status == "Verified" ? ReportingEntity switch { "Group" => "Verified (Consolidated Group)", "Company" => "Verified (Individual Company)", _ => Status } : Status;
    public string RevenueDisplay => Revenue.HasValue && Status == "Verified" ? (Currency switch { "GBP" => "£", "EUR" => "€", "USD" => "$", _ => Currency + " " }) + Revenue.Value.ToString("#,##0.############################", System.Globalization.CultureInfo.InvariantCulture) : "—";
}
public record ImportTable(string[] Columns, List<string[]> Rows);
public record ImportPreview(List<string> Valid, List<string> Invalid, int Duplicates);
