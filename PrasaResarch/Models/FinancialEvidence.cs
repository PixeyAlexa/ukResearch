namespace PrasaResarch.Models;
public record RevenueCandidate
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int? Page { get; init; }
    public string FactReference { get; init; } = "";
    public int? Year { get; init; }
    public decimal? Amount { get; init; }
    public string Currency { get; init; } = "";
    public decimal Scale { get; init; } = 1;
    public string Entity { get; init; } = "Unknown";
    public bool Exact { get; init; }
    public bool Statement { get; init; }
    public bool Audited { get; init; }
    public bool Aligned { get; init; }
    public double Confidence { get; init; }
    public double LabelConfidence { get; init; } = 100;
    public double EntityConfidence { get; init; } = 100;
    public string RawText { get; init; } = "";
    public string Headings { get; init; } = "";
    public string Issue { get; init; } = "";
    public double Left { get; init; }
    public double Right { get; init; }
    public double Bottom { get; init; }
    public string Display => $"Page {Page?.ToString() ?? FactReference} | {Year} | {Entity} | {Currency} {Amount:N2} | {(Exact ? "Exact" : "Rounded/support only")} | {Issue}";
}
public record ManualRevenueDecision(decimal Amount, string Currency, DateTime PeriodEnd, string Entity, int Page, string SourceUrl, string DocumentHash, string Reason, string Evidence, string Reviewer, DateTimeOffset At);
