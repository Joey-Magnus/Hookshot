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
            var service = LauncherService.Open();
            var main = new MainViewModel(service, Program.Start);
            desktop.MainWindow = new MainWindow { DataContext = main };
            // (the update check: at start unless turned off in the settings or offline; with nothing built it is needed to know what to
            // build, and --update with no --tag needs it to know what to update to)
            var offline = Environment.GetEnvironmentVariable("HOOKSHOT_LAUNCHER_OFFLINE") == "1";
            if (!offline && (service.Settings.CheckUpdates || service.Registry.Builds.Count == 0 || Program.Start.Kind == Core.StartKind.Update))
                _ = main.Home.CheckReleasesAsync();
            else service.Checked = true;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
