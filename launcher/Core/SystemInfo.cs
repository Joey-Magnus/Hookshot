namespace HookshotLauncher.Core;

/// What the build settings say about this PC: its memory, its CPU threads and the makers of its graphics cards
public static class SystemInfo
{
    public const string Nvidia = "NVIDIA", Amd = "AMD", Intel = "Intel";

    /// The memory this PC has, in whole GB (what .NET may use: the PC's, or a container's limit)
    public static int MemoryGb() => (int)Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (double)(1L << 30));

    public static int Threads() => Environment.ProcessorCount;

    /// The PCI vendor id's maker: 0x10de NVIDIA, 0x1002 AMD, 0x8086 Intel; null for another
    public static string? VendorOf(string pciVendor) => pciVendor.Trim().ToLowerInvariant() switch
    {
        "0x10de" => Nvidia,
        "0x1002" => Amd,
        "0x8086" => Intel,
        _ => null,
    };

    /// The makers of the graphics cards Linux lists in /sys/class/drm (card&lt;n&gt;/device/vendor), each once, in card order.
    /// drm: that folder (the tests' stand-in). On Windows none are read yet (the step's "Your GPU" chip is left out)
    public static IReadOnlyList<string> GpuVendors(Platform platform, string drm = "/sys/class/drm")
    {
        var found = new List<string>();
        if (platform != Platform.Linux) return found;
        try
        {
            foreach (var card in Directory.EnumerateDirectories(drm, "card*").Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(card);
                if (name.Contains('-')) continue;   // (card0-DP-1 and the like are its outputs)
                var vendorFile = Path.Combine(card, "device", "vendor");
                if (!File.Exists(vendorFile)) continue;
                if (VendorOf(File.ReadAllText(vendorFile)) is { } v && !found.Contains(v)) found.Add(v);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return found;
    }
}
