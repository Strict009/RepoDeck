using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RepoDeck.Controls;

/// <summary>
/// A segmented bar meter, in the manner of a late-1990s media player level display.
/// </summary>
/// <remarks>
/// Drawn directly rather than assembled from a hundred nested borders, which keeps it
/// cheap enough to sit on every card without anyone noticing.
///
/// It only ever shows a value it was actually given. Where progress is genuinely unknown
/// the interface uses an indeterminate bar instead - a meter that invents a percentage
/// would be lying in a particularly irritating way.
/// </remarks>
public class SegmentedMeter : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SegmentedMeter, double>(nameof(Value));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SegmentedMeter, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<int> SegmentCountProperty =
        AvaloniaProperty.Register<SegmentedMeter, int>(nameof(SegmentCount), 20);

    public static readonly StyledProperty<IBrush?> OnBrushProperty =
        AvaloniaProperty.Register<SegmentedMeter, IBrush?>(nameof(OnBrush));

    public static readonly StyledProperty<IBrush?> OffBrushProperty =
        AvaloniaProperty.Register<SegmentedMeter, IBrush?>(nameof(OffBrush));

    public static readonly StyledProperty<double> SegmentGapProperty =
        AvaloniaProperty.Register<SegmentedMeter, double>(nameof(SegmentGap), 2d);

    static SegmentedMeter()
    {
        AffectsRender<SegmentedMeter>(
            ValueProperty, MaximumProperty, SegmentCountProperty,
            OnBrushProperty, OffBrushProperty, SegmentGapProperty);
    }

    /// <summary>Current value. Clamped to the range when drawn.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>How many bars. Twenty reads as a meter; two hundred reads as a smear.</summary>
    public int SegmentCount
    {
        get => GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    public IBrush? OnBrush
    {
        get => GetValue(OnBrushProperty);
        set => SetValue(OnBrushProperty, value);
    }

    public IBrush? OffBrush
    {
        get => GetValue(OffBrushProperty);
        set => SetValue(OffBrushProperty, value);
    }

    public double SegmentGap
    {
        get => GetValue(SegmentGapProperty);
        set => SetValue(SegmentGapProperty, value);
    }

    /// <summary>
    /// How many bars light up for a value. Pure, so the rule can be verified without
    /// standing up a windowing system.
    /// </summary>
    /// <remarks>
    /// Values outside the range are clamped rather than rejected: a download that
    /// reports 101 percent is a reporting bug, and the meter should show a full bar
    /// rather than throw. Rounding is away from zero so a value that has essentially
    /// reached a segment boundary lights that bar instead of sitting one short.
    /// </remarks>
    internal static int LitSegments(double value, double maximum, int segments)
    {
        segments = Math.Max(segments, 1);

        if (double.IsNaN(value) || double.IsNaN(maximum)) return 0;

        var range = maximum <= 0 ? 100 : maximum;
        var fraction = Math.Clamp(value / range, 0, 1);

        return (int)Math.Round(fraction * segments, MidpointRounding.AwayFromZero);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var segments = Math.Max(SegmentCount, 1);
        var gap = Math.Max(SegmentGap, 0);
        var segmentWidth = (bounds.Width - gap * (segments - 1)) / segments;

        if (segmentWidth <= 0) return;

        var lit = LitSegments(Value, Maximum, segments);

        var on = OnBrush ?? Brushes.LimeGreen;
        var off = OffBrush ?? Brushes.DimGray;

        for (var i = 0; i < segments; i++)
        {
            var x = i * (segmentWidth + gap);
            var rectangle = new Rect(x, 0, segmentWidth, bounds.Height);

            context.FillRectangle(i < lit ? on : off, rectangle);
        }
    }
}
