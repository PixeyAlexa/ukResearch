using System.IO;
using System.Text;
using PrasaResarch.Models;
using PrasaResarch.Services;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace PrasaResarch.Tests;
public sealed class OcrFactAttribute : FactAttribute
{
    public OcrFactAttribute() { if (!new OfflineOcrService(new(), new()).IsAvailable(out var reason)) Skip = reason; }
}
public class ExtractionPipelineTests
{
    internal static byte[] TextPdf(bool turnover = true, bool threeYears = false, string currencyScale = "GBP thousands", string amount = "1,234")
    {
        var book = new PdfDocumentBuilder(); var font = book.AddStandard14Font(Standard14Font.Helvetica);
        var page = book.AddPage(595, 842);
        page.AddText("Company income statement", 16, new(35, 770), font);
        page.AddText(currencyScale, 12, new(35, 735), font);
        page.AddText("2024", 12, new(272, 700), font); page.AddText("2023", 12, new(372, 700), font);
        if (threeYears) page.AddText("2022", 12, new(472, 700), font);
        page.AddText(turnover ? "Turnover" : "Gross profit", 12, new(35, 660), font);
        page.AddText(amount, 12, new(270, 660), font); page.AddText("900", 12, new(381, 660), font);
        if (threeYears) page.AddText("800", 12, new(481, 660), font);
        return book.Build();
    }
    internal static byte[] ScanPdf(string currencyScale = "GBP thousands")
    {
        using var input = new MemoryStream(TextPdf(currencyScale: currencyScale)); using var png = new MemoryStream();
#pragma warning disable CA1416 // Native Windows/macOS test runners only.
        PDFtoImage.Conversion.SavePng(png, input, page: 0, options: new PDFtoImage.RenderOptions(Dpi: 300));
#pragma warning restore CA1416
        var book = new PdfDocumentBuilder(); var page = book.AddPage(595, 842);
        page.AddPng(png.ToArray(), new(0, 0, 595, 842)); return book.Build();
    }
    private sealed class NoOcr : IOfflineOcr
    {
        public int Calls;
        public bool IsAvailable(out string reason) { reason = "Fixture provider unavailable"; return false; }
        public Task<OcrPage> ReadPageAsync(byte[] pdf, int page, int segmentation, CancellationToken ct) { Calls++; throw new InvalidOperationException(); }
    }
    [Fact]
    public async Task TextLayerUsesCoordinatesAndNeverCallsOcr()
    {
        var ocr = new NoOcr();
        var result = await new PdfExtractionService(ocr: ocr).ExtractAsync(TextPdf(threeYears: true), new(2024, 12, 31), default);
        Assert.Equal("Found", result.Status); Assert.Equal(1234000m, result.Value); Assert.Equal("GBP", result.Currency); Assert.Equal(0, ocr.Calls); Assert.Contains("Page 1", result.Evidence);
    }
    [Fact]
    public async Task ProfitDoesNotBecomeRevenue()
    {
        var result = await new PdfExtractionService(ocr: new NoOcr()).ExtractAsync(TextPdf(false), new(2024, 12, 31), default);
        Assert.NotEqual("Found", result.Status); Assert.Null(result.Value);
    }
    [Theory]
    [InlineData("EUR millions", "1,234", "EUR", 1234000000)]
    [InlineData("USD", "1,234", "USD", 1234)]
    [InlineData("GBP thousands", "(1,234)", "GBP", -1234000)]
    [InlineData("CHF thousands", "1,234", "CHF", 1234000)]
    public async Task NativePdfCurrencyScaleAndNegativeValues(string header, string amount, string currency, decimal expected)
    {
        var result = await new PdfExtractionService(ocr: new NoOcr()).ExtractAsync(TextPdf(currencyScale: header, amount: amount), new(2024, 12, 31), default);
        Assert.Equal("Found", result.Status); Assert.Equal(expected, result.Value); Assert.Equal(currency, result.Currency);
    }
    [Theory]
    [InlineData("$")]
    [InlineData("GBP thousands or millions")]
    public async Task AmbiguousScaleOrCurrencyNeedsReview(string header)
    {
        var result = await new PdfExtractionService(ocr: new NoOcr()).ExtractAsync(TextPdf(currencyScale: header), new(2024, 12, 31), default);
        Assert.Equal("Needs Review", result.Status); Assert.Null(result.Value);
    }
    [Fact]
    public async Task ScanWithoutRuntimeReportsOcrRequired()
    {
        var result = await new PdfExtractionService(ocr: new NoOcr()).ExtractAsync(ScanPdf(), new(2024, 12, 31), default);
        Assert.Equal("OCR Required", result.Status); Assert.Null(result.Value); Assert.Contains("Page 1", result.Evidence);
    }
    [OcrFact]
    public async Task ScannedPdfIsRenderedAndReadByRealOfflineTesseract()
    {
        var result = await new PdfExtractionService().ExtractAsync(ScanPdf(), new(2024, 12, 31), default);
        Assert.Equal("Found", result.Status); Assert.Equal(1234000m, result.Value); Assert.Equal("GBP", result.Currency);
        Assert.Contains("Tesseract", result.Method); Assert.Contains("two layout passes", result.Confidence); Assert.Contains("Page 1", result.Evidence);
    }
    [OcrFact]
    public async Task OcrSubprocessPreservesUtf8CurrencySymbols()
    {
        var read = await new OfflineOcrService(new(), new()).ReadPageAsync(ScanPdf("\u00a3 thousands"), 1, 6, default);
        Assert.Contains("\u00a3", read.Text); Assert.DoesNotContain("\u00c2\u00a3", read.Text);
    }
    [Theory]
    [InlineData("<html>Forbidden</html>")]
    [InlineData("%PDF-1.7 truncated")]
    public void InvalidOrIncompleteDownloadIsRejected(string text) => Assert.Throws<InvalidDataException>(() => PdfContentValidator.Validate(Encoding.ASCII.GetBytes(text)));
    [Fact]
    public void MalformedPdfWithMarkersIsStillRejectedByParser()
    {
        Assert.ThrowsAny<Exception>(() => new PdfExtractionService().Extract(Encoding.ASCII.GetBytes("%PDF-1.7\ninvalid object data\n%%EOF"), null, default));
    }
    [Fact]
    public void BaselineClusteringHandlesWordsAcrossRoundingBoundary()
    {
        var lines = PdfTableRevenueReader.Lines([new("Revenue", 10, 60, 100.4), new("1234", 200, 230, 102)]);
        Assert.Single(lines); Assert.Equal(2, lines[0].Count);
    }
    [Fact]
    public void TsvPreservesConfidenceAndFinancialCoordinates()
    {
        var result = OfflineOcrService.ParseTsv("level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n5\t1\t1\t1\t1\t1\t300\t100\t200\t40\t96.5\t1,234\n");
        Assert.Equal("1,234", Assert.Single(result.Words).Text); Assert.Equal(96.5, result.Confidence); Assert.Equal(72, result.Words[0].Left);
    }
    private sealed class FixtureOcr(bool disagree = false, bool lowConfidence = false) : IOfflineOcr
    {
        public List<int> Pages { get; } = [];
        public bool IsAvailable(out string reason) { reason = "Fixture OCR"; return true; }
        public Task<OcrPage> ReadPageAsync(byte[] pdf, int page, int segmentation, CancellationToken ct)
        {
            Pages.Add(page);
            using var native = UglyToad.PdfPig.PdfDocument.Open(TextPdf());
            var words = native.GetPage(1).GetWords().Select(w => new PdfWord(disagree && segmentation == 3 && w.Text == "1,234" ? "9,999" : w.Text, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom, lowConfidence ? 50 : 99)).ToArray();
            return Task.FromResult(new OcrPage(words, string.Join("\n", PdfTableRevenueReader.Lines(words).Select(l => string.Join(" ", l.Select(w => w.Text)))), lowConfidence ? 50 : 99));
        }
    }
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UncertainOcrNeverPublishesAnAmount(bool disagree, bool lowConfidence)
    {
        var result = await new PdfExtractionService(ocr: new FixtureOcr(disagree, lowConfidence)).ExtractAsync(ScanPdf(), new(2024, 12, 31), default);
        Assert.Equal("Needs Review", result.Status); Assert.Null(result.Value);
    }
    [Fact]
    public async Task MixedDocumentOcrRunsOnlyOnImagePage()
    {
        using var text = UglyToad.PdfPig.PdfDocument.Open(TextPdf(false));
        using var scan = UglyToad.PdfPig.PdfDocument.Open(ScanPdf());
        var mixed = new PdfDocumentBuilder(); mixed.AddPage(text, 1); mixed.AddPage(scan, 1);
        var ocr = new FixtureOcr();
        var result = await new PdfExtractionService(ocr: ocr).ExtractAsync(mixed.Build(), new(2024, 12, 31), default);
        Assert.Equal("Found", result.Status); Assert.Equal(1234000m, result.Value);
        Assert.All(ocr.Pages, p => Assert.Equal(2, p)); Assert.Equal(2, ocr.Pages.Count); Assert.Contains("Page 2", result.Evidence);
    }
    [Fact]
    public void StructuredComparativeYearCannotReplaceMissingCurrentYear()
    {
        var xml = Encoding.UTF8.GetBytes("""
            <xbrl xmlns="http://www.xbrl.org/2003/instance" xmlns:t="urn:fixture" xmlns:iso="http://www.xbrl.org/2003/iso4217">
            <context id="prior"><period><startDate>2023-01-01</startDate><endDate>2023-12-31</endDate></period></context>
            <unit id="u"><measure>iso:GBP</measure></unit><t:Revenue contextRef="prior" unitRef="u">1234</t:Revenue>
            </xbrl>
            """);
        var result = new XbrlExtractionService().Extract(xml, new(2024, 12, 31));
        Assert.Equal("Needs Review", result.Status); Assert.Null(result.Value);
    }
    [Fact]
    public async Task PageBudgetBlocksPublishingPartiallyReadScan()
    {
        using var scan = UglyToad.PdfPig.PdfDocument.Open(ScanPdf());
        var book = new PdfDocumentBuilder(); book.AddPage(scan, 1); book.AddPage(scan, 1);
        var result = await new PdfExtractionService(new() { OcrMaxPages = 1 }, new FixtureOcr()).ExtractAsync(book.Build(), new(2024, 12, 31), default);
        Assert.Equal("Needs Review", result.Status); Assert.Null(result.Value); Assert.Contains("pages remain", result.Evidence);
    }
    private static byte[] RepeatPages(byte[] bytes, int count)
    {
        using var source = UglyToad.PdfPig.PdfDocument.Open(bytes);
        var book = new PdfDocumentBuilder(); for (int page = 0; page < count; page++) book.AddPage(source, 1);
        return book.Build();
    }
    [Fact]
    public async Task AllPagesProcesses35PageScanAndReportsCompleteCoverage()
    {
        var ocr = new FixtureOcr(); var activities = new List<string>();
        var result = await new PdfExtractionService(new() { OcrMaxPages = 0 }, ocr).ExtractAsync(RepeatPages(ScanPdf(), 35), new(2024, 12, 31), default, activity: activities.Add);
        Assert.Equal("Found", result.Status); Assert.Equal(1234000m, result.Value);
        Assert.Equal(35, ocr.Pages.Distinct().Count()); Assert.Contains("35/35 pages inspected", result.Evidence);
        Assert.Contains(activities, a => a.Contains("OCR page 35/35"));
    }
    [Fact]
    public async Task PositiveBudgetCanExceed30WithoutHardClamp()
    {
        var ocr = new FixtureOcr();
        var result = await new PdfExtractionService(new() { OcrMaxPages = 32 }, ocr).ExtractAsync(RepeatPages(ScanPdf(), 35), new(2024, 12, 31), default);
        Assert.Equal(32, ocr.Pages.Distinct().Count()); Assert.Equal("Needs Review", result.Status); Assert.Null(result.Value);
        Assert.Contains("3 pages remain", result.Evidence);
    }
    [Fact]
    public async Task Readable35PageDocumentSkipsOcrEntirely()
    {
        var ocr = new NoOcr();
        var result = await new PdfExtractionService(ocr: ocr).ExtractAsync(RepeatPages(TextPdf(), 35), new(2024, 12, 31), default);
        Assert.Equal("Found", result.Status); Assert.Equal(0, ocr.Calls);
    }
    [Fact]
    public void ContentsFinancialReferencesArePrioritizedBeyondPage30()
    {
        var planner = new OcrPagePlanner(80); planner.Observe(3, "Revenue notes ........ 45\nIncome statement ........ 36");
        Assert.Contains(planner.Next(Enumerable.Range(1, 80)), new[] { 36, 45 });
        var settings = new AppSettings { OcrMaxPages = 400 }; Assert.False(settings.OcrAllPages);
        settings.OcrAllPages = true; Assert.Equal(0, settings.OcrMaxPages);
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(new AppSettings { OcrMaxPages = 400 }));
        Assert.Equal(400, roundtrip!.OcrMaxPages);
    }
    private sealed class CancellingOcr(CancellationTokenSource cancellation) : IOfflineOcr
    {
        public int Calls;
        public bool IsAvailable(out string reason) { reason = "Fixture"; return true; }
        public Task<OcrPage> ReadPageAsync(byte[] pdf, int page, int segmentation, CancellationToken ct)
        {
            Calls++; if (Calls == 31) cancellation.Cancel(); ct.ThrowIfCancellationRequested();
            return Task.FromResult(new OcrPage([], "No reliably readable financial content", 90));
        }
    }
    [Fact]
    public async Task LongDocumentCancellationStopsAfterPage30AndLogsCoverage()
    {
        using var cancellation = new CancellationTokenSource(); var ocr = new CancellingOcr(cancellation);
        var folder = Path.Combine(Path.GetTempPath(), "prasa-diagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new PdfExtractionService(new() { OcrMaxPages = 0 }, ocr, new(folder));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExtractAsync(RepeatPages(ScanPdf(), 35), new(2024, 12, 31), cancellation.Token));
            Assert.Equal(31, ocr.Calls);
            var log = File.ReadAllText(Path.Combine(folder, "extraction.jsonl"));
            Assert.Contains("ocr_summary", log); Assert.Contains("totalPages=35", log); Assert.Contains("ocrChecked=30", log);
        }
        finally { if (Directory.Exists(folder)) { foreach (var file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); } }
    }
    [OcrFact]
    public async Task RealOcrCacheReusesIdenticalDocumentPageAndLayout()
    {
        var unique = ScanPdf().Concat(Encoding.ASCII.GetBytes("\n% cache-fixture-" + Guid.NewGuid())).ToArray();
        var service = new OfflineOcrService(new(), new());
        var first = await service.ReadPageAsync(unique, 1, 6, default);
        var second = await new OfflineOcrService(new(), new()).ReadPageAsync(unique.ToArray(), 1, 6, default);
        Assert.False(first.FromCache); Assert.True(second.FromCache); Assert.NotEmpty(second.Words);
        Assert.Equal(first.Text, second.Text); Assert.Equal(first.Confidence, second.Confidence);
    }
    private sealed class FixtureApi(bool malformedPdf = false, bool missing = false, bool wrongOwner = false) : ICompaniesHouseApi
    {
        public int Downloads;
        public Task<CompanyProfile> GetProfileAsync(string number, CancellationToken ct) => Task.FromResult(new CompanyProfile(number, "Offline fixture company", "active", "ltd", "Fixture address"));
        public Task<List<AccountsFiling>> GetFilingsAsync(string number, CancellationToken ct) => Task.FromResult(new List<AccountsFiling> { new("fixture", new(2025, 1, 1), new(2024, 12, 31), "accounts-with-made-up-date", "https://document-api.company-information.service.gov.uk/document/fixture") });
        public Task<System.Text.Json.JsonDocument> GetJsonAsync(string url, CancellationToken ct)
        {
            if (missing) throw new ApiException(System.Net.HttpStatusCode.NotFound, "Fixture document missing");
            var owner = wrongOwner ? "99999999" : "00001234";
            var json = System.Text.Json.JsonSerializer.Serialize(new { company_number = owner, resources = new Dictionary<string, object> { ["application/xml"] = new { }, ["application/pdf"] = new { } }, links = new { document = "https://document-api.company-information.service.gov.uk/document/fixture/content" } });
            return Task.FromResult(System.Text.Json.JsonDocument.Parse(json));
        }
        public Task<byte[]> DownloadAsync(string url, string mime, CancellationToken ct)
        {
            Downloads++;
            return Task.FromResult(mime == "application/xml" || malformedPdf ? Encoding.ASCII.GetBytes("<html>invalid representation</html>") : TextPdf());
        }
    }
    [Theory]
    [InlineData(false, false, false, "Verified")]
    [InlineData(true, false, false, "Extraction Failed")]
    [InlineData(false, true, false, "Document Unavailable")]
    [InlineData(false, false, true, "Extraction Failed")]
    public async Task PipelineValidatesOwnerPayloadAndFallsBackFromUnreadableStructuredData(bool malformedPdf, bool missing, bool wrongOwner, string expected)
    {
        var api = new FixtureApi(malformedPdf, missing, wrongOwner);
        var result = await new RevenueExtractionService(api, new(api), new(), new(ocr: new NoOcr()), new()).ProcessAsync("00001234", default);
        Assert.Equal(expected, result.Status);
        if (expected == "Verified") { Assert.Equal(1234000m, result.Revenue); Assert.Equal("PDF table", result.Method); Assert.Equal(2, api.Downloads); }
        else Assert.Null(result.Revenue);
        if (wrongOwner) Assert.Equal(0, api.Downloads);
    }
}

