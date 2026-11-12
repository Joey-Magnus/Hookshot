using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HookshotLauncher.ViewModels;

namespace HookshotLauncher;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // (the 2.x hand-over is read before anything is shown: docs/launcher-upgrade.md, section 3)
            var main = new MainViewModel(LauncherService.Open(), Program.Start);
            desktop.MainWindow = new MainWindow { DataContext = main };
            if (Environment.GetEnvironmentVariable("HOOKSHOT_LAUNCHER_OFFLINE") != "1") _ = main.Home.CheckReleasesAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
