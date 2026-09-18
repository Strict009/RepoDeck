using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// The picture strip. Selecting a thumbnail changes the large image without fetching
/// anything again, and the keyboard reaches it.
/// </summary>
public class QuickLookGalleryTests
{
    private static MediaTileViewModel Tile(string url, string? description = null) =>
        new(url, description);

    [Fact]
    public void A_tile_describes_itself_for_a_screen_reader()
    {
        Assert.Equal("Screenshot of the main window",
            Tile("https://example.invalid/1.png", "Screenshot of the main window").AccessibleName);
    }

    [Fact]
    public void A_tile_with_no_alt_text_still_has_something_to_announce()
    {
        // A blank accessible name is worse than a generic one.
        Assert.False(string.IsNullOrWhiteSpace(Tile("https://example.invalid/1.png").AccessibleName));
        Assert.False(string.IsNullOrWhiteSpace(Tile("https://example.invalid/1.png", "   ").AccessibleName));
    }

    [Fact]
    public void Only_one_tile_is_ever_the_selected_one()
    {
        var tiles = new[]
        {
            Tile("https://example.invalid/1.png"),
            Tile("https://example.invalid/2.png"),
            Tile("https://example.invalid/3.png")
        };

        foreach (var chosen in tiles)
        {
            foreach (var tile in tiles) tile.IsSelected = ReferenceEquals(tile, chosen);

            Assert.Single(tiles, t => t.IsSelected);
            Assert.True(chosen.IsSelected);
        }
    }

    [Fact]
    public void A_tile_holds_its_own_picture()
    {
        // This is what makes selection free: promoting a thumbnail swaps a reference
        // rather than starting a fetch.
        var tile = Tile("https://example.invalid/1.png");

        Assert.False(tile.IsLoaded);
        Assert.Null(tile.Image);
    }

    [Fact]
    public void The_strip_is_bound_to_selection_rather_than_to_a_click_handler()
    {
        // A ListBox with SelectedItem bound to the hero is what gives arrow-key movement
        // and a visible focus ring for nothing. This checks the view kept that shape.
        var view = File.ReadAllText(Path.Combine(SourceViews(), "QuickLookView.axaml"));

        Assert.Contains("Classes=\"thumbs\"", view);
        Assert.Contains("SelectedItem=\"{Binding Hero}\"", view);
        Assert.Contains("AutomationProperties.Name", view);
    }

    [Fact]
    public void The_selected_thumbnail_is_outlined_rather_than_merely_tinted()
    {
        // At 74 pixels a background tint is invisible.
        var theme = File.ReadAllText(Path.Combine(SourceViews(), "Theme.axaml"));

        Assert.Contains("ListBox.thumbs ListBoxItem:selected", theme);
        Assert.Contains("ListBox.thumbs ListBoxItem:focus-visible", theme);
    }

    private static string SourceViews()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "RepoDeck", "Views");
    }
}
