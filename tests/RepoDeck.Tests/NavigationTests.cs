using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.History;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// Going back, and knowing where back goes.
/// </summary>
/// <remarks>
/// RepoDeck previously had a single boolean and a hardcoded label reading "BACK TO
/// RESULTS", which was a lie whenever somebody had arrived from Favorites or from Discover
/// before searching for anything.
/// </remarks>
public class NavigationHistoryTests
{
    private static NavigationEntry Entry(string title, string label = "Back") =>
        new(new object(), title, label);

    [Fact]
    public void A_fresh_history_has_nowhere_to_go()
    {
        var history = new NavigationHistory();

        Assert.False(history.CanGoBack);
        Assert.Equal("", history.BackLabel);
        Assert.Null(history.Pop());
    }

    [Fact]
    public void Pushing_a_screen_makes_it_the_way_back()
    {
        var history = new NavigationHistory();
        history.Push(Entry("Discover", "Back to results"));

        Assert.True(history.CanGoBack);
        Assert.Equal("Back to results", history.BackLabel);
    }

    [Fact]
    public void Back_returns_the_screen_that_was_left()
    {
        var history = new NavigationHistory();
        var discover = Entry("Discover", "Back to results");

        history.Push(discover);

        Assert.Same(discover, history.Pop());
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void Back_walks_more_than_one_step()
    {
        var history = new NavigationHistory();
        history.Push(Entry("Discover", "Back to results"));
        history.Push(Entry("someone/tool", "Back to someone/tool"));

        Assert.Equal("Back to someone/tool", history.BackLabel);
        Assert.Equal("someone/tool", history.Pop()!.Title);
        Assert.Equal("Back to results", history.BackLabel);
        Assert.Equal("Discover", history.Pop()!.Title);
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void Choosing_a_destination_forgets_where_you_were()
    {
        // A top-level destination is a fresh start. Pressing Back afterwards must not walk
        // into the section somebody just deliberately left.
        var history = new NavigationHistory();
        history.Push(Entry("Discover"));
        history.Push(Entry("someone/tool"));

        history.Clear();

        Assert.False(history.CanGoBack);
        Assert.Equal("", history.BackLabel);
    }

    [Fact]
    public void The_history_is_bounded()
    {
        var history = new NavigationHistory();

        for (var i = 0; i < NavigationHistory.MaxDepth + 25; i++)
        {
            history.Push(Entry($"page {i}"));
        }

        Assert.Equal(NavigationHistory.MaxDepth, history.Depth);
    }

    [Fact]
    public void The_oldest_entries_are_the_ones_dropped()
    {
        var history = new NavigationHistory();

        for (var i = 0; i < NavigationHistory.MaxDepth + 1; i++)
        {
            history.Push(Entry($"page {i}"));
        }

        // The most recent is still the way back; it is the far end that went.
        Assert.Equal($"page {NavigationHistory.MaxDepth}", history.Pop()!.Title);
    }

    [Fact]
    public void The_label_is_recorded_when_leaving_not_worked_out_when_returning()
    {
        // Discover is two places depending on what is on it, and by the time somebody
        // presses Back the screen may no longer be in the state that made the label true.
        var history = new NavigationHistory();
        history.Push(Entry("Discover", "Back to results"));

        Assert.Equal("Back to results", history.Pop()!.BackLabel);
    }
}

/// <summary>
/// The Recently viewed shelf: local, bounded, and absent when there is nothing in it.
/// </summary>
public class RecentlyViewedTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-recent-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly RecentlyViewed _recent;

    public RecentlyViewedTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _recent = new RecentlyViewed(_paths, NullAppLog.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a temp folder */ }
    }

    private static GitHubRepository Repository(string name, string owner = "someone") =>
        TestRepositories.Create(name, owner: owner);

    [Fact]
    public void A_new_user_has_nothing_in_it()
    {
        Assert.Empty(_recent.All());
    }

