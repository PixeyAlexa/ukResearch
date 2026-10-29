using Microsoft.Extensions.DependencyInjection;
using PrasaResarch.Data;
using PrasaResarch.Services;
using PrasaResarch.ViewModels;
using System.Net.Http;
using System.Windows;

namespace PrasaResarch;
public partial class App : Application
{
    private ServiceProvider? services;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var collection = new ServiceCollection();
            collection.AddSingleton<SettingsService>();
            collection.AddSingleton(new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan });
            collection.AddSingleton<RequestThrottle>();
            collection.AddSingleton<ICompaniesHouseApi, CompaniesHouseApiService>();
            collection.AddSingleton<CompaniesHouseDocumentService>();
            collection.AddSingleton<XbrlExtractionService>();
            collection.AddSingleton<PdfExtractionService>(sp => new PdfExtractionService(sp.GetRequiredService<SettingsService>().Settings));
            collection.AddSingleton<ICompanyProcessor, RevenueExtractionService>();
            collection.AddSingleton<BulkImportService>(); collection.AddSingleton<ExportService>();
            collection.AddSingleton<JobPersistenceService>(sp => new JobPersistenceService(sp.GetRequiredService<SettingsService>()));
            collection.AddSingleton<BulkProcessingService>(); collection.AddSingleton<MainViewModel>();
            services = collection.BuildServiceProvider();
            var vm = services.GetRequiredService<MainViewModel>(); vm.ApplyTheme();
            MainWindow = new MainWindow(vm); MainWindow.Show();
        }
        catch (Exception ex)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrasaResearch");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "startup.log"), $"{DateTimeOffset.Now:O} {ex}\n");
            MessageBox.Show("The application could not load its settings or job database. Check the files in %LOCALAPPDATA%\\PrasaResearch and your folder permissions.", "PrasaResearch startup", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { services?.Dispose(); base.OnExit(e); }
}

