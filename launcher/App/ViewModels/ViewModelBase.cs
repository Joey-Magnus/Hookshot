using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace HookshotLauncher.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// A button's action, and whether it can run now
public sealed class Command : ICommand
{
    readonly Action run;
    readonly Func<bool>? can;

    public Command(Action run, Func<bool>? can = null)
    {
        this.run = run;
        this.can = can;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => can?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) run();
    }

    public void Changed() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
