using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrasaResarch.Data;
using PrasaResarch.Services;
using PrasaResarch.ViewModels;
using PrasaResarch.Models;
using System.Windows.Threading;

namespace PrasaResarch.Tests;
public class WindowTests
{
    private sealed class StreamingProcessor : ICompanyProcessor, IProcessingProgressSource
    {
        public int Calls { get; private set; }
        public event Action<CompanyResult>? ResultUpdated;
        public async Task<CompanyResult> ProcessAsync(string number, CancellationToken ct)
        {
            Calls++;
            var result = new CompanyResult { CompanyNumber = number, CompanyName = "Offline test company", CompanyStatus = "active", Stage = "Reading PDF page 1 of 2" };
            ResultUpdated?.Invoke(result.Snapshot());
            await Task.Delay(150, ct);
            result.Status = "Needs Review"; result.Stage = "Needs Review"; result.Evidence = "Offline fixture evidence";
            return result;
        }
    }
    [Fact]
    public async Task WindowLoadsEveryNavigationSectionAndRenders()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
            try
            {
                var application = new App(); application.InitializeComponent();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var settings = new SettingsService(); using var http = new System.Net.Http.HttpClient();
                var api = new CompaniesHouseApiService(http, settings, new()); var documents = new CompaniesHouseDocumentService(api);
                var store = new JobPersistenceService(path);
                typeof(SettingsService).GetProperty(nameof(SettingsService.ApiKey))!.SetValue(settings, "offline-window-fixture");
                var processor = new StreamingProcessor();
                var vm = new MainViewModel(settings, api, new(), new(), store, new(processor, store), documents);
                var window = new MainWindow(vm); window.Show(); window.UpdateLayout();
                var primary = (System.Windows.Controls.DataGrid)window.FindName("PrimaryResultsGrid");
                Assert.Equal(new[] { "Company Registration Number", "Company Name", "Latest Revenue", "Currency", "Financial Year", "Extraction Status", "Source Document" }, primary.Columns.Select(c => c.Header.ToString()));
                foreach (var section in vm.Navigation) { vm.Section = section; window.UpdateLayout(); Assert.True(window.IsVisible); }
                vm.Number = "1234"; vm.SearchCommand.Execute(null);
                var frame = new DispatcherFrame(); var elapsed = System.Diagnostics.Stopwatch.StartNew();
                bool sawDetailsDuringProcessing = false;
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
                {
                    if (vm.Busy && vm.Selected?.CompanyName == "Offline test company") sawDetailsDuringProcessing = true;
                    if (!vm.Busy || elapsed.Elapsed > TimeSpan.FromSeconds(5)) frame.Continue = false;
                }, Dispatcher.CurrentDispatcher);
                Dispatcher.PushFrame(frame); timer.Stop();
                Assert.False(vm.Busy); Assert.True(sawDetailsDuringProcessing);
                Assert.Equal("Results", vm.Section); Assert.Equal(100, vm.Progress);
                Assert.Contains("Offline fixture evidence", vm.SelectedDetails);
                frame = new DispatcherFrame(); elapsed.Restart();
                vm.RetryCommand.Execute(null); timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
                Assert.False(vm.Busy); Assert.Equal(2, processor.Calls);
                var sourceBytes = ExtractionPipelineTests.TextPdf(); File.WriteAllBytes(path + ".pdf", sourceBytes);
                vm.Selected!.PeriodEnd = new(2024, 12, 31); vm.Selected.SourceUrl = "https://example.test/filing";
                vm.Selected.LocalDocumentPath = path + ".pdf"; vm.Selected.DocumentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sourceBytes));
                vm.ReviewAmount = "1234.56"; vm.ReviewCurrency = "GBP"; vm.ReviewEntity = "Company";
                vm.ReviewPage = "1"; vm.ReviewPeriod = "2024-12-31"; vm.ReviewReason = "Inspected the original fixture statement"; vm.SourceInspected = true;
                vm.ConfirmRevenueCommand.Execute(null);
                Assert.Equal("Verified", vm.Selected.Status); Assert.Single(store.Reviews(vm.Selected.CompanyNumber)); Assert.Contains("Inspected the original", vm.ReviewHistory);
                vm.ClearCommand.Execute(null);
                vm.Section = "Dashboard"; window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts")); Directory.CreateDirectory(output);
                using (var file = File.Create(Path.Combine(output, "dashboard.png"))) encoder.Save(file);
                window.Close(); application.Shutdown(); completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); File.Delete(path + ".pdf"); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
