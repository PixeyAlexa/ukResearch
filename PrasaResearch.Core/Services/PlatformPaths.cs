namespace PrasaResarch.Services;
public static class PlatformPaths
{
    public static string LegacyWindowsData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrasaResearch");
    public static string Data => Environment.GetEnvironmentVariable("PRASARESEARCH_DATA_DIR") is { Length: > 0 } custom ? Path.GetFullPath(custom) : OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "PrasaResearch") : Path.Combine(LegacyWindowsData, "Desktop");
    public static string Cache => OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "PrasaResearch") : Data;
    public static string DefaultTesseract
    {
        get
        {
            var bundled = OperatingSystem.IsMacOS()
                ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "ocr", "tesseract"))
                : Path.Combine(AppContext.BaseDirectory, "ocr", "tesseract.exe");
            if (File.Exists(bundled)) return bundled;
            if (OperatingSystem.IsWindows()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe");
            return File.Exists("/opt/homebrew/bin/tesseract") ? "/opt/homebrew/bin/tesseract" : "/usr/local/bin/tesseract";
        }
    }
    public static string Tessdata(string executable)
    {
        var bundled = Path.Combine(Path.GetDirectoryName(executable)!, "tessdata");
        if (File.Exists(Path.Combine(bundled, "eng.traineddata"))) return bundled;
        foreach (var path in new[] { "/opt/homebrew/share/tessdata", "/usr/local/share/tessdata" }) if (File.Exists(Path.Combine(path, "eng.traineddata"))) return path;
        return bundled;
    }
}
