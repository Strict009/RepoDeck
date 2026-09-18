using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RepoDeck.Models;

namespace RepoDeck.Controls;

/// <summary>
/// Artwork drawn for a project that has no usable pictures of its own.
/// </summary>
/// <remarks>
/// A coloured square with a letter in it says only "this is a thing", and a page of them
/// says "RepoDeck found nothing". This draws a plain geometric mark for the kind of thing
/// the project appears to be - a window, a prompt, a controller, a waveform - over a
/// faint grid, in the same colours as everything else.
///
/// It is RepoDeck's own artwork and must never be mistaken for the project's. Nothing here
/// invents a screenshot, a logo or a brand: the marks are generic shapes, the same for
/// every project of a kind, and the card labels them with the kind rather than presenting
/// them as the project's own. Where the kind is unknown it falls back to the initial,
/// because guessing a picture would be worse than admitting there isn't one.
/// </remarks>
public class KindArtwork : Control
{
    public static readonly StyledProperty<ProjectKind> KindProperty =
        AvaloniaProperty.Register<KindArtwork, ProjectKind>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<KindArtwork, IBrush?>(nameof(Accent));

    public static readonly StyledProperty<string> InitialProperty =
        AvaloniaProperty.Register<KindArtwork, string>(nameof(Initial), "?");

    public static readonly StyledProperty<double> MarkScaleProperty =
        AvaloniaProperty.Register<KindArtwork, double>(nameof(MarkScale), 0.38);

    static KindArtwork()
    {
        AffectsRender<KindArtwork>(KindProperty, AccentProperty, InitialProperty, MarkScaleProperty);
    }

    public ProjectKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>The ink. Supplied by the caller so the mark matches the tile behind it.</summary>
    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Shown when the kind is unknown, because a shape would be a guess.</summary>
    public string Initial
    {
        get => GetValue(InitialProperty);
        set => SetValue(InitialProperty, value);
    }

    /// <summary>The mark's size as a fraction of the shorter edge.</summary>
    public double MarkScale
    {
        get => GetValue(MarkScaleProperty);
        set => SetValue(MarkScaleProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 4 || bounds.Height <= 4) return;

        var ink = Accent ?? Brushes.Black;
        var size = Math.Min(bounds.Width, bounds.Height) * Math.Clamp(MarkScale, 0.1, 0.9);
        var centre = new Point(bounds.Width / 2, bounds.Height / 2);

        DrawGrid(context, bounds, ink);

        if (Kind == ProjectKind.Unknown)
        {
            DrawInitial(context, centre, size, ink);
            return;
        }

        var pen = new Pen(ink, Math.Max(size * 0.07, 2))
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        switch (Kind)
        {
            case ProjectKind.DesktopApplication: DrawWindow(context, centre, size, pen, ink); break;
            case ProjectKind.CommandLineTool: DrawPrompt(context, centre, size, pen, ink); break;
            case ProjectKind.GameOrEmulator: DrawController(context, centre, size, pen, ink); break;
            case ProjectKind.Audio: DrawWaveform(context, centre, size, ink); break;
            case ProjectKind.Video: DrawFilmFrame(context, centre, size, pen, ink); break;
            case ProjectKind.Utility: DrawGear(context, centre, size, pen, ink); break;
            case ProjectKind.DeveloperTool: DrawAngleBrackets(context, centre, size, pen); break;
            case ProjectKind.Library: DrawStack(context, centre, size, pen); break;
        }
    }

    // ---- The marks --------------------------------------------------------

