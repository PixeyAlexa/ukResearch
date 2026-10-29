using System.IO;
using System.Security.Cryptography;
using PrasaResarch.Models;
using PrasaResarch.Services;
using PrasaResarch.Data;

namespace PrasaResarch.Tests;
public class RevenueValidationTests
{
    private static IReadOnlyList<PdfWord> Table(string scope = "Company", bool fragmented = false, string current = "1234567")
    {
        var words = new List<PdfWord> { new(scope + " audited income statement", 20, 220, 750), new("GBP", 20, 50, 725), new("2027", 280, 300, 700), new("2026", 480, 500, 700), new("Turnover", 20, 80, 680), new("7", 120, 125, 680), new("765432", 450, 500, 680) };
        if (fragmented) words.AddRange([new("1", 220, 225, 680), new("234", 230, 260, 680), new("567", 270, 300, 680)]);
        else words.Add(new(current, 230, 300, 680));
        return words;
    }
    private static Extraction Validate(params (int Page, IReadOnlyList<PdfWord> Words)[] pages) => new RevenueValidationService().Validate(new PdfTableRevenueReader().Extract(pages, new(2027, 9, 30))!);
    [Fact]
    public void SplitDigitGroupsAndNoteReferenceMatchYearColumn()
    {
        var result = Validate((2, Table(fragmented: true)));
        Assert.Equal("Verified", result.Status); Assert.Equal(1234567m, result.Value); Assert.Equal("Company", result.ReportingEntity);
        Assert.Contains(result.Candidates, c => c.Year == 2026 && c.Amount == 765432m);
    }
    [Fact]
    public void ConsolidatedScopeIsExplicitAndSeparateFromCompany()
    {
        Assert.Equal("Group", Validate((1, Table("Consolidated"))).ReportingEntity);
        var conflict = Validate((1, Table("Group")), (2, Table("Company", current: "9876543")));
        Assert.Equal("Verified", conflict.Status); Assert.Equal(1234567m, conflict.Value); Assert.Equal("Group", conflict.ReportingEntity); Assert.Equal(4, conflict.Candidates.Count);
    }
    [Fact]
    public void RepeatedYearColumnsRetainGroupAndCompanyCandidates()
    {
        var words = new List<PdfWord> { new("Income statement", 20, 160, 770), new("Group", 275, 325, 745), new("Company", 525, 575, 745), new("GBP", 20, 50, 725),
            new("2027", 230, 250, 700), new("2026", 330, 350, 700), new("2027", 480, 500, 700), new("2026", 580, 600, 700), new("Turnover", 20, 80, 680),
            new("1234567", 200, 250, 680), new("765432", 300, 350, 680), new("9876543", 450, 500, 680), new("8765432", 550, 600, 680) };
        var result = Validate((1, words));
        Assert.Equal("Verified", result.Status); Assert.Equal("Group", result.ReportingEntity); Assert.Equal(1234567m, result.Value); Assert.Equal(4, result.Candidates.Count);
        Assert.Contains(result.Candidates, c => c.Entity == "Group" && c.Year == 2027 && c.Amount == 1234567m);
        Assert.Contains(result.Candidates, c => c.Entity == "Company" && c.Year == 2027 && c.Amount == 9876543m);
    }
    [Fact]
    public void RoundedNarrativeCorroboratesWithoutReplacingExactAmount()
    {
        var support = new[] { new PdfWord("Turnover", 20, 80, 680), new PdfWord("GBP", 90, 115, 680), new PdfWord("1.2M", 130, 180, 680) };
        var result = Validate((2, Table()), (7, support));
        Assert.Equal("Verified", result.Status); Assert.Equal(1234567m, result.Value); Assert.Contains("1 supporting pages", result.Confidence);
    }
    [Fact]
    public void UnknownEntityAndDifferentExactAmountsCannotBeVerified()
    {
        var unknown = Validate((1, Table(""))); Assert.Equal("Needs Review", unknown.Status);
        var conflict = Validate((1, Table()), (2, Table(current: "1111111")));
        Assert.Equal("Needs Review", conflict.Status); Assert.Null(conflict.Value);
    }
    [Fact]
    public void OcrCharacterCorrectionRemainsReviewable()
    {
        var result = Validate((1, Table(current: "123O567")));
        Assert.Equal("Needs Review", result.Status); Assert.Contains(result.Candidates, c => c.Issue.Contains("substitution"));
    }
    [Fact]
    public void ReportingYearMismatchDoesNotUseComparative()
    {
        var result = new RevenueValidationService().Validate(new PdfTableRevenueReader().Extract([(1, Table())], new(2028, 9, 30))!);
        Assert.Equal("Needs Review", result.Status); Assert.Null(result.Value);
    }
    [Fact]
    public void FirstPeriodSingleYearStatementCanBeVerified()
    {
        var words = Table().Where(w => w.Text is not "2026" and not "765432").ToArray();
        Assert.Equal("Verified", Validate((1, words)).Status);
    }
    [Theory]
    [InlineData("CompanyMember", "Company")]
    [InlineData("UnconsolidatedMember", "Company")]
    [InlineData("ConsolidatedGroupMember", "Group")]
    public void StructuredConsolidationContextIsVerifiedWithoutCompanySpecificRules(string member, string expectedScope)
    {
        var xml = $"<xbrl xmlns='http://www.xbrl.org/2003/instance' xmlns:t='urn:fixture' xmlns:iso='http://www.xbrl.org/2003/iso4217' xmlns:d='http://xbrl.org/2006/xbrldi'><context id='c'><entity><identifier scheme='urn:fixture'>entity</identifier><segment><d:explicitMember dimension='t:ConsolidationAxis'>t:{member}</d:explicitMember></segment></entity><period><startDate>2027-01-01</startDate><endDate>2027-09-30</endDate></period></context><unit id='u'><measure>iso:GBP</measure></unit><t:Revenue contextRef='c' unitRef='u'>1234</t:Revenue></xbrl>";
        var result = new RevenueValidationService().Validate(new XbrlExtractionService().Extract(System.Text.Encoding.UTF8.GetBytes(xml), new(2027, 9, 30)));
        Assert.Equal("Verified", result.Status); Assert.Equal(expectedScope, result.ReportingEntity); Assert.Equal(1234m, result.Value);
    }
    [Fact]
    public void ManualDecisionPersistsAuditAndChecksSourceHashAndPeriod()
    {
        var folder = Path.Combine(Path.GetTempPath(), "prasa-review-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            var bytes = ExtractionPipelineTests.TextPdf(); var path = Path.Combine(folder, "source.pdf"); File.WriteAllBytes(path, bytes);
            var result = new CompanyResult { CompanyNumber = "00001234", PeriodEnd = new(2027, 9, 30), SourceUrl = "https://example.test/filing", LocalDocumentPath = path, DocumentHash = Convert.ToHexString(SHA256.HashData(bytes)), Status = "Needs Review" };
            var store = new JobPersistenceService(Path.Combine(folder, "jobs.db")); store.Save(result);
            var service = new ManualRevenueReviewService(store);
            Assert.Throws<ArgumentException>(() => service.Confirm(result, 123m, "GBP", new(2027, 9, 30), "Company", 1, "Checked financial source", false));
            var verified = service.Confirm(result, 123m, "GBP", new(2027, 9, 30), "Company", 1, "Checked financial source", true);
            Assert.Equal("Verified", verified.Status); Assert.Equal(123m, Assert.Single(store.Load()).Revenue);
            Assert.Equal(result.DocumentHash, Assert.Single(new JobPersistenceService(Path.Combine(folder, "jobs.db")).Reviews(result.CompanyNumber)).DocumentHash);
            File.AppendAllText(path, "tampered");
            Assert.Throws<InvalidDataException>(() => service.Confirm(result, 456m, "GBP", new(2027, 9, 30), "Company", 1, "Checked financial source", true));
            store.Clear(); Assert.Single(store.Reviews(result.CompanyNumber));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); foreach (var file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
    }
}
