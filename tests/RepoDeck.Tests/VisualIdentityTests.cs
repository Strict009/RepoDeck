using RepoDeck.Controls;
using RepoDeck.Infrastructure;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.Media;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// The visual pass introduced real behaviour as well as styling: categories that run
/// searches, and a design system that must not be bypassed.
/// </summary>
public class VisualIdentityTests
{
    private static DiscoverViewModel Discover(FakeGitHubClient github) =>
        new(github,
            new HeuristicRepositoryExplanationService(),
            new RepositoryMediaService(NullAppLog.Instance),
            NullAppLog.Instance);

    [Fact]
    public void Every_category_has_a_label_a_query_and_an_explanation()
    {
        Assert.NotEmpty(DiscoverCategory.All);

        foreach (var category in DiscoverCategory.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(category.Label));
            Assert.False(string.IsNullOrWhiteSpace(category.Query));
            Assert.False(string.IsNullOrWhiteSpace(category.Description));
        }
    }

    [Fact]
    public void A_category_query_is_more_specific_than_its_label()
    {
        // "Games" alone returns engines and tutorials, so the query does the work while
        // the label stays short enough to read on a tile.
        foreach (var category in DiscoverCategory.All)
        {
            Assert.True(category.Query.Length > category.Label.Length,
                $"{category.Label} has a query no more specific than its label.");
        }
    }

    [Fact]
    public async Task Choosing_a_category_runs_an_ordinary_search()
    {
        // No curated catalogue sits behind the tiles, and RepoDeck does not pretend one does.
        var github = new FakeGitHubClient { DefaultResults = [TestRepositories.Create()] };
        var vm = Discover(github);

        var games = DiscoverCategory.All.Single(c => c.Label == "Games");
        await vm.SearchCategoryCommand.ExecuteAsync(games);

        Assert.Equal(1, github.SearchCallCount);
        Assert.Equal(games.Query, vm.SearchText);
        Assert.Equal("Games", vm.ActiveCategory);
        Assert.True(vm.ShowResults);
    }

    [Fact]
    public async Task A_card_always_has_something_to_show_even_with_no_image()
    {
        var github = new FakeGitHubClient { DefaultResults = [TestRepositories.Create("shotcut")] };
        var vm = Discover(github);

        vm.SearchText = "video";
        await vm.SearchCommand.ExecuteAsync(null);

        var card = Assert.Single(vm.Results);

        // No image loader was supplied, so the fallback must carry the card.
        Assert.True(card.ShowFallback);
        Assert.False(card.ShowImage);
        Assert.Equal("S", card.FallbackInitial);
        Assert.NotNull(card.FallbackBrush);
        Assert.Equal("Shotcut", card.FriendlyName);
    }

    [Fact]
    public async Task A_card_leads_with_plain_English_rather_than_the_slug()
    {
        var github = new FakeGitHubClient
        {
            DefaultResults = [TestRepositories.Create(
                "obs-studio", "obsproject", description: "Software for live streaming.")]
        };

        var vm = Discover(github);
        vm.SearchText = "streaming";
        await vm.SearchCommand.ExecuteAsync(null);

        var card = Assert.Single(vm.Results);

        Assert.Equal("Obs Studio", card.FriendlyName);
        Assert.Contains("live streaming", card.Purpose);

        // The slug is still available, just not the headline.
        Assert.Equal("obs-studio", card.Repository.Name);
        Assert.Equal("obsproject", card.Owner);
    }

    [Fact]
    public void Views_contain_no_literal_colours()
    {
        // The whole point of a token system is that nothing bypasses it. A literal hex
        // value in a view is how a design system quietly stops being one.
        var views = Directory.GetFiles(ViewsDirectory(), "*.axaml");
        Assert.NotEmpty(views);

        var offenders = views
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(
                File.ReadAllText(f), "\"#[0-9A-Fa-f]{6,8}\""))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_accent_is_defined_once_and_reused()
    {
        var app = File.ReadAllText(Path.Combine(SourceRoot(), "App.axaml"));

        // One definition per theme variant, and a brush that everything else points at.
        var definitions = System.Text.RegularExpressions.Regex.Matches(
            app, "<Color x:Key=\"RdAccent\">").Count;

        Assert.Equal(2, definitions);
        Assert.Contains("<SolidColorBrush x:Key=\"RdAccentBrush\"", app);
    }

    [Fact]
    public void State_is_never_communicated_by_colour_alone()
    {
        // Every status light in the shell sits beside a word. Someone who cannot
        // distinguish lime from amber still reads READY, ONLINE or the platform name.
        var shell = File.ReadAllText(Path.Combine(SourceRoot(), "Views", "MainWindow.axaml"));

        var lights = System.Text.RegularExpressions.Regex.Matches(shell, "Classes=\"led").Count;
        var labels = System.Text.RegularExpressions.Regex.Matches(shell, "Classes=\"microLabel").Count
                     + System.Text.RegularExpressions.Regex.Matches(shell, "Classes=\"muted").Count;

        Assert.True(lights > 0);
        Assert.True(labels >= lights,
            $"{lights} status lights but only {labels} accompanying labels.");
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "RepoDeck");
    }

    private static string ViewsDirectory() => Path.Combine(SourceRoot(), "Views");
}


public class SegmentedMeterTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 5)]
    [InlineData(50, 10)]
    [InlineData(99, 20)]
    [InlineData(100, 20)]
    public void The_meter_lights_the_share_of_segments_it_was_given(double value, int expected)
    {
        Assert.Equal(expected, SegmentedMeter.LitSegments(value, 100, 20));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-9999)]
    public void A_negative_value_lights_nothing_rather_than_throwing(double value)
    {
        Assert.Equal(0, SegmentedMeter.LitSegments(value, 100, 20));
    }

    [Fact]
    public void A_value_beyond_the_maximum_shows_a_full_meter_rather_than_overflowing()
    {
        // A download reporting more bytes than the release advertised is a reporting
        // bug; the meter should be full, not draw past its own bounds.
        Assert.Equal(20, SegmentedMeter.LitSegments(140, 100, 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_meaningless_maximum_falls_back_to_a_percentage(double maximum)
    {
        Assert.Equal(10, SegmentedMeter.LitSegments(50, maximum, 20));
    }

    [Fact]
    public void A_meter_with_no_segments_still_has_one()
    {
        Assert.Equal(1, SegmentedMeter.LitSegments(100, 100, 0));
        Assert.Equal(0, SegmentedMeter.LitSegments(0, 100, -4));
    }

    [Fact]
    public void Nonsense_input_lights_nothing()
    {
        Assert.Equal(0, SegmentedMeter.LitSegments(double.NaN, 100, 20));
        Assert.Equal(0, SegmentedMeter.LitSegments(50, double.NaN, 20));
    }

    [Fact]
    public void The_meter_is_only_used_where_progress_is_actually_known()
    {
        // Where progress is indeterminate the interface shows a bar instead. A meter
        // displaying an invented percentage would be a particularly irritating lie.
        var view = File.ReadAllText(Path.Combine(SourceViews(), "RepositoryDetailsView.axaml"));

        Assert.Contains("SegmentedMeter", view);
        Assert.Contains("IsIndeterminate", view);
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
