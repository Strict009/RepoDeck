using RepoDeck.Infrastructure;

namespace RepoDeck.Tests;

public class HumanizeTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1.0k")]
    [InlineData(1_500, "1.5k")]
    [InlineData(12_345, "12k")]
    [InlineData(999_999, "999k")]
    [InlineData(1_500_000, "1.5M")]
    public void Count_formats_large_numbers_compactly(int value, string expected)
    {
        Assert.Equal(expected, Humanize.Count(value));
    }

    [Fact]
    public void Count_treats_negative_values_as_zero()
    {
        Assert.Equal("0", Humanize.Count(-5));
    }

    [Fact]
    public void RelativeTime_reports_unknown_for_missing_dates()
    {
        Assert.Equal("unknown", Humanize.RelativeTime(null));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(90, "1 minute ago")]
    [InlineData(60 * 60 * 5, "5 hours ago")]
    [InlineData(60 * 60 * 24 * 3, "3 days ago")]
    [InlineData(60 * 60 * 24 * 45, "1 month ago")]
    [InlineData(60 * 60 * 24 * 400, "1 year ago")]
    public void RelativeTime_describes_the_past_in_plain_words(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var value = now.AddSeconds(-secondsAgo);

        Assert.Equal(expected, Humanize.RelativeTime(value, now));
    }

    [Fact]
    public void RelativeTime_does_not_produce_negative_ages_for_future_dates()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", Humanize.RelativeTime(now.AddDays(2), now));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")]
    public void FileSize_uses_binary_units(long bytes, string expected)
    {
        Assert.Equal(expected, Humanize.FileSize(bytes));
    }
}
