using System.Windows;
using PrasaResarch.ViewModels;

namespace PrasaResarch;
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
        ApiKeyBox.Password = viewModel.InitialApiKey; viewModel.ReadPassword = () => ApiKeyBox.Password;
        Closing += (_, e) =>
        {
            if (!viewModel.Busy) return;
            e.Cancel = true; viewModel.CancelCommand.Execute(null);
            viewModel.Message = "Stopping safely. Completed results are saved. Close again after processing stops.";
        };
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Import_DragOver(object sender, DragEventArgs e) { e.Effects = !viewModel.Busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void Import_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) await viewModel.ImportAsync(files[0]); }
}

