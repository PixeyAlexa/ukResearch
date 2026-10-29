using System.Globalization;
using System.Text.RegularExpressions;
using PrasaResarch.Models;

namespace PrasaResarch.Services;
public record PdfWord(string Text, double Left, double Right, double Bottom, double Confidence = 100);
public class PdfTableRevenueReader
{
    public static List<List<PdfWord>> Lines(IEnumerable<PdfWord> words)
    {
        var lines = new List<List<PdfWord>>();
        foreach (var word in words.OrderByDescending(w => w.Bottom))
        {
            var line = lines.LastOrDefault(l => Math.Abs(l.Average(w => w.Bottom) - word.Bottom) <= 3.5);
            if (line == null) lines.Add([word]); else line.Add(word);
        }
        return lines.Select(l => l.OrderBy(w => w.Left).ToList()).ToList();
    }
    private static string Text(IEnumerable<PdfWord> words) => string.Join(" ", words.Select(w => w.Text));
    private const string Label = @"^\s*(?:total\s+revenue|revenue\s+from\s+contracts\s+with\s+customers|turnover|revenue|sales)\b";
    public List<RevenueCandidate> Analyze(IEnumerable<(int Page, IReadOnlyList<PdfWord> Words)> pages)
    {
        var results = new List<RevenueCandidate>();
        foreach (var page in pages)
        {
            var lines = Lines(page.Words); var pageText = string.Join("\n", lines.Select(Text));
            var audited = Regex.IsMatch(pageText, @"\baudited\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(pageText, @"\bunaudited\b", RegexOptions.IgnoreCase);
            for (int row = 0; row < lines.Count; row++)
            {
                var text = Text(lines[row]); var match = Regex.Match(text, Label, RegexOptions.IgnoreCase);
                if (!match.Success || Regex.IsMatch(text[match.Length..], @"^\s*(?:growth|margin|recognition|policy|per\s+employee|percentage)\b", RegexOptions.IgnoreCase)) continue;
                var headerLines = lines.Take(row).TakeLast(22).ToList();
                var heading = string.Join("\n", headerLines.Select(Text));
                bool statement = Regex.IsMatch(heading, @"income\s*statement|profit\s*and\s*loss|statement\s*of\s*(?:comprehensive\s*)?income|statement\s*of\s*profit", RegexOptions.IgnoreCase);
                var yearLine = headerLines.LastOrDefault(l => l.Count(w => Regex.IsMatch(w.Text, @"^(?:19|20)\d{2}$")) >= 2 || l.Count <= 2 && l.Any(w => Regex.IsMatch(w.Text, @"^(?:19|20)\d{2}$")));
                var years = yearLine?.Where(w => Regex.IsMatch(w.Text, @"^(?:19|20)\d{2}$")).OrderBy(w => w.Right).ToList() ?? [];
                var scope = Scope(heading);
                var currency = Currency(heading + "\n" + text);
                var scale = Scale(heading);
                if (years.Count > 0 && statement)
                {
                    for (int column = 0; column < years.Count; column++)
                    {
                        var year = years[column];
                        var gap = years.Count > 1 ? years.Where(y => y != year).Min(y => Math.Abs(y.Right - year.Right)) : 120;
                        // Right-aligned figures may extend well to the left of their year header.
                        double left = column == 0 ? year.Right - gap * .95 : (years[column - 1].Right + year.Right) / 2;
                        double right = column == years.Count - 1 ? year.Right + gap * .35 : (year.Right + years[column + 1].Right) / 2;
                        var tokens = lines[row].Where(w => w.Right > left && w.Right <= right && Regex.IsMatch(w.Text, @"^[\dOoIl,.'\u2019\s()\u2212+\-\u00a3\u20ac$]+$")).ToList();
                        var clusters = new List<List<PdfWord>>();
                        foreach (var token in tokens)
                        {
                            if (clusters.Count == 0 || token.Left - clusters[^1][^1].Right > 20) clusters.Add([token]);
                            else clusters[^1].Add(token);
                        }
                        // A detached note reference must never be concatenated into the monetary amount.
                        tokens = clusters.OrderBy(c => Math.Abs(c[^1].Right - year.Right)).FirstOrDefault() ?? [];
                        string raw = Text(tokens);
                        bool normalized = Regex.IsMatch(raw, @"[OoIl]");
                        var number = NormalizeNumber(raw);
                        decimal? amount = decimal.TryParse(number, NumberStyles.Number | NumberStyles.AllowParentheses, CultureInfo.InvariantCulture, out var parsed) ? parsed * (scale ?? 1) : null;
                        string entity = scope;
                        double entityConfidence = headerLines.SelectMany(h => h).Where(w => Regex.IsMatch(w.Text, @"\b(?:group|company|parent|individual|consolidated)\b", RegexOptions.IgnoreCase)).Select(w => w.Confidence).DefaultIfEmpty(0).Max();
                        var scopeLine = headerLines.LastOrDefault(l => l.Any(w => Regex.IsMatch(w.Text, @"^(?:group|company|parent)$", RegexOptions.IgnoreCase)) && l.Count(w => Regex.IsMatch(w.Text, @"^(?:group|company|parent)$", RegexOptions.IgnoreCase)) > 1);
                        if (scopeLine != null)
                        {
                            var labels = scopeLine.Where(w => Regex.IsMatch(w.Text, @"^(?:group|company|parent)$", RegexOptions.IgnoreCase)).ToList();
                            var closest = labels.OrderBy(w => Math.Abs((w.Left + w.Right) / 2 - year.Right)).First();
                            entity = closest.Text.Equals("group", StringComparison.OrdinalIgnoreCase) ? "Group" : "Company";
                            entityConfidence = closest.Confidence;
                        }
                        var issue = amount == null ? "Amount could not be parsed" : currency.Length == 0 ? "Currency missing or ambiguous" : scale == null ? "Scale ambiguous" : normalized ? "OCR character substitution requires review" : "";
                        if (tokens.Count > 0 && Math.Abs(tokens[^1].Right - year.Right) > Math.Min(30, gap * .3)) issue = "Amount/year alignment uncertain";
                        var currencyWords = headerLines.SelectMany(h => h).Where(w => Currency(w.Text) == currency && currency.Length > 0).ToList();
                        var currencyConfidence = currencyWords.Select(w => w.Confidence).DefaultIfEmpty(0).Max();
                        var labelConfidence = lines[row].Take(match.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).Select(w => w.Confidence).DefaultIfEmpty(0).Min();
                        results.Add(new() { Page = page.Page, Year = int.Parse(year.Text, CultureInfo.InvariantCulture), Amount = amount, Currency = currency, Scale = scale ?? 1, Entity = entity, EntityConfidence = entityConfidence, Exact = true, Statement = true, Audited = audited, Aligned = tokens.Count > 0 && !issue.Contains("alignment"), Confidence = Math.Min(currencyConfidence, tokens.Concat([year]).Select(w => w.Confidence).DefaultIfEmpty(0).Min()), LabelConfidence = labelConfidence, RawText = text, Headings = heading, Issue = issue, Left = tokens.FirstOrDefault()?.Left ?? 0, Right = tokens.LastOrDefault()?.Right ?? 0, Bottom = lines[row][0].Bottom });
                    }
                }
                else
                {
                    // Narrative and rounded measures are retained as support, never selected as exact statement revenue.
                    foreach (Match value in Regex.Matches(text[match.Length..], @"(?<currency>\u00a3|\u20ac|GBP|EUR|USD)?\s*(?<amount>\d[\d,]*(?:\.\d+)?)\s*(?<scale>[MmKk]|million|thousand)?\b"))
                    {
                        if (!decimal.TryParse(value.Groups["amount"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) continue;
                        var unit = value.Groups["scale"].Value.ToLowerInvariant(); var factor = unit is "m" or "million" ? 1_000_000m : unit is "k" or "thousand" ? 1000m : 1m;
                        results.Add(new() { Page = page.Page, Amount = n * factor, Currency = currency, Scale = factor, Entity = scope, Exact = factor == 1, Statement = false, Confidence = lines[row].Min(w => w.Confidence), RawText = text, Headings = heading, Issue = "Corroboration only; year not aligned" });
                    }
                }
            }
        }
        return results;
    }
    public static string NormalizeNumber(string value) => Regex.Replace(value.Replace("\u2212", "-").Replace('O', '0').Replace('o', '0').Replace('I', '1').Replace('l', '1'), @"[\s,\u00a3\u20ac$'\u2019]", "");
    public static string Scope(string text)
    {
        bool group = Regex.IsMatch(text, @"\bconsolidated\b|\bgroup\b", RegexOptions.IgnoreCase);
        bool company = Regex.IsMatch(text, @"\bcompany\b|\bindividual\b|\bparent\b", RegexOptions.IgnoreCase);
        return group && !company ? "Group" : company && !group ? "Company" : "Unknown";
    }
    private static string Currency(string text)
    {
        var found = new List<string>();
        foreach (var c in new[] { "GBP", "EUR", "USD", "CAD", "AUD", "CHF", "JPY" })
            if (Regex.IsMatch(text, @"\b" + c + @"\b", RegexOptions.IgnoreCase) || c == "GBP" && Regex.IsMatch(text, @"\u00a3|pounds\s+sterling", RegexOptions.IgnoreCase) || c == "EUR" && text.Contains('\u20ac')) found.Add(c);
        return found.Count == 1 ? found[0] : "";
    }
    private static decimal? Scale(string text)
    {
        bool m = Regex.IsMatch(text, @"million|[\u00a3\u20ac$]\s*m\b|(?:GBP|EUR|USD)\s*m\b", RegexOptions.IgnoreCase);
        bool k = Regex.IsMatch(text, @"thousand|[\u00a3\u20ac$]\s*['\u2019]?000|\b(?:GBP|EUR|USD|CAD|AUD|CHF|JPY)\s*['\u2019]?000", RegexOptions.IgnoreCase);
        return m && k ? null : m ? 1_000_000 : k ? 1000 : 1;
    }
    public Extraction? Extract(IEnumerable<(int Page, IReadOnlyList<PdfWord> Words)> pages, DateTime? periodEnd)
    {
        var materialized = pages.ToList();
        var candidates = Analyze(materialized);
        if (candidates.Count == 0) return null;
        bool consolidated = candidates.Any(c => c.Statement && c.Entity == "Group") || materialized.Any(p => Regex.IsMatch(Text(p.Words), @"consolidated\s*(?:financial|accounts|income|statement|profit)|group\s*(?:financial|accounts|income|statement|profit)", RegexOptions.IgnoreCase));
        var valid = candidates.Where(c => c.Statement && c.Exact && c.Aligned && c.Issue.Length == 0 && c.Year == periodEnd?.Year && c.Amount.HasValue && c.Confidence >= 85).ToList();
        if (consolidated) valid = valid.Where(c => c.Entity == "Group").ToList();
        var values = valid.DistinctBy(c => (c.Amount, c.Currency, c.Entity)).ToList();
        var success = values.Count == 1 && !candidates.Any(c => c.Statement && c.Year == periodEnd?.Year && (!consolidated || c.Entity != "Company") && (c.Confidence < 85 || c.Issue.Length > 0));
        return new(success ? values[0].Amount : null, success ? values[0].Currency : "", periodEnd, "PDF table", success ? "Found" : "Needs Review", success ? "Validated table context" : "Ambiguous or incomplete candidates", string.Join("\n", candidates.Select(c => c.Display + "; " + c.RawText))) { Candidates = candidates, ReportingEntity = success ? values[0].Entity : "Unknown", ConsolidatedAccounts = consolidated };
    }
}
