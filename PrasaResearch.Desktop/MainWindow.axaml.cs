using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PrasaResarch.ViewModels;
using Avalonia.VisualTree;
namespace PrasaResearch.Desktop;
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    public MainWindow() : this(null!) { }
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent(); viewModel = vm; if (vm == null) return; DataContext = vm;
        ApiKeyBox.Text = vm.InitialApiKey; vm.ReadPassword = () => ApiKeyBox.Text ?? "";
        PointerPressed += (_, e) =>
        {
            var p = e.GetPosition(this); const int grip = 6;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            WindowEdge? edge = p.X < grip ? (p.Y < grip ? WindowEdge.NorthWest : p.Y > Bounds.Height - grip ? WindowEdge.SouthWest : WindowEdge.West) : p.X > Bounds.Width - grip ? (p.Y < grip ? WindowEdge.NorthEast : p.Y > Bounds.Height - grip ? WindowEdge.SouthEast : WindowEdge.East) : p.Y < grip ? WindowEdge.North : p.Y > Bounds.Height - grip ? WindowEdge.South : null;
            if (edge.HasValue) { BeginResizeDrag(edge.Value, e); e.Handled = true; }
        };
        Closing += (_, e) => { if (!vm.Busy) return; e.Cancel = true; vm.CancelCommand.Execute(null); vm.Message = "Stopping safely. Close again after processing stops."; };
    }
    private void Title_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button || e.Source is Control control && control.FindAncestorOfType<Button>() != null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this); if (p.X < 6 || p.Y < 6 || p.X > Bounds.Width - 6) return;
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else BeginMoveDrag(e);
    }
    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    private void Import_DragOver(object? sender, DragEventArgs e) { e.DragEffects = viewModel.Busy ? DragDropEffects.None : DragDropEffects.Copy; e.Handled = true; }
    private async void Import_Drop(object? sender, DragEventArgs e) { var files = e.DataTransfer.TryGetFiles(); var path = files?.FirstOrDefault()?.TryGetLocalPath(); if (path != null) await viewModel.ImportAsync(path); }
}
