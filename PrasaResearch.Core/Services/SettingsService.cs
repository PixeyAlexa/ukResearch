using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrasaResarch.Models;

namespace PrasaResarch.Services;
public class SettingsService
{
    public string Root { get; } = PlatformPaths.Data;
    private readonly ICredentialStore credentials;
    public AppSettings Settings { get; private set; }
    public string ApiKey { get; private set; } = "";
    public SettingsService()
    {
        Directory.CreateDirectory(Root);
        if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRASARESEARCH_DATA_DIR"))) LegacyMigration.ImportOnce(Root, PlatformPaths.LegacyWindowsData);
        credentials = OperatingSystem.IsWindows() ? new WindowsCredentialStore(Root) : new MacKeychainCredentialStore();
        Settings = File.Exists(Path.Combine(Root, "settings.json")) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(Root, "settings.json"))) ?? new() : new();
        ApiKey = credentials.Read();
        Settings.TesseractPath = File.Exists(Settings.TesseractPath) ? Settings.TesseractPath : PlatformPaths.DefaultTesseract;
    }
    public void Save(string key)
    {
        if (Settings.OcrMaxPages < 0) throw new ArgumentException("Maximum OCR pages must be zero (all pages) or a positive number.");
        if (Settings.TimeoutSeconds is < 5 or > 600 || Settings.Concurrency is < 1 or > 16 || Settings.RequestsPerFiveMinutes is < 1 or > 10000)
            throw new ArgumentException("Timeout must be 5–600 seconds, concurrency 1–16, and rate 1–10000 requests per five minutes.");
        Directory.CreateDirectory(Settings.DownloadFolder); Directory.CreateDirectory(Settings.TemporaryFolder);
        File.WriteAllText(Path.Combine(Root, "settings.json"), JsonSerializer.Serialize(Settings));
        credentials.Write(key.Trim());
        ApiKey = key.Trim();
    }
}

