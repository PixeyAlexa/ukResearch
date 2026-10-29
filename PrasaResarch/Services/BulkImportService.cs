using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using PrasaResarch.Models;

namespace PrasaResarch.Services;
public class BulkImportService
{
    public ImportTable Read(string path)
    {
        var rows = new List<string[]>();
        if (Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var book = new XLWorkbook(path); var sheet = book.Worksheets.First();
            int columns = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (columns > 256) throw new InvalidDataException("Choose a sheet with no more than 256 columns.");
            foreach (var row in sheet.RowsUsed()) rows.Add(Enumerable.Range(1, columns).Select(i => row.Cell(i).GetFormattedString(CultureInfo.InvariantCulture)).ToArray());
        }
        else if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(path);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = false, DetectDelimiter = true });
            while (csv.Read()) rows.Add(Enumerable.Range(0, csv.Parser.Count).Select(i => csv.GetField(i) ?? "").ToArray());
        }
        else throw new ArgumentException("Select an Excel (.xlsx) or CSV (.csv) file.");
        rows = rows.Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
        if (rows.Count == 0) throw new InvalidDataException("The file contains no data.");
        var first = rows[0];
        bool header = first.Any(v => v.Contains("company", StringComparison.OrdinalIgnoreCase) || v.Contains("registration", StringComparison.OrdinalIgnoreCase));
        int width = rows.Max(r => r.Length);
        var names = Enumerable.Range(0, width).Select(i => header && i < first.Length ? $"{i + 1}: {first[i]}" : $"Column {i + 1}").ToArray();
        if (header) rows.RemoveAt(0);
        return new(names, rows);
    }
    public int DetectColumn(ImportTable table) => Enumerable.Range(0, table.Columns.Length).OrderByDescending(i => table.Rows.Take(100).Count(r => i < r.Length && IsNumber(r[i]))).First();
    private static bool IsNumber(string input) { try { CompanyNumber.Normalize(input); return true; } catch (ArgumentException) { return false; } }
    public ImportPreview Preview(ImportTable table, int column)
    {
        var numbers = new HashSet<string>(); var invalid = new List<string>(); int duplicates = 0;
        foreach (var row in table.Rows)
        {
            var value = column < row.Length ? row[column].Trim() : "";
            if (string.IsNullOrWhiteSpace(value)) continue;
            try { if (!numbers.Add(CompanyNumber.Normalize(value))) duplicates++; }
            catch (ArgumentException) { invalid.Add(value); }
        }
        return new(numbers.ToList(), invalid, duplicates);
    }
}