    [Fact]
    public void Opening_something_records_it()
    {
        _recent.Record(Repository("tool"));

        var all = _recent.All();

        Assert.Single(all);
        Assert.Equal("someone/tool", all[0].FullName);
    }

    [Fact]
    public void The_most_recent_is_first()
    {
        _recent.Record(Repository("first"));
        _recent.Record(Repository("second"));

        Assert.Equal("second", _recent.All()[0].Name);
    }

    [Fact]
    public void Looking_at_something_twice_moves_it_rather_than_listing_it_twice()
    {
        _recent.Record(Repository("tool"));
        _recent.Record(Repository("other"));
        _recent.Record(Repository("tool"));

        var all = _recent.All();

        Assert.Equal(2, all.Count);
        Assert.Equal("tool", all[0].Name);
    }

    [Fact]
    public void It_is_bounded()
    {
        for (var i = 0; i < RecentlyViewed.MaxEntries + 10; i++)
        {
            _recent.Record(Repository($"tool{i}"));
        }

        Assert.Equal(RecentlyViewed.MaxEntries, _recent.All().Count);
    }

    [Fact]
    public void It_survives_a_restart()
    {
        _recent.Record(Repository("tool"));

        var reopened = new RecentlyViewed(_paths, NullAppLog.Instance);

        Assert.Single(reopened.All());
        Assert.Equal("someone/tool", reopened.All()[0].FullName);
    }

    [Fact]
    public void Clearing_forgets_everything_and_says_how_much()
    {
        _recent.Record(Repository("one"));
        _recent.Record(Repository("two"));

        Assert.Equal(2, _recent.Clear());
        Assert.Empty(_recent.All());
    }

    [Fact]
    public void Recording_and_clearing_both_announce_themselves()
    {
        var changes = 0;
        _recent.Changed += () => changes++;

        _recent.Record(Repository("tool"));
        _recent.Clear();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_repository_with_no_owner_is_not_recorded()
    {
        // Half a record is worse than none: it would draw a card that cannot be reopened.
        _recent.Record(new GitHubRepository
        {
            Id = 1,
            Name = "tool",
            FullName = "tool",
            HtmlUrl = "https://example.invalid",
            Owner = null
        });

        Assert.Empty(_recent.All());
    }

    [Fact]
    public void A_very_long_description_is_trimmed_before_it_is_stored()
    {
        var repository = TestRepositories.Create("tool", description: new string('x', 5000));

        _recent.Record(repository);

        Assert.True(_recent.All()[0].Description!.Length <= 200);
    }

    [Fact]
    public void It_stores_nothing_beyond_what_a_card_needs()
    {
        _recent.Record(Repository("tool"));

        // Owner, name, one-line description, timestamp. Not a profile.
        var entry = _recent.All()[0];

        Assert.Equal("someone", entry.Owner);
        Assert.Equal("tool", entry.Name);
        Assert.NotEqual(default, entry.ViewedAt);
    }

    [Fact]
    public void The_null_implementation_remembers_nothing()
    {
        var none = NullRecentlyViewed.Instance;

        none.Record(Repository("tool"));

        Assert.Empty(none.All());
        Assert.Equal(0, none.Clear());
    }
}

/// <summary>
/// Returning Discover to its home state.
/// </summary>
/// <remarks>
/// Found by driving the application: clicking Discover in the sidebar while already inside
/// Discover left the results on screen, because the selection had not changed and so the
/// handler never ran. A sidebar destination has to be a way out from anywhere, including
/// from inside itself.
/// </remarks>
public class DiscoverHomeTests
{
    private static DiscoverViewModel Discover(FakeGitHubClient github) =>
        new(github,
            new Services.Explanation.HeuristicRepositoryExplanationService(),
            new Services.Media.RepositoryMediaService(NullAppLog.Instance),
            NullAppLog.Instance);

    [Fact]
    public void A_fresh_page_is_home_not_results()
    {
        var vm = Discover(new FakeGitHubClient());

        Assert.True(vm.ShowHome);
        Assert.False(vm.ShowResults);
    }

