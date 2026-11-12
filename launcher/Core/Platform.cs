namespace HookshotLauncher.Core;

/// The two platforms the launcher runs on: Windows (llvm-mingw builds of the game) and Linux
public enum Platform { Windows, Linux }

public static class Platforms
{
    public static Platform Current => OperatingSystem.IsWindows() ? Platform.Windows : Platform.Linux;

    /// The game's executable under its folder's bin/
    public static string GameExe(Platform platform) => platform == Platform.Windows ? "hookshot.exe" : "hookshot";

    /// The name of a version's parts list on a release: the Windows setup's and the Linux updater's (docs/launcher-upgrade.md, section 5)
    public static string PartsListName(Platform platform) => platform == Platform.Windows ? "hookshot-setup.txt" : "hookshot-linux.txt";
}
