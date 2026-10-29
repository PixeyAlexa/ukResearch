using ClosedXML.Excel;
using CsvHelper;
using System.Globalization;
using PrasaResarch.Models;

namespace PrasaResarch.Services;
public class ExportService
{
    private static readonly string[] Headers = ["Company Registration Number", "Company Name", "Latest Revenue", "Currency", "Financial Year", "Extraction Status", "Source Document"];
    private static object?[] Values(CompanyResult r) => [r.CompanyNumber, r.CompanyName, r.Status == "Verified" ? r.Revenue : null, r.Currency, r.FinancialYear, r.PrimaryStatus, r.SourceUrl];
    public void Export(string path, IEnumerable<CompanyResult> source)
    {
        var results = source.GroupBy(r => CompanyNumber.Normalize(r.CompanyNumber)).Select(g => g.OrderByDescending(r => r.PeriodEnd ?? DateTime.MinValue).ThenByDescending(r => r.FilingDate ?? DateTime.MinValue).First()).ToList();
        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(true)); using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            foreach (var h in Headers) csv.WriteField(h); csv.NextRecord();
            foreach (var r in results) { foreach (var v in Values(r)) csv.WriteField(v is string s ? SafeCsv(s) : v); csv.NextRecord(); }
        }
        else
        {
            using var workbook = new XLWorkbook(); var sheet = workbook.AddWorksheet("Revenue results");
            for (int i = 0; i < Headers.Length; i++) sheet.Cell(1, i + 1).Value = Headers[i];
            int row = 2;
            foreach (var r in results)
            {
                var values = Values(r);
                for (int i = 0; i < values.Length; i++)
                {
                    var cell = sheet.Cell(row, i + 1);
                    if (values[i] is decimal d) cell.Value = d;
                    else if (values[i] is int n) cell.Value = n;
                    else cell.Value = values[i]?.ToString() ?? "";
                }
                row++;
            }
            sheet.Column(1).Style.NumberFormat.Format = "@"; sheet.Column(3).Style.NumberFormat.Format = "#,##0.00;[Red](#,##0.00)";
            var header = sheet.Range(1, 1, 1, Headers.Length); header.Style.Fill.BackgroundColor = XLColor.FromHtml("#172748"); header.Style.Font.FontColor = XLColor.White; header.Style.Font.Bold = true;
            sheet.Range(1, 1, Math.Max(1, row - 1), Headers.Length).SetAutoFilter(); sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents(8, 55); workbook.SaveAs(path);
        }
    }
    private static string SafeCsv(string s) => s.Length > 0 && "=+-@\t\r".Contains(s[0]) ? "'" + s : s;
}
