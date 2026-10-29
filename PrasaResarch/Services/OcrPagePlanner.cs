using System.Text.RegularExpressions;

namespace PrasaResarch.Services;
public sealed class OcrPagePlanner(int totalPages)
{
    private readonly Dictionary<int, int> scores = new();
    public void Observe(int page, string text)
    {
        const string labels = @"income\s+statement|profit\s+and\s+loss|turnover|revenue|statement\s+of\s+(?:comprehensive\s+)?income";
        if (Regex.IsMatch(text, labels, RegexOptions.IgnoreCase))
            for (int offset = -1; offset <= 2; offset++) Boost(page + offset, offset == 0 ? 100 : 60);
        foreach (var line in text.Split('\n'))
        {
            if (!Regex.IsMatch(line, labels, RegexOptions.IgnoreCase)) continue;
            var match = Regex.Match(line, @"(?:\.{2,}|\s)\s*(\d{1,4})\s*$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var target))
                // Printed page numbers can differ from PDF indices; prioritize nearby pages too.
                for (int offset = -2; offset <= 6; offset++) Boost(target + offset, 200 - Math.Abs(offset) * 5);
        }
    }
    private void Boost(int page, int score) { if (page >= 1 && page <= totalPages) scores[page] = Math.Max(scores.GetValueOrDefault(page), score); }
    public int Next(IEnumerable<int> pages) => pages.OrderByDescending(p => scores.GetValueOrDefault(p)).ThenBy(p => p).First();
}
