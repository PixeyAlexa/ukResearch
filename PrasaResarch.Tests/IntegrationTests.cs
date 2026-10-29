using System.Net;
using System.Net.Http;
using System.Text;
using PrasaResarch.Services;

namespace PrasaResarch.Tests;
public class IntegrationTests
{
    private sealed class ContractHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath; Requests.Add(path);
            string content = path switch
            {
                "/company/00001234" => """{"company_number":"00001234","company_name":"Fixture company","company_status":"active","type":"ltd"}""",
                "/company/00001234/filing-history" => """{"total_count":1,"items":[{"transaction_id":"fixture","date":"2025-04-01","description":"accounts-with-made-up-date","description_values":{"made_up_date":"2024-12-31"},"links":{"document_metadata":"https://document-api.company-information.service.gov.uk/document/fixture"}}]}""",
                "/document/fixture" => """{"resources":{"application/xml":{"content_length":500}},"links":{"document":"https://document-api.company-information.service.gov.uk/document/fixture/content"}}""",
                "/document/fixture/content" => """
                  <xbrl xmlns="http://www.xbrl.org/2003/instance" xmlns:t="urn:fixture" xmlns:iso="http://www.xbrl.org/2003/iso4217">
                    <context id="c"><period><startDate>2024-01-01</startDate><endDate>2024-12-31</endDate></period></context>
                    <unit id="u"><measure>iso:GBP</measure></unit><t:Turnover contextRef="c" unitRef="u">1234.56</t:Turnover>
                  </xbrl>
                  """,
                _ => throw new InvalidOperationException("Unexpected contract request")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8) });
        }
    }
    [Fact]
    public async Task CompleteProcessingFollowsAdvertisedMetadataAndContentLinks()
    {
        var settings = new SettingsService(); typeof(SettingsService).GetProperty(nameof(SettingsService.ApiKey))!.SetValue(settings, "offline-test");
        var handler = new ContractHandler(); using var client = new HttpClient(handler);
        var api = new CompaniesHouseApiService(client, settings, new());
        var processor = new RevenueExtractionService(api, new(api), new(), new(), settings);
        var result = await processor.ProcessAsync("00001234", default);
        Assert.Equal("Needs Review", result.Status); Assert.Null(result.Revenue); Assert.Single(result.Candidates);
        Assert.Equal(2024, result.FinancialYear); Assert.Contains("/filing-history/fixture/document", result.SourceUrl);
        Assert.Equal(new[] { "/company/00001234", "/company/00001234/filing-history", "/document/fixture", "/document/fixture/content" }, handler.Requests);
    }
    private sealed class RetryHandler(HttpStatusCode first) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(++Calls == 1 ? first : HttpStatusCode.OK) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10));
            return Task.FromResult(response);
        }
    }
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task TransientFailuresRespectRetryAfter(HttpStatusCode code)
    {
        var settings = new SettingsService(); typeof(SettingsService).GetProperty(nameof(SettingsService.ApiKey))!.SetValue(settings, "offline-test");
        var handler = new RetryHandler(code); using var client = new HttpClient(handler);
        var api = new CompaniesHouseApiService(client, settings, new());
        using var json = await api.GetJsonAsync(CompaniesHouseApiService.BaseUrl + "/company/00001234", default);
        Assert.Equal(2, handler.Calls);
    }
}
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMPANIES_HOUSE_TEST_API_KEY"))) Skip = "Live integration requires COMPANIES_HOUSE_TEST_API_KEY; offline tests do not use the network."; }
}
public class LiveIntegrationTests
{
    [LiveFact]
    [Trait("Category", "Live")]
    public async Task RevenueSearchPublishesDetailsAndTerminatesForLargeAccounts()
    {
        var settings = new SettingsService();
        typeof(SettingsService).GetProperty(nameof(SettingsService.ApiKey))!.SetValue(settings, Environment.GetEnvironmentVariable("COMPANIES_HOUSE_TEST_API_KEY")!);
        settings.Settings.SaveSourcePdfs = false;
        using var client = new HttpClient(); var api = new CompaniesHouseApiService(client, settings, new());
        var processor = new RevenueExtractionService(api, new(api), new(), new(), settings, TimeSpan.FromSeconds(20));
        var updates = new List<PrasaResarch.Models.CompanyResult>(); processor.ResultUpdated += r => updates.Add(r);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        var result = await processor.ProcessAsync("00445790", ct.Token);
        Assert.NotEqual("Pending", result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.CompanyName));
        Assert.Contains(updates, r => r.Stage == "Finding latest accounts filing" && r.CompanyName.Length > 0);
        Assert.Contains(updates, r => r.Stage == "Downloading accounts PDF");
        Console.WriteLine($"Live result: {result.CompanyName}; {result.Status}; {result.Method}; {result.Confidence}; {result.Evidence}");
    }
    [LiveFact]
    [Trait("Category", "Live")]
    public async Task AuthenticatedProfileFilingAndDocumentMetadata()
    {
        var settings = new SettingsService(); typeof(SettingsService).GetProperty(nameof(SettingsService.ApiKey))!.SetValue(settings, Environment.GetEnvironmentVariable("COMPANIES_HOUSE_TEST_API_KEY")!);
        using var client = new HttpClient(); var api = new CompaniesHouseApiService(client, settings, new());
        using var ct = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var profile = await api.GetProfileAsync("00445790", ct.Token); Assert.False(string.IsNullOrWhiteSpace(profile.Name));
        var filings = await api.GetFilingsAsync(profile.Number, ct.Token); var latest = AccountsFilingService.Latest(filings); Assert.NotNull(latest);
        if (latest.MetadataUrl != null) { var metadata = await new CompaniesHouseDocumentService(api).MetadataAsync(latest.MetadataUrl, ct.Token); Assert.NotEmpty(metadata.Types); }
    }
}
