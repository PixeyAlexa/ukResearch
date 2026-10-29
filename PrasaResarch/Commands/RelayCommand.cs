using System.Windows.Input;

namespace PrasaResarch.Commands;
public class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
}
public class AsyncCommand(Func<object?, Task> execute, Action<Exception> onError, Predicate<object?>? canExecute = null) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? p) => !running && (canExecute?.Invoke(p) ?? true);
    public async void Execute(object? p)
    {
        if (!CanExecute(p)) return; running = true; CommandManager.InvalidateRequerySuggested();
        try { await execute(p); } catch (OperationCanceledException) { } catch (Exception ex) { onError(ex); }
        finally { running = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
