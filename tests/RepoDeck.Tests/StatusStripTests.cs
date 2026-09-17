using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// The status strip states facts. It is the one part of the window that is always
/// visible, so anything it says wrong is wrong all the time.
/// </summary>
public class StatusStripTests
{
    private static RateLimitStatus Limit(
        int remaining, int limit = 60, bool authenticated = false, DateTimeOffset? resets = null) =>
        new()
        {
            Limit = limit,
            Remaining = remaining,
            IsAuthenticated = authenticated,
            ResetsAt = resets
        };

    [Fact]
    public void Before_the_first_response_nothing_is_claimed()
    {
        var view = RateLimitPresentation.From(RateLimitStatus.Unknown);

        Assert.False(view.IsKnown);
        Assert.Equal(0, view.Percent);
        Assert.Equal("", view.Label);
        Assert.Contains("has not contacted GitHub", view.Detail);
    }

    [Theory]
    [InlineData(60, 100)]
    [InlineData(30, 50)]
    [InlineData(0, 0)]
    public void The_meter_shows_the_share_of_the_allowance_that_is_left(int remaining, double expected)
    {
        Assert.Equal(expected, RateLimitPresentation.From(Limit(remaining)).Percent);
    }

    [Theory]
    [InlineData(60, "ALLOWANCE OK")]
    [InlineData(20, "ALLOWANCE OK")]
    [InlineData(14, "ALLOWANCE LOW")]
    [InlineData(0, "ALLOWANCE USED UP")]
    public void The_label_says_in_words_what_the_meter_shows_in_bars(int remaining, string expected)
    {
        // Someone who cannot read the lime bar still reads the sentence.
        Assert.Equal(expected, RateLimitPresentation.From(Limit(remaining)).Label);
    }

    [Fact]
    public void Only_an_exhausted_allowance_puts_a_warning_on_the_strip()
    {
        Assert.Equal("", RateLimitPresentation.From(Limit(20)).Warning);
        Assert.Equal("GitHub limit reached", RateLimitPresentation.From(Limit(0)).Warning);
    }

    [Fact]
    public void A_zero_limit_does_not_divide_by_zero()
    {
        var view = RateLimitPresentation.From(Limit(remaining: 0, limit: 0));

        Assert.True(view.IsKnown);
        Assert.Equal(0, view.Percent);
    }

    [Fact]
    public void The_tooltip_carries_the_numbers_and_the_strip_does_not()
    {
        var view = RateLimitPresentation.From(Limit(remaining: 43, limit: 60));

        Assert.Contains("43 of 60", view.Detail);
        Assert.DoesNotContain("43", view.Label);
    }

    [Fact]
    public void An_anonymous_session_is_told_why_its_allowance_is_small()
    {
        var anonymous = RateLimitPresentation.From(Limit(10, authenticated: false));
        var signedIn = RateLimitPresentation.From(Limit(10, limit: 5000, authenticated: true));

        Assert.Contains("Not signed in", anonymous.Detail);
        Assert.Contains("Settings", anonymous.Detail);
        Assert.Contains("Signed in", signedIn.Detail);
    }

    [Fact]
    public void A_reset_in_the_future_is_described_as_being_in_the_future()
    {
        // Reusing the "5 minutes ago" formatter here produced "Resets 43 minutes ago"
        // for something that had not happened yet.
        var now = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        var view = RateLimitPresentation.From(
            Limit(5, resets: now.AddMinutes(43)), now);

        Assert.Contains("Resets in 43 minutes.", view.Detail);
        Assert.DoesNotContain("ago", view.Detail);
    }

    [Fact]
    public void A_reset_time_that_has_passed_reads_sensibly()
    {
        var now = new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        var view = RateLimitPresentation.From(Limit(0, resets: now.AddMinutes(-2)), now);

        Assert.Contains("Resets shortly.", view.Detail);
    }
}

public class TimeUntilTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_unknown_moment_says_so()
    {
        Assert.Equal("unknown", Humanize.TimeUntil(null, Now));
    }

    [Theory]
    [InlineData(0, "shortly")]
    [InlineData(-60, "shortly")]
    [InlineData(30, "in less than a minute")]
    [InlineData(60, "in 1 minute")]
    [InlineData(150, "in 2 minutes")]
    [InlineData(3600, "in 1 hour")]
    [InlineData(7200, "in 2 hours")]
    [InlineData(86400, "in 1 day")]
    [InlineData(172800, "in 2 days")]
    public void A_future_moment_is_phrased_forwards(int seconds, string expected)
    {
        Assert.Equal(expected, Humanize.TimeUntil(Now.AddSeconds(seconds), Now));
    }
}
