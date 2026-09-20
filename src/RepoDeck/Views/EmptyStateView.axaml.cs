using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RepoDeck.Views;

/// <summary>
/// The "there is nothing here yet" block, with the same shape on every page that needs one.
/// </summary>
/// <remarks>
/// Favorites, Installed and Downloads each hand-wrote a heading and a sentence in the same
/// arrangement, and they had already drifted apart in wording weight. Two styled properties
/// is the whole surface: a title and a sentence explaining what would appear here and how to
/// make that happen.
///
/// No action slot yet. One caller wanting a button is not evidence for a shared one.
/// </remarks>
public partial class EmptyStateView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(nameof(Title), "");

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(nameof(Message), "");

    public EmptyStateView() => InitializeComponent();

    /// <summary>The heading: what is missing, in three or four words.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What would appear here, and how to make that happen.</summary>
    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
