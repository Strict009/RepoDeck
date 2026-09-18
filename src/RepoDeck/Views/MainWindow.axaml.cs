using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RepoDeck.ViewModels;

namespace RepoDeck.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Tunnelling, so Back works wherever the focus happens to be. Bubbling would let a
        // text box or a list swallow the key first, and "Back does nothing while the cursor
        // is in the search box" is exactly the kind of thing nobody reports and everybody
        // notices.
        AddHandler(KeyDownEvent, OnKeyDownPreview, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPointerPressedPreview, RoutingStrategies.Tunnel);
    }

    private void OnKeyDownPreview(object? sender, KeyEventArgs e)
    {
        // Alt+Left, the same gesture as every browser and file manager.
        if (e.Key == Key.Left && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (TryGoBack()) e.Handled = true;
        }
    }

    private void OnPointerPressedPreview(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);

        // The side button on a mouse that has one. XButton1 is Back on every mouse that
        // follows the convention, which is effectively all of them.
        if (point.Properties.IsXButton1Pressed)
        {
            if (TryGoBack()) e.Handled = true;
        }
    }

    /// <summary>
    /// A sidebar destination is a way out from anywhere, including from inside itself.
    /// </summary>
    /// <remarks>
    /// The ListBox selection alone cannot do this. Somebody on a details page reached from
    /// Discover still has Discover selected, so clicking Discover changes no selection,
    /// raises no event, and leaves them looking at the page they were trying to leave.
    /// Handling the tap instead means the destination always navigates.
    /// </remarks>
    private void OnNavigationTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel model) return;

        // The tap may land on the text or the icon inside the row, so walk up to the row.
        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (item?.DataContext is not NavigationItem destination) return;

        model.GoToSectionCommand.Execute(destination);
    }

    private bool TryGoBack()
    {
        if (DataContext is not MainWindowViewModel model) return false;
        if (!model.GoBackCommand.CanExecute(null)) return false;

        model.GoBackCommand.Execute(null);
        return true;
    }
}