    [Fact]
    public void Home_carries_no_result_controls()
    {
        // Filters, sort and view mode are for narrowing something that exists. Offering
        // them before there is anything to narrow asks a beginner to operate machinery
        // with nothing in it.
        var vm = Discover(new FakeGitHubClient());

        Assert.False(vm.ShowResultControls);
    }

    [Fact]
    public async Task Searching_leaves_home_and_shows_the_result_controls()
    {
        var vm = Discover(new FakeGitHubClient());
        vm.SearchText = "music player";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.False(vm.ShowHome);
        Assert.True(vm.ShowResultControls);
    }

    [Fact]
    public async Task Returning_home_clears_the_search_and_its_results()
    {
        var vm = Discover(new FakeGitHubClient());
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);

        vm.ReturnHome();

        Assert.True(vm.ShowHome);
        Assert.False(vm.ShowResultControls);
        Assert.Empty(vm.Results);
        Assert.Equal("", vm.SearchText);
    }

    [Fact]
    public async Task Returning_home_keeps_how_the_user_likes_to_browse()
    {
        // Filters and the Apps/All Projects mode are preferences about browsing, not part
        // of one particular search, so starting again must not reset them.
        var vm = Discover(new FakeGitHubClient());
        vm.UseAllProjectsModeCommand.Execute(null);
        vm.ExcludeArchived = false;

        vm.SearchText = "tool";
        await vm.SearchCommand.ExecuteAsync(null);

        vm.ReturnHome();

        Assert.True(vm.IsAllProjectsMode);
        Assert.False(vm.ExcludeArchived);
    }

    [Fact]
    public async Task Returning_home_forgets_where_the_results_were_scrolled_to()
    {
        var vm = Discover(new FakeGitHubClient());
        await vm.SearchCommand.ExecuteAsync(null);
        vm.ResultsScrollOffset = 900;

        vm.ReturnHome();

        Assert.Equal(0, vm.ResultsScrollOffset);
    }

    [Fact]
    public void An_empty_shelf_does_not_appear()
    {
        // No "Recently viewed" heading for somebody who has never viewed anything: an
        // empty shelf is a promise of content that is not there.
        var vm = Discover(new FakeGitHubClient());

        Assert.False(vm.HasRecentlyViewed);
        Assert.Empty(vm.RecentlyViewed);
    }

    [Fact]
    public void The_four_featured_collections_lead_and_popularity_is_not_one_of_them()
    {
        var featured = DiscoverCollection.FeaturedOnly;

        Assert.Equal(4, featured.Count);
        Assert.Contains(featured, c => c.Label == "Portable apps");
        Assert.Contains(featured, c => c.Label == "No installation needed");
        Assert.Contains(featured, c => c.Label == "Small & useful");
        Assert.Contains(featured, c => c.Label == "Weird & useful");

        // Promoting this would turn a star count owned by GitHub into a recommendation
        // owned by RepoDeck.
        Assert.DoesNotContain(featured, c => c.Label == "Popular on GitHub");
    }

    [Fact]
    public void Featured_and_the_rest_together_are_all_of_them()
    {
        Assert.Equal(
            DiscoverCollection.All.Count,
            DiscoverCollection.FeaturedOnly.Count + DiscoverCollection.SecondaryOnly.Count);
    }

    [Fact]
    public void Every_category_carries_a_mark_to_draw()
    {
        foreach (var category in DiscoverCategory.All)
        {
            Assert.NotEqual(Models.ProjectKind.Unknown, category.Kind);
        }
    }

    [Fact]
    public void The_category_marks_are_not_all_the_same_one()
    {
        // Five of the nine were Utility, which drew the same gear five times and made the
        // row read as unfinished.
        var distinct = DiscoverCategory.All.Select(c => c.Kind).Distinct().Count();

        Assert.True(distinct >= 6, $"only {distinct} distinct marks across {DiscoverCategory.All.Count} categories");
    }
}
