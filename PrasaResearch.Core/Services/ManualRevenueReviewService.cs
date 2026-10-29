using System.Text.RegularExpressions;
using System.Security.Cryptography;
using PrasaResarch.Models;
using PrasaResarch.Data;

namespace PrasaResarch.Services;
public class ManualRevenueReviewService(JobPersistenceService store)
{
    public CompanyResult Confirm(CompanyResult original, decimal amount, string currency, DateTime periodEnd, string entity, int page, string reason, bool sourceInspected)
    {
        currency = currency.Trim().ToUpperInvariant();
        if (!sourceInspected || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10) throw new ArgumentException("Inspect the original PDF, confirm the source checkbox, and enter a review reason of at least 10 characters.");
        if (!Regex.IsMatch(currency, "^[A-Z]{3}$") || entity is not "Company" and not "Group" || page < 1) throw new ArgumentException("Select Company or Group, a three-letter currency and a positive source page.");
        if (original.PeriodEnd == null || original.PeriodEnd.Value.Date != periodEnd.Date) throw new ArgumentException("The reviewed reporting period must match the selected filing's accounts period end.");
        if ((original.ConsolidatedAccounts || original.Candidates.Any(c => c.Statement && c.Entity == "Group")) && entity != "Group") throw new ArgumentException("These accounts establish a consolidated group basis. Confirm the latest group turnover, not the individual company figure.");
        if (string.IsNullOrWhiteSpace(original.SourceUrl) || !File.Exists(original.LocalDocumentPath) || original.DocumentHash.Length == 0) throw new ArgumentException("Download/retry this filing first so its original PDF and hash are available for review.");
        var bytes = File.ReadAllBytes(original.LocalDocumentPath);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != original.DocumentHash) throw new InvalidDataException("The saved PDF differs from the extracted source. Retry before confirming a value.");
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes); if (page > pdf.NumberOfPages) throw new ArgumentException("The source page is outside this PDF.");
        var decision = new ManualRevenueDecision(amount, currency, periodEnd, entity, page, original.SourceUrl, original.DocumentHash, reason.Trim(), original.Evidence, Environment.UserName, DateTimeOffset.UtcNow);
        var verified = original.Snapshot(); verified.Revenue = amount; verified.Currency = currency; verified.ReportingEntity = entity;
        verified.Status = "Verified"; verified.Stage = "Verified"; verified.Method = "Manual review"; verified.Confidence = "Manually verified — user confirmation";
        verified.Evidence = $"Manual confirmation by {decision.Reviewer} at {decision.At:O}. Page {page}; entity {entity}; period {periodEnd:yyyy-MM-dd}; {currency} {amount}. Reason: {decision.Reason}\n" + original.Evidence;
        store.SaveManualDecision(original, verified, decision); return verified;
    }
}
