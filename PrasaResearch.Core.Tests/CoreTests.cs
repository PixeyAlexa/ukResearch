using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using ClosedXML.Excel;
using PrasaResarch.Data;
using PrasaResarch.Models;
using PrasaResarch.Services;

namespace PrasaResarch.Tests;
public class CoreTests
{
    [Theory]
    [InlineData("1234", "00001234")]
    [InlineData(" sc 012345 ", "SC012345")]
    [InlineData("00000000", "00000000")]
    [InlineData("OE123456", "OE123456")]
    [InlineData("r0000283", "R0000283")]
    public void NormalizePreservesPrefixesAndZeros(string input, string expected) => Assert.Equal(expected, CompanyNumber.Normalize(input));
    [Theory]
    [InlineData("")][InlineData("123456789")][InlineData("AB-12345")][InlineData("<script>")]
    public void InvalidNumberRejected(string input) => Assert.Throws<ArgumentException>(() => CompanyNumber.Normalize(input));

    private static byte[] Xbrl(string facts, string currency = "GBP") => Encoding.UTF8.GetBytes($"""
      <xbrl xmlns="http://www.xbrl.org/2003/instance" xmlns:t="urn:taxonomy" xmlns:ix="http://www.xbrl.org/2013/inlineXBRL" xmlns:iso4217="http://www.xbrl.org/2003/iso4217">
       <context id="now"><entity><identifier scheme="test">123</identifier></entity><period><startDate>2024-01-01</startDate><endDate>2024-12-31</endDate></period></context>
       <context id="prior"><entity><identifier scheme="test">123</identifier></entity><period><startDate>2023-01-01</startDate><endDate>2023-12-31</endDate></period></context>
       <unit id="money"><measure>iso4217:{currency}</measure></unit>{facts}
      </xbrl>
      """);
    [Fact]
    public void StructuredCurrentYearScaleCurrencyAndSign()
    {
        var bytes = Xbrl("""
          <ix:nonFraction name="t:Turnover" contextRef="prior" unitRef="money">999999</ix:nonFraction>
          <ix:nonFraction name="t:Turnover" contextRef="now" unitRef="money" scale="3" sign="-" format="ixt:num-dot-decimal">1,234.5</ix:nonFraction>
          """, "EUR");
        var result = new XbrlExtractionService().Extract(bytes, new(2024, 12, 31));
        Assert.Equal("Found", result.Status); Assert.Equal(-1234500m, result.Value); Assert.Equal("EUR", result.Currency); Assert.Contains("context now", result.Evidence);
    }
    [Fact]
    public void StructuredAmbiguityIsNotGuessed()
    {
        var result = new XbrlExtractionService().Extract(Xbrl("<t:Revenue contextRef='now' unitRef='money'>100</t:Revenue><t:Turnover contextRef='now' unitRef='money'>200</t:Turnover>"), new(2024, 12, 31));
        Assert.Equal("Needs Review", result.Status); Assert.Null(result.Value);
    }
    [Fact]
    public void ProfitIsNotRevenue()
    {
        var result = new XbrlExtractionService().Extract(Xbrl("<t:Profit contextRef='now' unitRef='money'>100</t:Profit>"), null);
        Assert.NotEqual("Found", result.Status); Assert.Null(result.Value);
    }
    [Fact]
    public void LatestFinancialPeriodWinsOverFilingDate()
    {
        var filings = new[] { new AccountsFiling("a", new(2026, 1, 1), new(2023, 12, 31), "accounts", null), new AccountsFiling("b", new(2025, 1, 1), new(2024, 12, 31), "accounts", null) };
        Assert.Equal("b", AccountsFilingService.Latest(filings)!.Id);
    }
    [Fact]
    public void PdfScannedAndOmissionAreDistinguished()
    {
        var reader = new PdfExtractionService();
        Assert.Equal("OCR Required", reader.ExtractText([(1, "")], null).Status);
        Assert.Equal("Not Disclosed", reader.ExtractText([(1, new string(' ', 10) + "The profit and loss account has not been filed. " + new string('a', 120))], null).Status);
        var result = reader.ExtractText([(1, "Revenue 2024 2023 100 90 " + new string('a', 150))], null);
        Assert.Equal("Needs Review", result.Status); Assert.Null(result.Value);
        Assert.Equal("Needs Review", reader.ExtractText([(1, "The profit and loss account is not required to be audited. " + new string('a', 120))], null).Status);
    }
    private static IReadOnlyList<PdfWord> Table(string currency = "GBP", string amount = "1,234", bool reverse = false) =>
    [new("Income statement", 20, 130, 750), new(currency + " thousands", 20, 130, 730),
     new(reverse ? "2023" : "2024", 280, 300, 700), new(reverse ? "2024" : "2023", 380, 400, 700),
     new("Turnover", 20, 90, 680), new(reverse ? "900" : amount, 265, 300, 680), new(reverse ? amount : "900", 365, 400, 680)];
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void PdfUsesAlignedCurrentYearNotFirstNumber(bool reverse)
    {
        var result = new PdfTableRevenueReader().Extract([(1, Table(reverse: reverse))], new(2024, 12, 31));
        Assert.NotNull(result); Assert.Equal(1234000m, result.Value); Assert.Equal("GBP", result.Currency);
    }
    [Fact]
    public void ConflictingPdfTablesNeedReview()
    {
        var result = new PdfTableRevenueReader().Extract([(1, Table()), (2, Table(amount: "2,000"))], new(2024, 12, 31));
        Assert.Equal("Needs Review", result!.Status); Assert.Null(result.Value);
    }
    [Fact]
    public void ActualPdfBytesAreParsedWithCoordinates()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842);
        page.AddText("Income statement", 12, new(20, 750), font);
        page.AddText("GBP thousands", 10, new(20, 730), font);
        page.AddText("2024", 10, new(278, 700), font); page.AddText("2023", 10, new(378, 700), font);
        page.AddText("Turnover", 10, new(20, 680), font);
        page.AddText("1,234", 10, new(275, 680), font); page.AddText("900", 10, new(383, 680), font);
        var result = new PdfExtractionService().Extract(builder.Build(), new(2024, 12, 31), default);
        Assert.Equal("Found", result.Status); Assert.Equal(1234000m, result.Value); Assert.Contains("Page 1", result.Evidence);
    }
    [Fact]
    public void CsvImportHandlesQuotedFieldsDuplicatesAndInvalid()
    {
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            File.WriteAllText(path, "Company Number,Company Name\n00001234,\"A, Ltd\"\n1234,B\nsc012345,C\nbad,D\n,Empty\n");
            var service = new BulkImportService(); var table = service.Read(path); var preview = service.Preview(table, service.DetectColumn(table));
            Assert.Equal(new[] { "00001234", "SC012345" }, preview.Valid); Assert.Equal(1, preview.Duplicates); Assert.Single(preview.Invalid);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ExcelImportPreservesFormattedNumericZeros()
    {
        var path = Path.GetTempFileName() + ".xlsx";
        try
        {
            using (var book = new XLWorkbook()) { var sheet = book.AddWorksheet("Companies"); sheet.Cell(1, 1).Value = "Company Number"; sheet.Cell(2, 1).Value = 1234; sheet.Cell(2, 1).Style.NumberFormat.Format = "00000000"; book.SaveAs(path); }
            var service = new BulkImportService(); Assert.Equal("00001234", Assert.Single(service.Preview(service.Read(path), 0).Valid));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ExcelAndCsvExportPreserveExtractedValues()
    {
        var path = Path.GetTempFileName(); var source = new[] { new CompanyResult { CompanyNumber = "00001234", CompanyName = "=unsafe", Revenue = 1234.56m, Currency = "GBP", Status = "Verified" } };
        try
        {
            var service = new ExportService(); service.Export(path + ".xlsx", source);
            using var book = new XLWorkbook(path + ".xlsx"); var sheet = book.Worksheet(1);
            Assert.Equal("00001234", sheet.Cell(2, 1).GetString()); Assert.Equal(1234.56m, sheet.Cell(2, 3).GetValue<decimal>()); Assert.False(sheet.Cell(2, 2).HasFormula); Assert.Equal(7, sheet.LastColumnUsed()!.ColumnNumber());
            service.Export(path + ".csv", source); var text = File.ReadAllText(path + ".csv"); Assert.Contains("00001234", text); Assert.Contains("1234.56", text); Assert.Contains("'=unsafe", text);
        }
        finally { File.Delete(path + ".xlsx"); File.Delete(path + ".csv"); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return Task.FromResult(response(request)); }
    }
    private static SettingsService TestSettings()
    {
        // Never persist fixture credentials or overwrite the user's settings.
        var settings = new SettingsService();
        typeof(SettingsService).GetProperty(nameof(SettingsService.ApiKey))!.SetValue(settings, "offline-fixture-key");
        return settings;
    }
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)][InlineData(HttpStatusCode.Unauthorized)][InlineData(HttpStatusCode.Forbidden)][InlineData(HttpStatusCode.NotFound)]
    public async Task PermanentApiFailuresAreNotRetried(HttpStatusCode code)
    {
        var handler = new Handler(_ => new(code)); using var http = new HttpClient(handler);
        var service = new CompaniesHouseApiService(http, TestSettings(), new());
        var ex = await Assert.ThrowsAsync<ApiException>(() => service.GetProfileAsync("00001234", default));
        Assert.Equal(code, ex.StatusCode); Assert.Equal(1, handler.Calls); Assert.DoesNotContain("offline-fixture-key", ex.Message);
    }
    [Fact]
    public async Task ApiProfileParsingAndBasicAuthentication()
    {
        var handler = new Handler(r => { Assert.Equal("Basic", r.Headers.Authorization!.Scheme); return new(HttpStatusCode.OK) { Content = new StringContent("""{"company_number":"00001234","company_name":"TEST PLC","company_status":"active","type":"plc","registered_office_address":{"locality":"London"}}""") }; });
        using var http = new HttpClient(handler); var service = new CompaniesHouseApiService(http, TestSettings(), new());
        var profile = await service.GetProfileAsync("00001234", default); Assert.Equal("TEST PLC", profile.Name); Assert.Equal("London", profile.Address);
    }
    [Fact]
    public async Task ApiFilingPaginationAndDocumentLinkParsing()
    {
        var handler = new Handler(r => new(HttpStatusCode.OK) { Content = new StringContent("""{"total_count":1,"items":[{"transaction_id":"abc","date":"2025-01-01","description":"accounts-with-accounts-type-full","description_values":{"made_up_date":"2024-12-31"},"links":{"document_metadata":"https://document-api.company-information.service.gov.uk/document/test"}}]}""") });
        using var http = new HttpClient(handler); var service = new CompaniesHouseApiService(http, TestSettings(), new());
        var filing = Assert.Single(await service.GetFilingsAsync("00001234", default)); Assert.Equal(new DateTime(2024, 12, 31), filing.PeriodEnd); Assert.EndsWith("/document/test", filing.MetadataUrl);
    }
    [Fact]
    public async Task RateLimitWaitingCanBeCancelled()
    {
        var limiter = new RequestThrottle(); await limiter.WaitAsync(1, default);
        using var ct = new CancellationTokenSource(80); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limiter.WaitAsync(1, ct.Token));
    }
    private sealed class Processor : ICompanyProcessor
    {
        public async Task<CompanyResult> ProcessAsync(string n, CancellationToken ct) { await Task.Delay(50, ct); return new() { CompanyNumber = n, Status = "Not Disclosed" }; }
    }
    private sealed class Sink : IProgress<CompanyResult> { public List<CompanyResult> Items { get; } = []; public void Report(CompanyResult value) { lock (Items) Items.Add(value); } }
    [Fact]
    public async Task CancellationAndRestartRetainCompletedJobs()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var store = new JobPersistenceService(path); var numbers = Enumerable.Range(1, 10).Select(n => n.ToString("D8")).ToList();
            foreach (var n in numbers) store.Save(new() { CompanyNumber = n });
            var service = new BulkProcessingService(new Processor(), store); var sink = new Sink();
            using var ct = new CancellationTokenSource(140);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(numbers, 1, sink, ct.Token));
            var saved = new JobPersistenceService(path).Load(); Assert.Contains(saved, r => r.Status == "Pending"); Assert.Contains(saved, r => r.Status == "Not Disclosed");
            await service.RunAsync(saved.Where(r => r.Status == "Pending").Select(r => r.CompanyNumber), 2, sink, default);
            Assert.All(store.Load(), r => Assert.Equal("Not Disclosed", r.Status));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
    }
    [Fact]
    public async Task PauseBlocksNewCompaniesUntilResume()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            var store = new JobPersistenceService(path); var service = new BulkProcessingService(new Processor(), store); var sink = new Sink();
            var run = service.RunAsync(["00000001", "00000002", "00000003"], 1, sink, default);
            service.Pause(); await Task.Delay(180); Assert.InRange(sink.Items.Count, 0, 1);
            service.Resume(); await run; Assert.Equal(3, sink.Items.Count);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
    }
}
