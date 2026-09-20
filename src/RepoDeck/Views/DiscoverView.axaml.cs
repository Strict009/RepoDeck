using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Threading;
using RepoDeck.ViewModels;

namespace RepoDeck.Views;

public partial class DiscoverView : UserControl
{
    public DiscoverView()
    {
        InitializeComponent();

        // The shell swaps the whole page when navigating, so this view is destroyed on the
        // way to a details page and built again on the way back. The scroll position is
        // therefore parked on the view model, which survives, and restored here.
        //
        // Somebody who has scrolled through forty results and opened one of them should
        // come back to where they were, not to the top of a page they have already read.
        AttachedToVisualTree += (_, _) => RestoreScroll();
        DetachedFromVisualTree += (_, _) => RememberScroll();
    }

    private void RestoreScroll()
    {
        if (DataContext is not DiscoverViewModel model) return;
        if (model.ResultsScrollOffset <= 0) return;

        // Deferred: the results are still being laid out at this point, and a ScrollViewer
        // clamps an offset to the extent it currently knows about - which is nothing yet.
        Dispatcher.UIThread.Post(
            () => ResultsScroller.Offset = new Vector(0, model.ResultsScrollOffset),
            DispatcherPriority.Loaded);
    }

    private void RememberScroll()
    {
        if (DataContext is not DiscoverViewModel model) return;

        model.ResultsScrollOffset = ResultsScroller.Offset.Y;
    }

    private void OnResultSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Six visual lists represent two view styles and three result sources. A list that
        // does not contain the active card reports a null selection; only an affirmative
        // user selection may change the shared view-model selection.
        if (DataContext is not DiscoverViewModel model) return;
        if (e.AddedItems.OfType<RepositoryCardViewModel>().FirstOrDefault() is not { } selected) return;

        // Queued, not assigned here.
        //
        // This runs inside the ListBox's own selection update, while its selection model is
        // part-way through a batch operation. Setting SelectedResult synchronously makes the
        // view model do its selection work in that window - and that work includes applying
        // a deferred group move, which removes a card from the very ObservableCollection the
        // list is bound to. Avalonia's SelectedItems then enumerates a collection that
        // changed underneath it and throws ArgumentOutOfRangeException.
        //
        // Posting lets the selection operation finish first. The queue preserves order, so
        // rapid clicks still end on the last one.
        Dispatcher.UIThread.Post(() => model.SelectedResult = selected);
    }
}
