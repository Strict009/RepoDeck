using Avalonia;
using Avalonia.Controls;

namespace RepoDeck.Controls;

/// <summary>
/// Lays results out in equal columns, choosing the column count from the width available.
/// </summary>
/// <remarks>
/// A <c>WrapPanel</c> full of fixed-width cards packs as many columns as will fit, which
/// on a wide monitor produced four narrow cards with room for a thumbnail and half a
/// sentence. Three columns of real width beats four of nothing, so the count is capped
/// and the cards take the space instead.
///
/// The column rule is a pure static function so it can be tested at every width without
/// a window; the panel itself only does arithmetic on the result.
/// </remarks>
public class ResponsiveCardsPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<ResponsiveCardsPanel, double>(nameof(MinItemWidth), 300d);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<ResponsiveCardsPanel, int>(nameof(MaxColumns), 3);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ResponsiveCardsPanel, double>(nameof(Spacing), 16d);

    static ResponsiveCardsPanel()
    {
        AffectsMeasure<ResponsiveCardsPanel>(MinItemWidthProperty, MaxColumnsProperty, SpacingProperty);
    }

    /// <summary>Narrower than this and a card stops being worth showing as a card.</summary>
    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <summary>Three by default: wide cards read better than many narrow ones.</summary>
    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// How many columns fit. Always at least one, so a very narrow window shows a single
    /// full-width column rather than nothing at all.
    /// </summary>
    public static int ColumnsFor(double availableWidth, double minItemWidth, double spacing, int maxColumns)
    {
        var cap = Math.Max(maxColumns, 1);

        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return cap;
        }

        var minimum = Math.Max(minItemWidth, 1);
        var gap = Math.Max(spacing, 0);

        // n columns need n items plus n-1 gaps.
        var fitting = (int)Math.Floor((availableWidth + gap) / (minimum + gap));

        return Math.Clamp(fitting, 1, cap);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children;
        if (children.Count == 0) return default;

        var columns = ColumnsFor(availableSize.Width, MinItemWidth, Spacing, MaxColumns);
        var gap = Math.Max(Spacing, 0);
        var itemWidth = ItemWidth(availableSize.Width, columns, gap);

        var rowHeight = 0d;
        var totalHeight = 0d;

        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);

            var lastInRow = (i + 1) % columns == 0 || i == children.Count - 1;
            if (!lastInRow) continue;

            totalHeight += rowHeight + gap;
            rowHeight = 0;
        }

        // The trailing gap belongs outside the panel, not inside it.
        totalHeight = Math.Max(totalHeight - gap, 0);

        var width = double.IsInfinity(availableSize.Width)
            ? itemWidth * columns + gap * (columns - 1)
            : availableSize.Width;

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children;
        if (children.Count == 0) return finalSize;

        var columns = ColumnsFor(finalSize.Width, MinItemWidth, Spacing, MaxColumns);
        var gap = Math.Max(Spacing, 0);
        var itemWidth = ItemWidth(finalSize.Width, columns, gap);

        var y = 0d;
        var rowHeight = 0d;

        for (var i = 0; i < children.Count; i++)
        {
            var column = i % columns;
            var x = column * (itemWidth + gap);

            // Every card in a row gets the same height, so the footers line up and the
            // grid reads as a grid rather than a ragged pile.
            rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);

            var lastInRow = column == columns - 1 || i == children.Count - 1;
            if (!lastInRow) continue;

            var first = i - column;
            for (var j = first; j <= i; j++)
            {
                var jx = (j - first) * (itemWidth + gap);
                children[j].Arrange(new Rect(jx, y, itemWidth, rowHeight));
            }

            y += rowHeight + gap;
            rowHeight = 0;
        }

        return new Size(finalSize.Width, Math.Max(y - gap, 0));
    }

    private static double ItemWidth(double availableWidth, int columns, double gap)
    {
        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return 320;
        }

        return Math.Max((availableWidth - gap * (columns - 1)) / columns, 1);
    }
}
