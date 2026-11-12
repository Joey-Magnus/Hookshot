using HookshotLauncher.Core;

namespace HookshotLauncher.ViewModels;

public sealed record WizardStep(int Number, string Title);

/// The build wizard's three steps, placeholders for now (the design comes next): 1 the version's tools download while the game
/// files are picked, 2 the build's settings, 3 the build with one overall bar (Core: Toolchains, MakeGameCommand, BuildProgress)
public sealed class WizardViewModel : ViewModelBase
{
    int step = 1;
    BuildEntry? update;
    string? target;

    public WizardViewModel(MainViewModel main)
    {
        Main = main;
        BackCommand = new Command(Back);
        NextCommand = new Command(Next);
        CancelCommand = new Command(Main.ShowHome);
    }

    public MainViewModel Main { get; }

    public IReadOnlyList<WizardStep> Steps { get; } = new[]
    {
        new WizardStep(1, "Game files"), new WizardStep(2, "Settings"), new WizardStep(3, "Build"),
    };

    public int Step
    {
        get => step;
        private set
        {
            if (!Set(ref step, value)) return;
            foreach (var n in new[] { nameof(Title), nameof(Body), nameof(NextText), nameof(StepText) }) Raise(n);
        }
    }

    public string StepText => $"Step {step} of 3";
    public string Kind => update == null
        ? $"New build · {BuildItemViewModel.ChannelLabel(Main.Service.Settings.DefaultChannel)}"
        : $"Update {update.Name}{(target != null ? " to " + target : "")}";

    public string Title => step switch
    {
        1 => "Your game files",
        2 => "Build settings",
        _ => "Building",
    };

    public string Body => step switch
    {
        1 => "The build tools for the chosen version download here, with their progress, while you pick your .nup file or your " +
             "romfs and exefs folders.",
        2 => "NVIDIA DLSS, AMD FSR, build memory, build workers, the game's language and whether to prepare the shaders now. " +
             "They are remembered for the next build." +
             (Saves.MoveOnBuild(Main.Service.Settings.MoveSaves, Main.Service.Platform, Main.Service.Registry.Builds.Count)
                 ? " Your saves are copied from the build you choose." : ""),
        _ => "One bar for the whole build: the tools' download, reading the game, translating, compiling, putting the game " +
             "together and preparing the shaders.",
    };

    public string NextText => step == 3 ? "Done" : "Next";

    public Command BackCommand { get; }
    public Command NextCommand { get; }
    public Command CancelCommand { get; }

    public void Begin(BuildEntry? updating, string? to)
    {
        update = updating;
        target = to;
        step = 0;
        Step = 1;
        Raise(nameof(Kind));
    }

    void Back()
    {
        if (step == 1) Main.ShowHome();
        else Step--;
    }

    void Next()
    {
        if (step == 3) Main.ShowHome();
        else Step++;
    }
}
