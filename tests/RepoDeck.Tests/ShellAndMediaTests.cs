using RepoDeck.Models;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// The shell's furniture, and what Quick Look does when the pictures do not arrive.
/// </summary>
public class ShellAndMediaTests
{
    // ---- The sidebar foot appears only when it has something to say -------

    [Fact]
    public void A_healthy_allowance_is_not_shown_at_all()
    {
        // ALLOWANCE OK with a fourteen-segment meter is RepoDeck's accounting, shown
        // permanently to somebody who came here to find software.
        var healthy = RateLimitPresentation.From(new RateLimitStatus
        {
            Limit = 60,
            Remaining = 55
        });

        Assert.True(healthy.IsKnown);
        Assert.False(healthy.IsLow);
    }

    [Fact]
    public void A_low_allowance_is_still_reported()
    {
        var low = RateLimitPresentation.From(new RateLimitStatus
        {
            Limit = 60,
            Remaining = 8
        });

        Assert.True(low.IsLow);
        Assert.Contains("LOW", low.Label);
    }

    [Fact]
    public void An_exhausted_allowance_is_reported_however_the_arithmetic_falls()
    {
        var spent = RateLimitPresentation.From(new RateLimitStatus
        {
            Limit = 60,
            Remaining = 0
        });

        Assert.True(spent.IsLow);
    }

    [Fact]
    public void Nothing_is_claimed_before_github_has_been_contacted()
    {
        Assert.False(RateLimitPresentation.Unknown.IsKnown);
        Assert.False(RateLimitPresentation.Unknown.IsLow);
    }

    // ---- Media: absence reads as absence ----------------------------------

    [Fact]
    public void A_tile_that_never_loaded_is_not_counted_as_a_picture()
    {
        // The defect this protects against: the strip counted candidates rather than
        // arrivals, so a 404 reserved an empty black frame in the gallery.
        var tile = new MediaTileViewModel("https://example.com/missing.png", null);

        Assert.False(tile.IsLoaded);
    }

    [Fact]
    public void A_tile_knows_its_own_accessible_name()
    {
        var described = new MediaTileViewModel("https://example.com/a.png", "A screenshot of the editor");
        var bare = new MediaTileViewModel("https://example.com/b.png", null);

        Assert.False(string.IsNullOrWhiteSpace(described.AccessibleName));
        Assert.False(string.IsNullOrWhiteSpace(bare.AccessibleName));
    }
}
