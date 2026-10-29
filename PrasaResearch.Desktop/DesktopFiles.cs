using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using System.Collections.ObjectModel;
using PrasaResarch.Models;

namespace PrasaResearch.Desktop;
public static class DesktopFiles
{
    public static Window? Window { get; set; }
    public static async Task<string?> OpenAsync()
    {
        var files = await Window!.StorageProvider.OpenFilePickerAsync(new() { Title = "Import company numbers", AllowMultiple = false, FileTypeFilter = [new("Excel or CSV") { Patterns = ["*.xlsx", "*.csv"] }] });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    public static async Task<string?> SaveAsync(string name, bool export)
    {
        var result = await Window!.StorageProvider.SaveFilePickerWithResultAsync(new() { Title = "Save document", SuggestedFileName = name, DefaultExtension = Path.GetExtension(name).TrimStart('.'), FileTypeChoices = export ? [new("Excel workbook") { Patterns = ["*.xlsx"] }, new("CSV file") { Patterns = ["*.csv"] }] : [new("Source document") { Patterns = ["*" + Path.GetExtension(name)] }] });
        var path = result.File?.TryGetLocalPath();
        if (path != null && export && result.SelectedFileType?.Patterns?.FirstOrDefault() is { } pattern) path = Path.ChangeExtension(path, pattern.Replace("*", ""));
        return path;
    }
    public static void ApplyTheme(string theme)
    { if (Window != null) Window.Background = new SolidColorBrush(Color.Parse(theme == "Deep Ocean" ? "#031E2B" : "#060E20")); }
    public static void OpenExternal(string value)
    {
        if (OperatingSystem.IsMacOS()) { var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false }; start.ArgumentList.Add(value); Process.Start(start); }
        else Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
    }
}
public sealed class ResultView : ObservableCollection<CompanyResult>
{
    private readonly ObservableCollection<CompanyResult> source;
    private Predicate<object>? filter;
    public Predicate<object>? Filter { get => filter; set { filter = value; Refresh(); } }
    public ResultView(ObservableCollection<CompanyResult> source) { this.source = source; source.CollectionChanged += (_, _) => Refresh(); Refresh(); }
    public void Refresh() { Clear(); foreach (var row in source.Where(r => Filter?.Invoke(r) ?? true)) Add(row); }
}
