using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;

namespace HookshotLauncher.Controls;

/// The mockup's icons (its app.js ICONS: 24x24, stroked or filled in the text's colour), drawn at the control's size
public sealed class Icon : Control
{
    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<Icon, string>(nameof(Kind), "check");
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Icon>();
    /// The strokes' width times this: a small icon drawn heavier stays crisp (the Linux title bar's buttons)
    public static readonly StyledProperty<double> WeightProperty = AvaloniaProperty.Register<Icon, double>(nameof(Weight), 1.0);

    static Icon()
    {
        AffectsRender<Icon>(KindProperty, ForegroundProperty, WeightProperty);
    }

    public Icon()
    {
        Width = 18;
        Height = 18;
    }

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Weight
    {
        get => GetValue(WeightProperty);
        set => SetValue(WeightProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// (path, stroke width; 0: filled)
    static readonly Dictionary<string, (string Data, double Stroke)[]> Shapes = new()
    {
        ["play"] = new[] { ("M7 4.5v15l12-7.5z", 0.0) },
        ["check"] = new[] { ("M5 12.5l4.5 4.5L19 7", 2.4) },
        ["star"] = new[] { ("M12 3.6l2.6 5.3 5.8.8-4.2 4.1 1 5.8L12 16.9l-5.2 2.7 1-5.8-4.2-4.1 5.8-.8z", 0.0) },
        ["starLine"] = new[] { ("M12 3.6l2.6 5.3 5.8.8-4.2 4.1 1 5.8L12 16.9l-5.2 2.7 1-5.8-4.2-4.1 5.8-.8z", 1.7) },
        ["folder"] = new[] { ("M3.5 7.5a2 2 0 0 1 2-2h4l2 2h7a2 2 0 0 1 2 2v7.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z", 1.7) },
        ["trash"] = new[] { ("M5 7h14M10 7V5h4v2M7 7l1 12h8l1-12", 1.7) },
        ["gear"] = new[]
        {
            ("M15 12a3 3 0 1 1-6 0a3 3 0 1 1 6 0z", 1.5),
            ("M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z", 1.5),
        },
        ["download"] = new[] { ("M12 4v11m0 0l-4.5-4.5M12 15l4.5-4.5M5 19.5h14", 1.9) },
        ["plus"] = new[] { ("M12 5v14M5 12h14", 2.0) },
        ["alert"] = new[] { ("M12 7.5v6", 2.4), ("M13.4 17a1.4 1.4 0 1 1-2.8 0a1.4 1.4 0 1 1 2.8 0z", 0.0) },
        ["arrowUp"] = new[] { ("M12 19V6m0 0l-5 5m5-5l5 5", 2.0) },
        ["min"] = new[] { ("M7 12h10", 1.2) },
        ["close"] = new[] { ("M7 7l10 10M17 7L7 17", 1.2) },
        ["disk"] = new[] { ("M5 4.5h11l3.5 3.5v11.5a1 1 0 0 1-1 1h-13a1 1 0 0 1-1-1v-14a1 1 0 0 1 1-1z M8 4.5v5h7v-5 M8 20.5v-6h8v6", 1.6) },
        ["swap"] = new[] { ("M7 7h11m0 0l-3.5-3.5M18 7l-3.5 3.5M17 17H6m0 0l3.5-3.5M6 17l3.5 3.5", 1.8) },
        ["arrowRight"] = new[] { ("M5 12h13m0 0l-5-5m5 5l-5 5", 2.0) },
        ["upload"] = new[] { ("M12 15V4m0 0L7.5 8.5M12 4l4.5 4.5M5 19.5h14", 1.9) },
        ["dash"] = new[] { ("M8 12h8", 2.0) },
        ["info"] = new[] { ("M12 10.5v6", 2.2), ("M13.3 7.3a1.3 1.3 0 1 1-2.6 0a1.3 1.3 0 1 1 2.6 0z", 0.0) },
        ["clock"] = new[] { ("M20 12a8 8 0 1 1-16 0a8 8 0 1 1 16 0z", 1.7), ("M12 8v4.5l3 2", 1.7) },
        ["chevron"] = new[] { ("M7 10l5 5 5-5", 1.6) },
    };

    static readonly Dictionary<string, Geometry> Parsed = new();

    static Geometry Geo(string data)
    {
        lock (Parsed)
        {
            if (!Parsed.TryGetValue(data, out var g)) Parsed[data] = g = StreamGeometry.Parse(data);
            return g;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (!Shapes.TryGetValue(Kind, out var parts) || Foreground is not { } brush) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var scale = size / 24;
        var offset = new Vector((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
            foreach (var (data, stroke) in parts)
            {
                var g = Geo(data);
                if (stroke == 0) context.DrawGeometry(brush, null, g);
                else context.DrawGeometry(null, new Pen(brush, stroke * Weight, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g);
            }
    }
}

/// A progress bar: a rounded track, a fill to Value (0..1), and notches between a build's stages
public sealed class Bar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Bar, double>(nameof(Value));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<Bar, IBrush?>(nameof(Fill), Brushes.RoyalBlue);
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<Bar, IBrush?>(nameof(Track), new SolidColorBrush(Color.Parse("#12FFFFFF")));
    public static readonly StyledProperty<IReadOnlyList<double>?> NotchesProperty = AvaloniaProperty.Register<Bar, IReadOnlyList<double>?>(nameof(Notches));

    static Bar()
    {
        AffectsRender<Bar>(ValueProperty, FillProperty, TrackProperty, NotchesProperty);
    }

    public Bar() => Height = 6;

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public IReadOnlyList<double>? Notches { get => GetValue(NotchesProperty); set => SetValue(NotchesProperty, value); }

    static readonly IBrush Notch = new SolidColorBrush(Color.Parse("#9914161A"));

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var r = h / 2;
        var clip = new RoundedRect(new Rect(0, 0, w, h), r);
        using (context.PushClip(clip))
        {
            if (Track != null) context.DrawRectangle(Track, null, clip);
            var v = Math.Clamp(double.IsFinite(Value) ? Value : 0, 0, 1);
            if (v > 0 && Fill != null) context.DrawRectangle(Fill, null, new RoundedRect(new Rect(0, 0, Math.Max(h, w * v), h), r));
            if (Notches != null)
                foreach (var n in Notches)
                    context.DrawRectangle(Notch, null, new Rect(w * n - 1, 0, 2, h));
        }
    }
}

/// The mockup's spinner: a faint ring with a turning arc
public sealed class Spinner : Control
{
    static readonly IPen Ring = new Pen(new SolidColorBrush(Color.Parse("#26FFFFFF")), 2);
    static readonly IPen Arc = new Pen(new SolidColorBrush(Color.Parse("#4B70F0")), 2, lineCap: PenLineCap.Round);
    DispatcherTimer? timer;
    double angle;

    public Spinner()
    {
        Width = 14;
        Height = 14;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
        {
            angle = (angle + 15) % 360;
            InvalidateVisual();
        });
        timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer?.Stop();
        timer = null;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var r = size / 2 - 1;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        context.DrawEllipse(null, Ring, c, r, r);
        var a0 = (angle - 90) * Math.PI / 180;
        var a1 = a0 + Math.PI / 2;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false);
            ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, false, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, Arc, g);
    }
}