    /// <summary>A window with a title bar: the universal shape of "a program".</summary>
    private static void DrawWindow(DrawingContext context, Point c, double s, Pen pen, IBrush ink)
    {
        var w = s * 1.25;
        var h = s * 0.95;
        var rect = new Rect(c.X - w / 2, c.Y - h / 2, w, h);

        context.DrawRectangle(null, pen, rect, 2, 2);

        // A title bar with three buttons in it. Outlined rather than filled, so the dots
        // can simply be drawn on top without needing to know the colour behind them.
        var barHeight = h * 0.28;
        var bar = new Rect(rect.X, rect.Y, rect.Width, barHeight);

        context.DrawLine(pen, new Point(bar.X, bar.Bottom), new Point(bar.Right, bar.Bottom));

        var dot = barHeight * 0.16;
        for (var i = 0; i < 3; i++)
        {
            var x = rect.Right - barHeight * 0.55 - i * dot * 3.2;
            context.DrawEllipse(ink, null, new Point(x, bar.Y + barHeight / 2), dot, dot);
        }
    }

    /// <summary>A chevron and a caret: a terminal waiting for you to type.</summary>
    private static void DrawPrompt(DrawingContext context, Point c, double s, Pen pen, IBrush ink)
    {
        var w = s * 1.25;
        var h = s * 0.95;
        var rect = new Rect(c.X - w / 2, c.Y - h / 2, w, h);

        context.DrawRectangle(null, pen, rect, 2, 2);

        var left = rect.X + w * 0.18;
        var mid = rect.Y + h * 0.5;
        var arm = h * 0.18;

        var chevron = new StreamGeometry();
        using (var g = chevron.Open())
        {
            g.BeginFigure(new Point(left, mid - arm), false);
            g.LineTo(new Point(left + arm, mid));
            g.LineTo(new Point(left, mid + arm));
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, chevron);
        context.FillRectangle(ink, new Rect(left + arm * 1.7, mid - arm * 0.55, w * 0.28, arm * 1.1));
    }

    /// <summary>A pad: two shoulders, a cross and two buttons.</summary>
    private static void DrawController(DrawingContext context, Point c, double s, Pen pen, IBrush ink)
    {
        var w = s * 1.35;
        var h = s * 0.8;
        var body = new Rect(c.X - w / 2, c.Y - h / 2, w, h);

        context.DrawRectangle(null, pen, body, h * 0.42, h * 0.42);

        var arm = h * 0.2;
        var padX = body.X + w * 0.24;

        context.DrawLine(pen, new Point(padX - arm, c.Y), new Point(padX + arm, c.Y));
        context.DrawLine(pen, new Point(padX, c.Y - arm), new Point(padX, c.Y + arm));

        var r = h * 0.11;
        context.DrawEllipse(ink, null, new Point(body.Right - w * 0.19, c.Y - r * 1.3), r, r);
        context.DrawEllipse(ink, null, new Point(body.Right - w * 0.30, c.Y + r * 1.3), r, r);
    }

    /// <summary>Bars of a level meter, which is what audio software looks like.</summary>
    private static void DrawWaveform(DrawingContext context, Point c, double s, IBrush ink)
    {
        double[] heights = [0.35, 0.62, 0.95, 0.5, 0.78, 0.42, 0.9, 0.55, 0.3];

        var w = s * 1.4;
        var barWidth = w / (heights.Length * 1.7);
        var gap = barWidth * 0.7;
        var total = heights.Length * barWidth + (heights.Length - 1) * gap;
        var x = c.X - total / 2;

        foreach (var height in heights)
        {
            var barHeight = s * height;
            context.FillRectangle(ink, new Rect(x, c.Y - barHeight / 2, barWidth, barHeight));
            x += barWidth + gap;
        }
    }

