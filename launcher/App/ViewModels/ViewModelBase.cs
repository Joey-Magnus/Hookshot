using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace HookshotLauncher.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void RaiseAll(params string[] names)
    {
        foreach (var n in names) Raise(n);
    }

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

/// What the view models ask of the window: the system's pickers, the clipboard, opening a folder, the window's own buttons.
/// Null in the screenshots
public interface IWindowHost
{
    Task<string?> PickFileAsync(string title, string? pattern);
    Task<string?> PickFolderAsync(string title, string? start);
    Task CopyAsync(string text);
    void Open(string path);
    void Minimize();
    void Close();
}

/// A path as the screens show it: on Linux, the home folder as ~ (~/Games/Hookshot). Only for reading: a path a player can edit,
/// or one handed to a program, stays whole
public static class ShownPath
{
    public static string Of(string? path)
    {
        if (string.IsNullOrEmpty(path) || OperatingSystem.IsWindows()) return path ?? "";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/');
        if (home.Length <= 1) return path;
        if (path == home) return "~";
        return path.StartsWith(home + "/", StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }

    /// A path a player typed, ~ read back as the home folder (Linux)
    public static string Typed(string path)
    {
        if (OperatingSystem.IsWindows() || !(path == "~" || path.StartsWith("~/", StringComparison.Ordinal))) return path;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/') + path[1..];
    }
}

/// The UI thread: what a background task (a download, a build) changes is handed over here
public static class Ui
{
    public static Action<Action> Post { get; set; } = a => Avalonia.Threading.Dispatcher.UIThread.Post(a);
}
