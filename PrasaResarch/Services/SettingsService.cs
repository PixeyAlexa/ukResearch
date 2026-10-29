using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrasaResarch.Models;

namespace PrasaResarch.Services;
public class SettingsService
{
    public string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrasaResearch");
    public AppSettings Settings { get; private set; }
    public string ApiKey { get; private set; } = "";
    public SettingsService()
    {
        Directory.CreateDirectory(Root);
        Settings = File.Exists(Path.Combine(Root, "settings.json")) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(Root, "settings.json"))) ?? new() : new();
        try { if (File.Exists(Path.Combine(Root, "credential.bin"))) ApiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(Root, "credential.bin")), null, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { /* A moved profile requires entering the key again. */ }
    }
    public void Save(string key)
    {
        if (Settings.OcrMaxPages < 0) throw new ArgumentException("Maximum OCR pages must be zero (all pages) or a positive number.");
        if (Settings.TimeoutSeconds is < 5 or > 600 || Settings.Concurrency is < 1 or > 16 || Settings.RequestsPerFiveMinutes is < 1 or > 10000)
            throw new ArgumentException("Timeout must be 5–600 seconds, concurrency 1–16, and rate 1–10000 requests per five minutes.");
        Directory.CreateDirectory(Settings.DownloadFolder); Directory.CreateDirectory(Settings.TemporaryFolder);
        File.WriteAllText(Path.Combine(Root, "settings.json"), JsonSerializer.Serialize(Settings));
        File.WriteAllBytes(Path.Combine(Root, "credential.bin"), ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
        ApiKey = key.Trim();
    }
}
