using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using PrasaResarch.Commands;
using PrasaResarch.Data;
using PrasaResarch.Models;
using PrasaResarch.Services;

namespace PrasaResarch.ViewModels;
public partial class MainViewModel : Observable
{
    private sealed class UiResultProgress(Action<CompanyResult> update) : IProgress<CompanyResult>
    {
        public void Report(CompanyResult value) => Application.Current.Dispatcher.Invoke(() => update(value));
    }
    private readonly SettingsService settings;
    private readonly ICompaniesHouseApi api;
    private readonly BulkImportService importer;
    private readonly ExportService exporter;
    private readonly JobPersistenceService persistence;
    private readonly BulkProcessingService processing;
    private readonly CompaniesHouseDocumentService documents;
    private CancellationTokenSource? cancellation;
    private ImportTable? table;
    private ImportPreview? preview;
    public ObservableCollection<CompanyResult> Results { get; } = [];
    public ICollectionView ResultsView { get; }
    public AppSettings Settings => settings.Settings;
    public string[] Navigation { get; } = ["Dashboard", "Single Company Search", "Bulk Import", "Results", "Export", "Settings"];
    public string[] Filters { get; } = ["All", "Verified", "Found", "Not Disclosed", "Needs Review", "OCR Required", "Extraction Failed", "Pending", "No Accounts", "Document Unavailable"];
    public string[] ExportScopes { get; } = ["All results", "Filtered results", "Successful results", "Failed results"];
    public string[] Themes { get; } = ["Midnight", "Deep Ocean"];
    private string section = "Dashboard";
    public string Section { get => section; set { Set(ref section, value); Changed(nameof(IsDashboard)); Changed(nameof(IsSingle)); Changed(nameof(IsBulk)); Changed(nameof(IsBulkDetails)); Changed(nameof(IsResults)); Changed(nameof(IsExport)); Changed(nameof(IsSettings)); } }
    public bool IsDashboard => Section == "Dashboard";
    public bool IsSingle => Section is "Dashboard" or "Single Company Search";
    public bool IsBulk => Section is "Dashboard" or "Bulk Import";
    public bool IsBulkDetails => Section == "Bulk Import";
    public bool IsResults => Section is "Dashboard" or "Results";
    public bool IsExport => Section == "Export";
    public bool IsSettings => Section == "Settings";
    private string number = "";
    public string Number { get => number; set => Set(ref number, value); }
    private string message = "Add your API key in Settings to connect to Companies House.";
    public string Message { get => message; set => Set(ref message, value); }
    private string connection = "Connection not tested";
    public string Connection { get => connection; set => Set(ref connection, value); }
    private string search = "";
    public string Search { get => search; set { Set(ref search, value); ResultsView.Refresh(); } }
    private string filter = "All";
    public string Filter { get => filter; set { Set(ref filter, value); ResultsView.Refresh(); } }
    private string exportScope = "All results";
    public string ExportScope { get => exportScope; set => Set(ref exportScope, value); }
    private bool busy;
    public bool Busy { get => busy; private set { Set(ref busy, value); Changed(nameof(ProcessingCount)); CommandManager.InvalidateRequerySuggested(); } }
    private bool paused;
    public bool Paused { get => paused; private set { Set(ref paused, value); Changed(nameof(PauseLabel)); } }
    public string PauseLabel => Paused ? "Resume" : "Pause";
    public int Total => Results.Count;
    public int Found => Results.Count(r => r.Status is "Found" or "Verified");
    public int NotDisclosed => Results.Count(r => r.Status == "Not Disclosed");
    public int Failed => Results.Count(r => r.Status is "Extraction Failed" or "Document Unavailable");
    public int Remaining => Results.Count(r => r.Status == "Pending");
    public int Completed => Total - Remaining;
    private int processingCount;
    public int ProcessingCount { get => processingCount; private set => Set(ref processingCount, value); }
    public double Progress => Total == 0 ? 0 : Completed * 100d / Total;
    public string ProgressText => $"Completed {Completed:N0} / {Total:N0}  •  Remaining {Remaining:N0}";
    private CompanyResult? selected;
    public CompanyResult? Selected { get => selected; set { Set(ref selected, value); Changed(nameof(SelectedDetails)); ReviewSelectionChanged(); } }
    public string SelectedDetails => Selected == null ? "Select a result to inspect source evidence and company details." :
        $"Company: {Selected.CompanyName} ({Selected.CompanyNumber})\nCompany status: {Selected.CompanyStatus}    Type: {Selected.CompanyType}\nRegistered office: {Selected.Address}\n\nRevenue: {Selected.RevenueDisplay}    Extraction: {Selected.Status}\nActivity: {Selected.Stage}\nReporting period end: {Selected.PeriodEnd:yyyy-MM-dd}    Filed: {Selected.FilingDate:yyyy-MM-dd}\nMethod: {Selected.Method}    Entity: {Selected.ReportingEntity}    Validation: {Selected.Confidence}\n\n{Selected.Evidence}\n{Selected.Error}";
    public ObservableCollection<string> ImportColumns { get; } = [];
    private int importColumn;
    public int ImportColumn { get => importColumn; set { Set(ref importColumn, value); RefreshPreview(); } }
    private string importInfo = "Upload a file containing company registration numbers. Leading zeros are preserved.";
    public string ImportInfo { get => importInfo; set => Set(ref importInfo, value); }
    private string importPreviewText = "";
    public string ImportPreviewText { get => importPreviewText; set => Set(ref importPreviewText, value); }
    public ICommand NavigateCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand QueueImportCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ProfileCommand { get; }
    public ICommand FilingCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand RefreshCommand { get; }
    public Func<string>? ReadPassword { get; set; }
    public string InitialApiKey => settings.ApiKey;
    public MainViewModel(SettingsService settings, ICompaniesHouseApi api, BulkImportService importer, ExportService exporter, JobPersistenceService persistence, BulkProcessingService processing, CompaniesHouseDocumentService documents)
    {
        this.settings = settings; this.api = api; this.importer = importer; this.exporter = exporter; this.persistence = persistence; this.processing = processing; this.documents = documents;
        InitializeReview();
        processing.ActiveCountChanged += count => Application.Current.Dispatcher.BeginInvoke(() => ProcessingCount = processing.ActiveCount);
        foreach (var r in persistence.Load())
        {
            if (r.Status == "Found" || r.Status == "Verified" && (r.ConsolidatedAccounts || r.Candidates.Any(c => c.Statement && c.Entity == "Group")) && r.ReportingEntity != "Group")
            {
                r.Status = "Needs Review";
                r.Confidence = "Retry required under latest annual revenue policy";
                r.Evidence = "This saved result predates latest-only/consolidated-primary validation. Its former figure remains in audit data; retry the latest accounts before publishing revenue.\n" + r.Evidence;
                persistence.Save(r);
            }
            Results.Add(r);
        }
        ResultsView = CollectionViewSource.GetDefaultView(Results);
        ResultsView.Filter = item => item is CompanyResult r && (Filter == "All" || r.Status == Filter) && ($"{r.CompanyNumber} {r.CompanyName}".Contains(Search, StringComparison.OrdinalIgnoreCase));
        NavigateCommand = new RelayCommand(p => Section = p?.ToString() ?? "Dashboard");
        SearchCommand = new AsyncCommand(async _ => { var n = CompanyNumber.Normalize(Number); AddPending(n); Search = ""; Filter = "All"; Section = "Results"; Selected = Results.First(r => r.CompanyNumber == n); await RunAsync([n]); }, ShowError, _ => !Busy);
        BrowseCommand = new AsyncCommand(async _ => { var dialog = new OpenFileDialog { Filter = "Excel or CSV|*.xlsx;*.csv" }; if (dialog.ShowDialog() == true) await ImportAsync(dialog.FileName); }, ShowError, _ => !Busy);
        QueueImportCommand = new RelayCommand(_ => { if (preview == null) return; foreach (var n in preview.Valid) AddPending(n); NotifyCounts(); Message = $"Queued {preview.Valid.Count:N0} valid numbers. Existing results were retained."; }, _ => !Busy && preview?.Valid.Count > 0);
        StartCommand = new AsyncCommand(_ => RunAsync(Results.Where(r => r.Status == "Pending").Select(r => r.CompanyNumber).ToList()), ShowError, _ => !Busy && Remaining > 0);
        PauseCommand = new RelayCommand(_ => { if (Paused) processing.Resume(); else processing.Pause(); Paused = !Paused; Message = Paused ? "Paused. Active requests will finish and save; new companies will wait." : "Processing resumed."; }, _ => Busy);
        CancelCommand = new RelayCommand(_ => cancellation?.Cancel(), _ => Busy);
        RetryCommand = new AsyncCommand(async _ => { var items = Selected != null ? new[] { Selected } : Results.Where(r => r.Status is "Extraction Failed" or "Document Unavailable").ToArray(); foreach (var r in items) { r.Status = "Pending"; persistence.Save(r); } ResultsView.Refresh(); NotifyCounts(); await RunAsync(items.Select(r => r.CompanyNumber).ToList()); }, ShowError, _ => !Busy && (Selected != null || Failed > 0));
        ClearCommand = new RelayCommand(_ => { persistence.Clear(); Results.Clear(); Selected = null; NotifyCounts(); Message = "Results and queued jobs cleared."; }, _ => !Busy && Total > 0);
        ExportCommand = new AsyncCommand(async _ => await ExportAsync(), ShowError);
        ProfileCommand = new RelayCommand(p => { try { OpenUrl($"https://find-and-update.company-information.service.gov.uk/company/{CompanyNumber.Normalize(p?.ToString() == "Input" ? Number : Selected?.CompanyNumber ?? Number)}"); } catch (ArgumentException ex) { ShowError(ex); } });
        FilingCommand = new RelayCommand(_ => OpenUrl(Selected!.SourceUrl), _ => !string.IsNullOrEmpty(Selected?.SourceUrl));
        DownloadCommand = new AsyncCommand(async _ => await DownloadAsync(), ShowError, _ => !string.IsNullOrEmpty(Selected?.MetadataUrl));
        SaveSettingsCommand = new RelayCommand(_ => { try { SaveSettings(); } catch (Exception ex) { ShowError(ex); } }, _ => !Busy);
        TestConnectionCommand = new AsyncCommand(async _ => { SaveSettings(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(Settings.TimeoutSeconds)); await api.GetProfileAsync("00000006", ct.Token); Connection = "Connected to Companies House API"; Message = "Authenticated company profile request succeeded."; }, ex => { Connection = "Connection failed"; ShowError(ex); }, _ => !Busy);
        RefreshCommand = new RelayCommand(_ => { ResultsView.Refresh(); NotifyCounts(); Message = "Results view refreshed. Select a row and Retry Extraction to retrieve its latest filing."; });
    }
    private void SaveSettings() { settings.Save(ReadPassword?.Invoke() ?? settings.ApiKey); ApplyTheme(); Message = "Settings saved. Your API key is protected for your Windows user account."; }
    public void ApplyTheme() { if (Application.Current != null) Application.Current.Resources["AppBackground"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Settings.Theme == "Deep Ocean" ? "#031E2B" : "#060E20")); }
    private void AddPending(string n)
    {
        if (Results.Any(r => r.CompanyNumber == n)) return;
        var r = new CompanyResult { CompanyNumber = n }; persistence.Save(r); Results.Add(r); NotifyCounts();
    }
    private async Task RunAsync(IEnumerable<string> numbers)
    {
        var list = numbers.ToList(); if (list.Count == 0) return;
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw new InvalidOperationException("Save your API key in Settings before processing.");
        Busy = true; Paused = false; cancellation = new();
        var reporter = new UiResultProgress(r => { var old = Results.FirstOrDefault(x => x.CompanyNumber == r.CompanyNumber); if (old != null) Results[Results.IndexOf(old)] = r; else Results.Add(r); Selected = r; NotifyCounts(); Message = $"{r.CompanyNumber} • {(r.Status == "Pending" ? r.Stage : r.Status)}"; });
        try { await processing.RunAsync(list, Settings.Concurrency, reporter, cancellation.Token); Message = "Processing finished. Review flagged results before using the figures."; }
        catch (OperationCanceledException) { Message = "Cancelled. Completed results are saved; pending companies can be resumed with Start Processing."; }
        finally { processing.Resume(); Paused = false; Busy = false; cancellation.Dispose(); cancellation = null; NotifyCounts(); }
    }
    public async Task ImportAsync(string path)
    {
        if (Busy) { Message = "Wait until processing finishes before importing another file."; return; }
        try { table = await Task.Run(() => importer.Read(path)); ImportColumns.Clear(); foreach (var c in table.Columns) ImportColumns.Add(c); ImportColumn = importer.DetectColumn(table); RefreshPreview(); Section = "Bulk Import"; Message = $"Loaded {Path.GetFileName(path)}. Review the selected column, then add numbers to the queue."; }
        catch (Exception ex) { ShowError(ex); }
    }
    private void RefreshPreview()
    {
        if (table == null || ImportColumn < 0 || ImportColumn >= table.Columns.Length) return;
        preview = importer.Preview(table, ImportColumn);
        ImportInfo = $"{preview.Valid.Count:N0} valid • {preview.Duplicates:N0} duplicates removed • {preview.Invalid.Count:N0} invalid";
        ImportPreviewText = "VALID (first 30)\n" + string.Join("  ", preview.Valid.Take(30)) + "\n\nINVALID (first 30)\n" + string.Join("  ", preview.Invalid.Take(30));
        CommandManager.InvalidateRequerySuggested();
    }
    private async Task ExportAsync()
    {
        var rows = ExportScope switch { "Filtered results" => ResultsView.Cast<CompanyResult>().ToList(), "Successful results" => Results.Where(r => r.Status is "Found" or "Verified").ToList(), "Failed results" => Results.Where(r => r.Status is "Extraction Failed" or "Document Unavailable").ToList(), _ => Results.ToList() };
        if (rows.Count == 0) { Message = "No results match the selected export scope."; return; }
        var dialog = new SaveFileDialog { Filter = "Excel workbook|*.xlsx|CSV file|*.csv", FileName = $"Companies-House-{DateTime.Now:yyyyMMdd}", DefaultExt = ".xlsx" };
        if (dialog.ShowDialog() != true) return;
        await Task.Run(() => exporter.Export(dialog.FileName, rows)); Message = $"Exported {rows.Count:N0} results to {dialog.FileName}.";
    }
    private async Task DownloadAsync()
    {
        var row = Selected!; var meta = await documents.MetadataAsync(row.MetadataUrl, CancellationToken.None);
        var mime = meta.Types.Contains("application/pdf") ? "application/pdf" : meta.Types.FirstOrDefault(t => t is "application/xml" or "application/xhtml+xml");
        if (mime == null) throw new InvalidOperationException("This filing has no supported downloadable PDF/XML representation.");
        var extension = mime == "application/pdf" ? ".pdf" : ".xml";
        var dialog = new SaveFileDialog { FileName = row.CompanyNumber + "-accounts" + extension, Filter = "Source document|*" + extension };
        if (dialog.ShowDialog() != true) return;
        await File.WriteAllBytesAsync(dialog.FileName, await documents.DownloadAsync(meta.ContentUrl, mime, CancellationToken.None, update => Application.Current.Dispatcher.Invoke(() => Message = $"Downloading source — {update.Activity}: {update.Received / 1048576d:F1} MB"))); Message = "Source document downloaded.";
    }
    private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception ex) => Message = ex is ArgumentException or InvalidOperationException or IOException or ApiException or TimeoutException ? ex.Message : "The operation failed. Check the input file, connection, and settings, then retry.";
    private void NotifyCounts() { foreach (var property in new[] { nameof(Total), nameof(Found), nameof(NotDisclosed), nameof(Failed), nameof(Remaining), nameof(Completed), nameof(ProcessingCount), nameof(Progress), nameof(ProgressText) }) Changed(property); CommandManager.InvalidateRequerySuggested(); }
}

