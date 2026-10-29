using System.IO;
using System.Net.Http;
using System.Text.Json;
using PrasaResarch.Services;

namespace PrasaResarch.Tests;
public sealed class LocalCorpusFactAttribute : FactAttribute
{
    public LocalCorpusFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPANIES_HOUSE_LOCAL_CORPUS"))) Skip = "Set COMPANIES_HOUSE_LOCAL_CORPUS to a saved real-document manifest."; }
}
public record LocalAccountFixture(string Path, DateTime PeriodEnd, decimal? ExpectedCandidate = null);
public sealed class RealCorpusFactAttribute : FactAttribute
{
    public RealCorpusFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPANIES_HOUSE_TEST_API_KEY")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPANIES_HOUSE_REAL_ACCOUNTS")))
            Skip = "Requires separately configured live key and COMPANIES_HOUSE_REAL_ACCOUNTS comma-separated corpus numbers.";
    }
}
public class RealAccountsCorpusTests
{
    [LocalCorpusFact]
    [Trait("Category", "RealDocuments")]
    public async Task AnalyzePreviouslyDownloadedRealDocuments()
    {
        var manifest = Environment.GetEnvironmentVariable("COMPANIES_HOUSE_LOCAL_CORPUS")!;
        var inputs = JsonSerializer.Deserialize<List<LocalAccountFixture>>(await File.ReadAllTextAsync(manifest))!;
        var reports = new System.Collections.Concurrent.ConcurrentBag<object>();
        await Parallel.ForEachAsync(inputs, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (input, _) =>
        {
            var bytes = await File.ReadAllBytesAsync(input.Path); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var extraction = await new PdfExtractionService().ExtractAsync(bytes, input.PeriodEnd, timeout.Token);
            var result = new RevenueValidationService().Validate(extraction);
            if (input.ExpectedCandidate.HasValue) Assert.Contains(result.Candidates, c => c.Year == input.PeriodEnd.Year && c.Amount == input.ExpectedCandidate);
            if (result.Status == "Verified") Assert.Contains(result.Candidates, c => c.Exact && c.Statement && c.Entity != "Unknown" && c.Amount == result.Value);
            reports.Add(new { input.Path, input.PeriodEnd, result.Status, result.Value, result.Currency, result.ReportingEntity, result.Confidence, result.Candidates });
        });
        await File.WriteAllTextAsync(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(manifest)!, "local-analysis-report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }
    [RealCorpusFact]
    [Trait("Category", "Live")]
    public async Task AnalyzeRealAccountsAcrossConfiguredCompanyCorpus()
    {
        var settings = new SettingsService(); typeof(SettingsService).GetProperty(nameof(SettingsService.ApiKey))!.SetValue(settings, Environment.GetEnvironmentVariable("COMPANIES_HOUSE_TEST_API_KEY"));
        settings.Settings.TimeoutSeconds = 15;
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var api = new CompaniesHouseApiService(client, settings, new()); var documents = new CompaniesHouseDocumentService(api);
        var reports = new System.Collections.Concurrent.ConcurrentBag<object>();
        var numbers = Environment.GetEnvironmentVariable("COMPANIES_HOUSE_REAL_ACCOUNTS")!.Split(',').Select(PrasaResarch.Models.CompanyNumber.Normalize).Distinct().ToList();
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/real-accounts")); Directory.CreateDirectory(output);
        await Parallel.ForEachAsync(numbers, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (number, _) =>
        {
            string name = "", accountsType = "", stage = "profile";
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                using var profile = await api.GetJsonAsync(CompaniesHouseApiService.BaseUrl + "/company/" + number, limit.Token);
                name = profile.RootElement.GetProperty("company_name").GetString()!;
                accountsType = profile.RootElement.GetProperty("accounts").GetProperty("last_accounts").GetProperty("type").GetString() ?? "";
                stage = "filings"; var filings = await api.GetFilingsAsync(number, limit.Token);
                var requested = Environment.GetEnvironmentVariable("COMPANIES_HOUSE_REAL_PERIOD");
                var filing = string.IsNullOrWhiteSpace(requested) ? AccountsFilingService.Latest(filings) : filings.FirstOrDefault(f => f.PeriodEnd?.ToString("yyyy-MM-dd") == requested); Assert.NotNull(filing);
                stage = "metadata"; var metadata = await documents.MetadataAsync(filing.MetadataUrl!, limit.Token, number);
                Assert.Contains("application/pdf", metadata.Types);
                stage = "download";
                var path = Path.Combine(output, $"{number}-{filing.PeriodEnd:yyyyMMdd}.pdf");
                var bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path, limit.Token) : await documents.DownloadAsync(metadata.ContentUrl, "application/pdf", limit.Token);
                PdfContentValidator.Validate(bytes); await File.WriteAllBytesAsync(path, bytes, limit.Token);
                stage = "financial analysis";
                var extraction = await Task.Run(() => new PdfExtractionService(settings.Settings).ExtractAsync(bytes, filing.PeriodEnd, limit.Token), limit.Token);
                var result = new RevenueValidationService().Validate(extraction);
                if (result.Status == "Verified") Assert.Contains(result.Candidates, c => c.Amount == result.Value && c.Entity == result.ReportingEntity && c.Year == filing.PeriodEnd?.Year && c.Exact && c.Statement);
                reports.Add(new { number, name, accountsType, period = filing.PeriodEnd, stage, result.Status, result.Value, result.Currency, result.ReportingEntity, candidates = result.Candidates.Count, result.Evidence });
            }
            catch (Exception ex) when (ex is IOException or ApiException or OperationCanceledException or TimeoutException)
            { reports.Add(new { number, name, accountsType, stage, status = "Live validation incomplete", error = ex.GetType().Name }); }
        });
        var json = JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), json);
        await File.WriteAllTextAsync(Path.Combine(output, $"report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"), json);
        Assert.Equal(numbers.Count, reports.Count);
    }
}
