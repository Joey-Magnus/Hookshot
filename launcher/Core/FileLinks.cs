using System.Runtime.InteropServices;

namespace HookshotLauncher.Core;

/// Hard links, which .NET has no call for: a part kept from another installed version takes no second copy on the same disk
static class FileLinks
{
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    static extern int UnixLink([MarshalAs(UnmanagedType.LPUTF8Str)] string from, [MarshalAs(UnmanagedType.LPUTF8Str)] string to);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WindowsLink(string to, string from, IntPtr security);

    /// A hard link at to of the file from; false when the disk or the system does not make one (the caller copies)
    public static bool TryHardLink(string from, string to)
    {
        try
        {
            return OperatingSystem.IsWindows() ? WindowsLink(to, from, IntPtr.Zero) : UnixLink(from, to) == 0;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// A copy of the file from at to: a hard link when it can be, else its bytes (and its mode, on Linux)
    public static void LinkOrCopy(string from, string to)
    {
        if (TryHardLink(from, to)) return;
        File.Copy(from, to, overwrite: false);
    }
}
