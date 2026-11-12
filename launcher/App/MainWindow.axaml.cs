using System.Diagnostics;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using HookshotLauncher.ViewModels;

namespace HookshotLauncher;

/// The window: the mockup's own title bar (drag it to move the window; minimize and close in its corner), the page, a dialog over
/// it, the toast; and what the view models ask of it (IWindowHost)
public sealed partial class MainWindow : Window, IWindowHost
{
    public MainWindow()
    {
        InitializeComponent();
        // (pages cross-fade quickly; with less motion asked for, they change at once and nothing in the window animates)
        if (Motion.Reduced) Classes.Add("still");
        Pages.PageTransition = Motion.Reduced ? null : new CrossFade(TimeSpan.FromMilliseconds(140));
        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        AddHandler(DragDrop.DragEnterEvent, (_, e) => Drag(e, true));
        AddHandler(DragDrop.DragOverEvent, (_, e) => Drag(e, true));
        AddHandler(DragDrop.DragLeaveEvent, (_, e) => Drag(e, false));
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (DataContext is not MainViewModel vm) return;
            var files = e.DataTransfer.TryGetFiles();
            vm.Drop(files?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? new List<string>());
        });
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel vm) vm.Host = this;
    }

    /// Closing while a build runs asks first, as Cancel does (make_game and its compilers would go on with no window and no row in
    /// the list); step 1's download and checks stop with the window
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is not MainViewModel vm) return;
        if (vm.Page is BuildStepViewModel { Running: true } build)
        {
            e.Cancel = true;
            vm.Modal = new StopBuildViewModel(build);
            return;
        }
        if (vm.Page is FilesStepViewModel files) files.Job.Stop();
        else if (vm.Page is SettingsStepViewModel settings) settings.Job.Stop();
    }

    void Drag(DragEventArgs e, bool over)
    {
        if (DataContext is not MainViewModel vm) return;
        var ok = over && vm.AcceptsDrop;
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        vm.Dropping = ok;
    }

    public async Task<string?> PickFileAsync(string title, string? pattern)
    {
        var options = new FilePickerOpenOptions { Title = title, AllowMultiple = false };
        if (pattern != null) options.FileTypeFilter = new[] { new FilePickerFileType(pattern) { Patterns = new[] { pattern } } };
        var files = await StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title, string? start)
    {
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (start != null && Directory.Exists(start)) options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start);
        var folders = await StorageProvider.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task CopyAsync(string text)
    {
        if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    /// A folder in the file manager, or a link in the browser
    public void Open(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            if (DataContext is MainViewModel vm) vm.ShowToast("Could not open " + path + ": " + e.Message);
        }
    }

    public void Minimize() => WindowState = WindowState.Minimized;
}
