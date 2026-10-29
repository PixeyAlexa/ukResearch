using System.IO;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using System.Globalization;
using PrasaResarch.Models;
using PrasaResarch.Services;

namespace PrasaResarch.Tests;
public class LatestRevenueTests
{
    private static RevenueCandidate Fact(int year, decimal value, string entity = "Company") => new() { Year = year, Amount = value, Currency = "GBP", Entity = entity, Exact = true, Statement = true, Aligned = true, Confidence = 100 };
    private static Extraction Validate(params RevenueCandidate[] facts) => new RevenueValidationService().Validate(new Extraction(null, "", new(2031, 12, 31), "fixture", "Needs Review", "Candidates", "") { Candidates = facts.ToList() });
    [Fact]
    public void CurrentYearWinsRegardlessOfCandidateOrderOrComparativeAmount()
    {
        foreach (var facts in new[] { new[] { Fact(2030, 999999), Fact(2031, 12345) }, new[] { Fact(2031, 12345), Fact(2030, 999999) } })
        { var result = Validate(facts); Assert.Equal("Verified", result.Status); Assert.Equal(12345m, result.Value); }
    }
    [Fact]
    public void PriorYearAndRoundedOnlyNeverBecomeLatestRevenue()
    {
        Assert.Null(Validate(Fact(2030, 999999)).Value);
        Assert.Null(Validate(Fact(2031, 12000) with { Exact = false }).Value);
    }
    [Fact]
    public void MismatchedNewerStatementYearCannotMakeFilingYearAComparativeSelection()
    {
        var result = Validate(Fact(2032, 40000), Fact(2031, 30000));
        Assert.Equal("Needs Review", result.Status); Assert.Null(result.Value);
    }
    [Fact]
    public void MissingLatestGroupTurnoverCannotUsePriorGroupOrCurrentCompany()
    {
        var result = Validate(Fact(2030, 70000, "Group"), Fact(2031, 20000));
        Assert.Equal("Needs Review", result.Status); Assert.Null(result.Value);
        var primary = Validate(Fact(2030, 70000, "Group"), Fact(2031, 80000, "Group"), Fact(2031, 20000));
        Assert.Equal(80000m, primary.Value); Assert.Equal("Group", primary.ReportingEntity);
    }
    [Fact]
    public void RecentUndatedAccountsPreventSilentSelectionOfOlderDatedAccounts()
    {
        var latest = AccountsFilingService.Latest([new("old", new(2031, 1, 1), new(2030, 12, 31), "accounts", null), new("new", new(2032, 1, 1), null, "accounts", null)]);
        Assert.Equal("new", latest!.Id);
    }
    private sealed class Api(bool includeCurrent) : ICompaniesHouseApi
    {
        public List<string> Downloads { get; } = [];
        public Task<CompanyProfile> GetProfileAsync(string n, CancellationToken ct) => Task.FromResult(new CompanyProfile(n, "Fixture", "active", "ltd", ""));
        public Task<List<AccountsFiling>> GetFilingsAsync(string n, CancellationToken ct) => Task.FromResult(new List<AccountsFiling> { new("older", new(2031, 1, 1), new(2030, 12, 31), "accounts", "https://document-api.company-information.service.gov.uk/document/older"), new("latest", new(2032, 1, 1), new(2031, 12, 31), "accounts", "https://document-api.company-information.service.gov.uk/document/latest") });
        public Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct) { Assert.EndsWith("/latest", url); return Task.FromResult(JsonDocument.Parse("""{"resources":{"application/xml":{}},"links":{"document":"https://document-api.company-information.service.gov.uk/document/latest/content"}}""")); }
        public Task<byte[]> DownloadAsync(string url, string mime, CancellationToken ct)
        {
            Downloads.Add(url);
            var context = "<entity><identifier scheme='fixture'>entity</identifier><segment><d:explicitMember dimension='t:ConsolidationAxis'>t:CompanyMember</d:explicitMember></segment></entity>";
            var xml = $"<xbrl xmlns='http://www.xbrl.org/2003/instance' xmlns:t='urn:fixture' xmlns:d='http://xbrl.org/2006/xbrldi' xmlns:iso='http://www.xbrl.org/2003/iso4217'><context id='current'>{context}<period><startDate>2031-01-01</startDate><endDate>2031-12-31</endDate></period></context><context id='prior'>{context}<period><startDate>2030-01-01</startDate><endDate>2030-12-31</endDate></period></context><unit id='u'><measure>iso:GBP</measure></unit><t:Revenue contextRef='prior' unitRef='u'>999999</t:Revenue>{(includeCurrent ? "<t:Revenue contextRef='current' unitRef='u'>12345</t:Revenue>" : "")}</xbrl>";
            return Task.FromResult(Encoding.UTF8.GetBytes(xml));
        }
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task FullPipelineDownloadsOnlyLatestAccountsAndNeverUsesPriorFact(bool includeCurrent)
    {
        var api = new Api(includeCurrent); var result = await new RevenueExtractionService(api, new(api), new(), new(), new()).ProcessAsync("00001234", default);
        Assert.Equal(2031, result.FinancialYear); Assert.Single(api.Downloads); Assert.EndsWith("/latest/content", api.Downloads[0]);
        if (includeCurrent) { Assert.Equal("Verified", result.Status); Assert.Equal(12345m, result.Revenue); }
        else { Assert.Equal("Needs Review", result.Status); Assert.Null(result.Revenue); }
    }
    [Fact]
    public void SevenColumnExportsKeepOneLatestRowEvenWhenOlderRowHasRevenue()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var rows = new[] { new CompanyResult { CompanyNumber = "00001234", PeriodEnd = new(2030, 12, 31), Revenue = 999m, Status = "Verified" }, new CompanyResult { CompanyNumber = "00001234", PeriodEnd = new(2031, 12, 31), Status = "Not Disclosed" } };
        try
        {
            var export = new ExportService(); export.Export(path + ".xlsx", rows);
            using var book = new XLWorkbook(path + ".xlsx"); var sheet = book.Worksheet(1);
            Assert.Equal(7, sheet.LastColumnUsed()!.ColumnNumber()); Assert.Equal(2, sheet.LastRowUsed()!.RowNumber()); Assert.True(sheet.Cell(2, 3).IsEmpty()); Assert.Equal(2031, sheet.Cell(2, 5).GetValue<int>());
            export.Export(path + ".csv", rows); using var reader = new StreamReader(path + ".csv"); using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            csv.Read(); csv.ReadHeader(); Assert.Equal(7, csv.HeaderRecord!.Length); Assert.True(csv.Read()); Assert.Equal("", csv.GetField(2)); Assert.Equal("2031", csv.GetField(4)); Assert.False(csv.Read());
        }
        finally { File.Delete(path + ".xlsx"); File.Delete(path + ".csv"); }
    }
}
