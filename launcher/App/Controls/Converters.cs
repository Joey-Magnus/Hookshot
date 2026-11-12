using Avalonia.Data.Converters;
using Avalonia.Media;
using HookshotLauncher.Core;

namespace HookshotLauncher.Controls;

public static class Converters
{
    static readonly IBrush Selected = new SolidColorBrush(Color.Parse("#143D63E6"));

    /// The build whose saves the page shows: tinted
    public static readonly IValueConverter ThisRow = new FuncValueConverter<bool, IBrush>(b => b ? Selected : Brushes.Transparent);

    /// A version as players see it: a nightly as "Nightly 2029 · c544828f" (Versions.Shown), any other as it is
    public static readonly IValueConverter ShownVersion = new FuncValueConverter<string?, string>(Versions.Shown);
}
