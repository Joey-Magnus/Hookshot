using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

/// The window: one page at a time (home, the build wizard's steps, settings, saves)
public sealed class MainViewModel : ViewModelBase
{
    object page;

    public MainViewModel(LauncherService service, StartRequest start)
    {
        Service = service;
        Home = new HomeViewModel(this);
        Settings = new SettingsViewModel(this);
        Wizard = new WizardViewModel(this);
        Saves = new SavesViewModel(this);
        page = Home;
        Home.Open(start);
    }

    public LauncherService Service { get; }
    public HomeViewModel Home { get; }
    public SettingsViewModel Settings { get; }
    public WizardViewModel Wizard { get; }
    public SavesViewModel Saves { get; }

    public object Page
    {
        get => page;
        private set => Set(ref page, value);
    }

    public void ShowHome()
    {
        Home.Refresh();
        Page = Home;
    }

    public void ShowSettings()
    {
        Settings.Load();
        Page = Settings;
    }

    /// The build wizard (its steps are placeholders until the design is in): a new build, or update for that build
    public void ShowWizard(BuildEntry? update = null, string? target = null)
    {
        Wizard.Begin(update, target);
        Page = Wizard;
    }

    public void ShowSaves(BuildEntry? build = null)
    {
        Saves.Load(build);
        Page = Saves;
    }
}