    /// <summary>A frame of film: sprocket holes down both sides.</summary>
    private static void DrawFilmFrame(DrawingContext context, Point c, double s, Pen pen, IBrush ink)
    {
        var w = s * 1.3;
        var h = s * 0.95;
        var rect = new Rect(c.X - w / 2, c.Y - h / 2, w, h);

        context.DrawRectangle(null, pen, rect, 2, 2);

        var hole = h * 0.13;
        var margin = w * 0.07;

        for (var i = 0; i < 3; i++)
        {
            var y = rect.Y + h * (0.22 + i * 0.28) - hole / 2;
            context.FillRectangle(ink, new Rect(rect.X + margin, y, hole, hole));
            context.FillRectangle(ink, new Rect(rect.Right - margin - hole, y, hole, hole));
        }

        var play = new StreamGeometry();
        using (var g = play.Open())
        {
            var size = h * 0.24;
            g.BeginFigure(new Point(c.X - size * 0.45, c.Y - size), true);
            g.LineTo(new Point(c.X + size * 0.75, c.Y));
            g.LineTo(new Point(c.X - size * 0.45, c.Y + size));
            g.EndFigure(true);
        }

        context.DrawGeometry(ink, null, play);
    }

    /// <summary>A cog: a ring with teeth, which is what a tool has always looked like.</summary>
    private static void DrawGear(DrawingContext context, Point c, double s, Pen pen, IBrush ink)
    {
        var outer = s * 0.55;
        var inner = s * 0.26;

        context.DrawEllipse(null, pen, c, outer, outer);
        context.DrawEllipse(null, pen, c, inner, inner);

        const int teeth = 8;
        for (var i = 0; i < teeth; i++)
        {
            var angle = i * (2 * Math.PI / teeth);
            var from = new Point(c.X + Math.Cos(angle) * outer, c.Y + Math.Sin(angle) * outer);
            var to = new Point(c.X + Math.Cos(angle) * (outer * 1.32), c.Y + Math.Sin(angle) * (outer * 1.32));

            context.DrawLine(pen, from, to);
        }
    }

    /// <summary>Angle brackets, which every programmer reads instantly.</summary>
    private static void DrawAngleBrackets(DrawingContext context, Point c, double s, Pen pen)
    {
        var reach = s * 0.42;
        var spread = s * 0.5;

        var left = new StreamGeometry();
        using (var g = left.Open())
        {
            g.BeginFigure(new Point(c.X - spread * 0.35, c.Y - reach), false);
            g.LineTo(new Point(c.X - spread, c.Y));
            g.LineTo(new Point(c.X - spread * 0.35, c.Y + reach));
            g.EndFigure(false);
        }

        var right = new StreamGeometry();
        using (var g = right.Open())
        {
            g.BeginFigure(new Point(c.X + spread * 0.35, c.Y - reach), false);
            g.LineTo(new Point(c.X + spread, c.Y));
            g.LineTo(new Point(c.X + spread * 0.35, c.Y + reach));
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, left);
        context.DrawGeometry(null, pen, right);
        context.DrawLine(pen, new Point(c.X + s * 0.1, c.Y - reach), new Point(c.X - s * 0.1, c.Y + reach));
    }

    /// <summary>Stacked plates: parts meant to be built with rather than run.</summary>
    private static void DrawStack(DrawingContext context, Point c, double s, Pen pen)
    {
        var w = s * 1.2;
        var h = s * 0.22;

        for (var i = 0; i < 3; i++)
        {
            var y = c.Y - h * 1.9 + i * h * 1.6;
            context.DrawRectangle(null, pen, new Rect(c.X - w / 2, y, w, h), 2, 2);
        }
    }

    private void DrawInitial(DrawingContext context, Point c, double s, IBrush ink)
    {
        var text = new FormattedText(
            Initial,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            s * 1.6,
            ink);

        context.DrawText(text, new Point(c.X - text.Width / 2, c.Y - text.Height / 2));
    }

    /// <summary>
    /// The same faint grid the old tiles had, so a mixed page still looks like one page.
    /// </summary>
    private static void DrawGrid(DrawingContext context, Rect bounds, IBrush ink)
    {
        var pen = new Pen(ink, 1) { Brush = ink };
        const double step = 16;

        using (context.PushOpacity(0.12))
        {
            for (var x = step; x < bounds.Width; x += step)
            {
                context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
            }

            for (var y = step; y < bounds.Height; y += step)
            {
                context.DrawLine(pen, new Point(0, y), new Point(bounds.Width, y));
            }
        }
    }
}
