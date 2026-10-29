using PrasaResarch.Models;

namespace PrasaResarch.Services;
public class RevenueValidationService
{
    public Extraction Validate(Extraction input)
    {
        if (input.Status is not "Found" and not "Verified" && input.Candidates.Count == 0) return input;
        if (input.Confidence is "OCR disagreement" or "Incomplete facts" or "Incomplete page coverage" || input.Candidates.Any(c => c.Issue.StartsWith("Alternate OCR pass", StringComparison.Ordinal)))
            return input with { Value = null, Status = "Needs Review" };
        if (input.PeriodEnd.HasValue && input.Candidates.Any(c => c.Statement && c.Year > input.PeriodEnd.Value.Year))
            return input with { Value = null, Status = "Needs Review", Confidence = "Statement year does not match latest filing period" };
        var current = input.Candidates.Where(c => c.Year == input.PeriodEnd?.Year && c.Exact && c.Statement).ToList();
        bool consolidated = input.ConsolidatedAccounts || input.Candidates.Any(c => c.Statement && c.Entity == "Group");
        if (consolidated) current = current.Where(c => c.Entity != "Company").ToList();
        var valid = current.Where(c => c.Aligned && c.Confidence >= 85 && c.LabelConfidence >= 85 && c.EntityConfidence >= 85 && c.Issue.Length == 0 && c.Amount.HasValue && c.Currency.Length == 3 && c.Entity is "Company" or "Group").ToList();
        var choices = valid.DistinctBy(c => (c.Amount, c.Currency, c.Entity)).ToList();
        bool unresolved = current.Any(c => !c.Amount.HasValue || c.Issue.Length > 0 || c.Confidence < 85 || c.LabelConfidence < 85 || c.EntityConfidence < 85 || c.Entity == "Unknown" && !valid.Any(v => v.Amount == c.Amount && v.Currency == c.Currency));
        if (choices.Count != 1 || unresolved || input.PeriodEnd == null)
            return input with { Value = null, Status = "Needs Review", ConsolidatedAccounts = consolidated, Confidence = "Year, entity or competing values unresolved", Evidence = "Automatic verification requires an exact statement amount aligned to the reporting year with currency, scale and reporting entity established.\n" + input.Evidence };
        var chosen = choices[0];
        var support = input.Candidates.Where(c => !c.Statement && c.Amount.HasValue && c.Currency == chosen.Currency && (c.Entity == "Unknown" || c.Entity == chosen.Entity) && (!c.Year.HasValue || c.Year == chosen.Year)
            && (c.Exact ? c.Amount == chosen.Amount : Math.Abs(c.Amount.Value - chosen.Amount!.Value) <= c.Scale / 2)).ToList();
        return input with { Value = chosen.Amount, Currency = chosen.Currency, Status = "Verified", ReportingEntity = chosen.Entity, ConsolidatedAccounts = consolidated,
            Confidence = $"Verified statement context; {support.Select(c => c.Page).Distinct().Count()} supporting pages",
            Evidence = $"Selected latest annual {(consolidated ? "consolidated Group" : chosen.Entity)} turnover for {chosen.Year}: {chosen.Currency} {chosen.Amount:N2}, scale {chosen.Scale}. Source page/fact: {chosen.Page?.ToString() ?? chosen.FactReference}. Comparative years are audit evidence only. Rounded evidence is corroboration only.\n" + input.Evidence };
    }
}
