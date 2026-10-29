using Avalonia;
namespace PrasaResearch.Desktop;
internal static class Program
{
    public static string? SmokeOutput { get; private set; }
    [STAThread]
    public static void Main(string[] args)
    {
        SmokeOutput = args.FirstOrDefault(a => a.StartsWith("--smoke-output="))?.Split('=', 2)[1];
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
