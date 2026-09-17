using System.Globalization;
using Avalonia.Data.Converters;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Decides whether a side panel sits beside the content or floats over it.
/// </summary>
/// <remarks>
/// Docking the Quick Look panel on a narrow window left the results about 270px wide:
/// names vanished, buttons were clipped and two labels sat on top of each other. Below
/// the threshold the panel covers the results instead, which is honest about there being
/// room for one thing at a time rather than pretending there is room for two.
///
/// This drives a two-column grid directly rather than going through a SplitView, whose
/// overlay mode closes its own pane on any click in the content area. That fought row
/// selection: clicking a result to open the panel closed it in the same gesture.
///
/// The rule is a plain static so it can be tested at every width without a window.
/// </remarks>
public sealed class PanelDisplayConverter : IValueConverter
{
    /// <summary>
    /// The narrowest content width worth docking a panel beside. Below this, a 380px
    /// panel leaves too little room for even one column of cards.
    /// </summary>
    public const double MinimumDockedContentWidth = 940;

    public static PanelDisplayConverter Instance { get; } = new();

    /// <summary>True when the panel should sit beside the content rather than over it.</summary>
    public static bool ShouldDock(double availableWidth)
    {
        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth)) return true;

        return availableWidth >= MinimumDockedContentWidth;
    }

    /// <summary>The grid column the panel occupies: its own when docked, the content's when not.</summary>
    public static int PanelColumn(double width) => ShouldDock(width) ? 1 : 0;

    /// <summary>How many columns it spans, which is what makes it overlay rather than push.</summary>
    public static int PanelColumnSpan(double width) => ShouldDock(width) ? 1 : 2;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var width = value switch
        {
            double d => d,
            int i => i,
            _ => double.NaN
        };

        return (parameter as string)?.ToLowerInvariant() switch
        {
            "span" => PanelColumnSpan(width),
            _ => PanelColumn(width)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A layout decision is never converted back to a width.");
}
