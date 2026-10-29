using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PrasaResarch.Data;
using PrasaResarch.Services;
using PrasaResarch.ViewModels;
using System.Net.Http;

namespace PrasaResearch.Desktop;
public partial class App : Application
{
    private HttpClient? http;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var settings = new SettingsService();
                http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                var api = new CompaniesHouseApiService(http, settings, new()); var documents = new CompaniesHouseDocumentService(api);
                var processor = new RevenueExtractionService(api, documents, new(), new(settings.Settings), settings);
                var store = new JobPersistenceService(settings);
                var vm = new MainViewModel(settings, api, new(), new(), store, new(processor, store), documents);
                desktop.MainWindow = new MainWindow(vm); DesktopFiles.Window = desktop.MainWindow; vm.ApplyTheme();
                if (Program.SmokeOutput is { } smoke)
                {
                    desktop.MainWindow.Opened += async (_, _) =>
                    {
                        try
                        {
                            foreach (var section in vm.Navigation) { vm.Section = section; await Task.Delay(100); }
                            vm.Section = "Dashboard";
                            await Task.Delay(200);
                            var window = desktop.MainWindow;
                            var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
                            bitmap.Render(window); bitmap.Save(smoke + ".png", new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); bitmap.Dispose();
                            await File.WriteAllTextAsync(smoke, System.Text.Json.JsonSerializer.Serialize(new { platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription, sections = vm.Navigation, width = window.Bounds.Width, height = window.Bounds.Height, launched = true }));
                            desktop.Shutdown(0);
                        }
                        catch (Exception ex) { await File.WriteAllTextAsync(smoke, ex.ToString()); desktop.Shutdown(1); }
                    };
                }
                desktop.Exit += (_, _) => http.Dispose();
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(PlatformPaths.Data);
                File.AppendAllText(Path.Combine(PlatformPaths.Data, "desktop-startup.log"), $"{DateTimeOffset.UtcNow:O} {ex}\n");
                if (Program.SmokeOutput is { } smoke) { File.WriteAllText(smoke, ex.ToString()); desktop.Shutdown(1); return; }
                desktop.MainWindow = new Avalonia.Controls.Window { Title = "PrasaResearch — startup error", Width = 600, Height = 220, Content = new Avalonia.Controls.TextBlock { Text = "Could not open settings, secure credentials or the job database. See desktop-startup.log in the application data folder. Your data has not been removed.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(28) } };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
