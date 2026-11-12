using Avalonia.Controls;

namespace HookshotLauncher.Views;

// The pages and dialogs: each its .axaml, bound to its view model (compiled bindings); no logic here

public sealed partial class HomeView : UserControl { public HomeView() => InitializeComponent(); }
public sealed partial class FilesStepView : UserControl { public FilesStepView() => InitializeComponent(); }
public sealed partial class SettingsStepView : UserControl
{
    public SettingsStepView()
    {
        InitializeComponent();
        // (opening Advanced scrolls the step's content, never the footer, to show it: the mockup's revealAdvanced)
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.SettingsStepViewModel vm)
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(vm.AdvancedOpen) && vm.AdvancedOpen)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Body.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Background);
                };
        };
    }

    /// For the screenshots: the content scrolled to its end, as opening Advanced leaves it
    public void ScrollToEnd() => Body.ScrollToEnd();
}
public sealed partial class BuildStepView : UserControl { public BuildStepView() => InitializeComponent(); }
public sealed partial class ReadyView : UserControl { public ReadyView() => InitializeComponent(); }
public sealed partial class ProblemView : UserControl { public ProblemView() => InitializeComponent(); }
public sealed partial class ReportView : UserControl { public ReportView() => InitializeComponent(); }
public sealed partial class SettingsView : UserControl { public SettingsView() => InitializeComponent(); }
public sealed partial class SavesView : UserControl { public SavesView() => InitializeComponent(); }
public sealed partial class SavesConfirmView : UserControl { public SavesConfirmView() => InitializeComponent(); }
public sealed partial class SavesResultView : UserControl { public SavesResultView() => InitializeComponent(); }
public sealed partial class ChannelPickerView : UserControl { public ChannelPickerView() => InitializeComponent(); }
public sealed partial class DeleteView : UserControl { public DeleteView() => InitializeComponent(); }
public sealed partial class LicenceView : UserControl { public LicenceView() => InitializeComponent(); }
public sealed partial class StopBuildView : UserControl { public StopBuildView() => InitializeComponent(); }
public sealed partial class SavePickView : UserControl { public SavePickView() => InitializeComponent(); }
