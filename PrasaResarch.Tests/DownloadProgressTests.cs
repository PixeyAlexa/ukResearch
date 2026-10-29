using System.Net;
using System.Net.Http;
using PrasaResarch.Services;

namespace PrasaResarch.Tests;
public class DownloadProgressTests
{
    private sealed class ContentHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
    [Fact]
    public async Task StreamingDownloadReportsActualByteCountsAndCompletion()
    {
        var settings = new SettingsService();
        typeof(SettingsService).GetProperty(nameof(SettingsService.ApiKey))!.SetValue(settings, "offline-progress-fixture");
        var bytes = new byte[900000]; using var http = new HttpClient(new ContentHandler(bytes));
        var api = new CompaniesHouseApiService(http, settings, new()); var updates = new List<DownloadProgress>();
        var result = await api.DownloadWithProgressAsync(CompaniesHouseApiService.BaseUrl + "/fixture", "application/octet-stream", new CallbackProgress<DownloadProgress>(updates.Add), default);
        Assert.Equal(bytes.Length, result.Length);
        Assert.Contains(updates, p => p.Received > 0 && p.Received < bytes.Length);
        Assert.Equal(bytes.Length, updates[^1].Received); Assert.Equal(bytes.Length, updates[^1].Total);
        Assert.Equal("Downloaded", updates[^1].Activity);
    }
}
