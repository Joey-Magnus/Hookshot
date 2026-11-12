using Avalonia;
using HookshotLauncher.Core;

namespace HookshotLauncher;

static class Program
{
    /// The start's request (the 2.x command line: CommandLine.cs)
    public static StartRequest Start { get; private set; } = StartRequest.Parse(Array.Empty<string>());

    [STAThread]
    public static int Main(string[] args)
    {
        Start = StartRequest.Parse(args);
        if (Start.Kind == StartKind.Play) return PlayWithoutWindow(Start);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect()
        .UseWaylandWithFallback()   // (native Wayland when there is a compositor: through XWayland, a scaled desktop draws the window blurry)
        .LogToTrace();

    /// --play [folder]: that build's game, else the default build's, with no window (the hand-over is read first, as at any start)
    static int PlayWithoutWindow(StartRequest start)
    {
        try
        {
            var service = LauncherService.Open();
            var folder = start.Folder ?? service.Registry.Default?.Folder;
            if (folder == null || !GameFolder.IsBuilt(folder, service.Platform))
            {
                Console.Error.WriteLine("Hookshot: no built game to start. Open the launcher and build one, or pass its folder.");
                return 1;
            }
            using var _ = System.Diagnostics.Process.Start(GamePlay.StartInfo(Path.GetFullPath(folder), service.Platform));
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine("Hookshot: the game could not start: " + e.Message);
            return 1;
        }
    }
}
