using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PrasaResarch.Models;

namespace PrasaResarch.Services;
public record DownloadProgress(long Received, long? Total, string Activity);
public sealed class CallbackProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
public interface ICompaniesHouseApi
{
    Task<CompanyProfile> GetProfileAsync(string number, CancellationToken ct);
    Task<List<AccountsFiling>> GetFilingsAsync(string number, CancellationToken ct);
    Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct);
    Task<byte[]> DownloadAsync(string url, string mime, CancellationToken ct);
    async Task<byte[]> DownloadWithProgressAsync(string url, string mime, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new(0, null, "Connecting"));
        var bytes = await DownloadAsync(url, mime, ct);
        progress?.Report(new(bytes.Length, bytes.Length, "Downloaded")); return bytes;
    }
}
public class ApiException(HttpStatusCode status, string message) : Exception(message)
{ public HttpStatusCode StatusCode { get; } = status; }
public class RequestThrottle
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Queue<DateTimeOffset> timestamps = new();
    public async Task WaitAsync(int limit, CancellationToken ct)
    {
        while (true)
        {
            TimeSpan delay;
            await gate.WaitAsync(ct);
            try
            {
                var now = DateTimeOffset.UtcNow;
                while (timestamps.TryPeek(out var t) && now - t >= TimeSpan.FromMinutes(5)) timestamps.Dequeue();
                if (timestamps.Count < Math.Max(1, limit)) { timestamps.Enqueue(now); return; }
                delay = timestamps.Peek().AddMinutes(5) - now;
            }
            finally { gate.Release(); }
            await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1), ct);
        }
    }
}
public class CompaniesHouseApiService(HttpClient http, SettingsService settings, RequestThrottle throttle) : ICompaniesHouseApi
{
    public const string BaseUrl = "https://api.company-information.service.gov.uk";
    public async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct) => JsonDocument.Parse(await DownloadAsync(url, "application/json", ct));
    public Task<byte[]> DownloadAsync(string url, string mime, CancellationToken ct) => DownloadWithProgressAsync(url, mime, null, ct);
    public async Task<byte[]> DownloadWithProgressAsync(string url, string mime, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || (uri.Host != "api.company-information.service.gov.uk" && uri.Host != "document-api.company-information.service.gov.uk"))
            throw new InvalidOperationException("The API returned an untrusted document metadata address.");
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw new InvalidOperationException("Enter and save your Companies House API key in Settings first.");
        for (int attempt = 0; ; attempt++)
        {
            progress?.Report(new(0, null, attempt == 0 ? "Waiting for API capacity" : $"Retry {attempt}: waiting for API capacity"));
            await throttle.WaitAsync(settings.Settings.RequestsPerFiveMinutes, ct);
            progress?.Report(new(0, null, "Waiting for response"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.Settings.TimeoutSeconds));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.ApiKey + ":")));
                request.Headers.Accept.Add(new(mime));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 3)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                    await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), ct); continue;
                }
                if (!response.IsSuccessStatusCode) throw new ApiException(response.StatusCode, response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "API authentication failed. Check your key in Settings.",
                    HttpStatusCode.Forbidden => "The API key does not have permission for this resource.",
                    HttpStatusCode.NotFound => "Company or filing document was not found.",
                    HttpStatusCode.BadRequest => "Companies House rejected the request.",
                    HttpStatusCode.TooManyRequests => "Companies House rate limit reached. Retry later or reduce the rate setting.",
                    _ => $"Companies House returned HTTP {(int)response.StatusCode}."
                });
                const int max = 80 * 1024 * 1024;
                if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Document exceeds the 80 MB processing limit. Review the original filing.");
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var output = new MemoryStream(); var buffer = new byte[81920]; int count;
                var total = response.Content.Headers.ContentLength;
                long lastBytes = 0; var lastUpdate = DateTime.UtcNow;
                progress?.Report(new(0, total, "Downloading"));
                while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (output.Length + count > max) throw new InvalidDataException("Document exceeds the 80 MB processing limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    if (output.Length - lastBytes >= 262144 || DateTime.UtcNow - lastUpdate > TimeSpan.FromMilliseconds(250))
                    { progress?.Report(new(output.Length, total, "Downloading")); lastBytes = output.Length; lastUpdate = DateTime.UtcNow; }
                }
                progress?.Report(new(output.Length, total ?? output.Length, "Downloaded"));
                return output.ToArray();
            }
            catch (HttpRequestException) when (attempt < 3) { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < 3) { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct); }
            catch (HttpRequestException) { throw new IOException("Network request failed. Check your connection and try again."); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("Companies House request timed out."); }
        }
    }
    public async Task<CompanyProfile> GetProfileAsync(string number, CancellationToken ct)
    {
        using var json = await GetJsonAsync($"{BaseUrl}/company/{CompanyNumber.Normalize(number)}", ct);
        var r = json.RootElement;
        return new(r.GetProperty("company_number").GetString()!, r.GetProperty("company_name").GetString()!, r.GetProperty("company_status").GetString()!, r.GetProperty("type").GetString()!,
            r.TryGetProperty("registered_office_address", out var address) ? string.Join(", ", address.EnumerateObject().Select(p => p.Value.ToString())) : "");
    }
    public async Task<List<AccountsFiling>> GetFilingsAsync(string number, CancellationToken ct)
    {
        var result = new List<AccountsFiling>(); int start = 0;
        while (true)
        {
            using var json = await GetJsonAsync($"{BaseUrl}/company/{number}/filing-history?category=accounts&items_per_page=100&start_index={start}", ct);
            var r = json.RootElement; var items = r.GetProperty("items");
            foreach (var item in items.EnumerateArray())
            {
                var description = item.GetProperty("description").GetString() ?? "";
                if (!description.StartsWith("accounts-with-accounts-type", StringComparison.Ordinal) && description != "accounts-with-made-up-date") continue;
                DateTime? end = null;
                if (item.TryGetProperty("description_values", out var values) && values.TryGetProperty("made_up_date", out var date) && DateTime.TryParse(date.GetString(), out var d)) end = d;
                string? metadata = item.TryGetProperty("links", out var links) && links.TryGetProperty("document_metadata", out var link) ? link.GetString() : null;
                result.Add(new(item.GetProperty("transaction_id").GetString()!, DateTime.Parse(item.GetProperty("date").GetString()!), end, description, metadata));
            }
            start += items.GetArrayLength();
            if (items.GetArrayLength() == 0 || start >= r.GetProperty("total_count").GetInt32()) break;
        }
        return result;
    }
}
public static class AccountsFilingService
{
    public static AccountsFiling? Latest(IEnumerable<AccountsFiling> filings)
    {
        var all = filings.ToList();
        var dated = all.Where(f => f.PeriodEnd.HasValue).OrderByDescending(f => f.PeriodEnd).ThenByDescending(f => f.FilingDate).FirstOrDefault();
        var unknown = all.Where(f => !f.PeriodEnd.HasValue).OrderByDescending(f => f.FilingDate).FirstOrDefault();
        return unknown != null && (dated == null || unknown.FilingDate >= dated.FilingDate) ? unknown : dated;
    }
}
