using RepoDeck.Controls;
using RepoDeck.Services.Analysis;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.Media;
using RepoDeck.Services.Preferences;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// Discover as an application browser rather than a repository browser: the layout the
/// results get, what a card leads with, and what it offers to do.
/// </summary>
public class DiscoverUxTests
{
    private static DiscoverViewModel Discover(
        FakeGitHubClient github, IUserPreferences? preferences = null) =>
        new(github,
            new HeuristicRepositoryExplanationService(),
            new RepositoryMediaService(NullAppLog.Instance),
            NullAppLog.Instance,
            images: null,
            preferences: preferences);

    // ---- The grid ---------------------------------------------------------

    [Theory]
    [InlineData(1600, 3)]
    [InlineData(1400, 3)]
    [InlineData(1280, 3)]
    [InlineData(1000, 3)]
    [InlineData(950, 3)]
    [InlineData(900, 2)]
    [InlineData(700, 2)]
    [InlineData(600, 1)]
    [InlineData(380, 1)]
    public void The_grid_is_three_columns_wide_then_two_then_one(double width, int expected)
    {
        Assert.Equal(expected, ResponsiveCardsPanel.ColumnsFor(width, minItemWidth: 300, spacing: 16, maxColumns: 3));
    }

    [Fact]
    public void A_very_wide_window_still_gets_three_columns_rather_than_four()
    {
        // Four narrow cards fit, and four narrow cards were the problem: there is room
        // for a thumbnail and half a sentence in each. Three wide ones say more.
        Assert.Equal(3, ResponsiveCardsPanel.ColumnsFor(2560, 300, 16, 3));
        Assert.Equal(3, ResponsiveCardsPanel.ColumnsFor(4000, 300, 16, 3));
    }

    [Fact]
    public void A_window_too_narrow_for_even_one_card_still_shows_one()
    {
        // Cramped beats blank.
        Assert.Equal(1, ResponsiveCardsPanel.ColumnsFor(120, 300, 16, 3));
        Assert.Equal(1, ResponsiveCardsPanel.ColumnsFor(1, 300, 16, 3));
    }

    [Fact]
    public void An_unmeasured_panel_assumes_its_full_column_count()
    {
        // Measure can be called with infinity before a real width is known.
        Assert.Equal(3, ResponsiveCardsPanel.ColumnsFor(double.PositiveInfinity, 300, 16, 3));
        Assert.Equal(3, ResponsiveCardsPanel.ColumnsFor(double.NaN, 300, 16, 3));
        Assert.Equal(3, ResponsiveCardsPanel.ColumnsFor(0, 300, 16, 3));
    }

    [Fact]
    public void The_column_count_accounts_for_the_gaps_between_cards()
    {
        // Three 300px cards need 900px plus two 16px gaps: 932, not 900.
        Assert.Equal(2, ResponsiveCardsPanel.ColumnsFor(931, 300, 16, 3));
        Assert.Equal(3, ResponsiveCardsPanel.ColumnsFor(932, 300, 16, 3));
    }

    // ---- What a card leads with ------------------------------------------

    [Fact]
    public async Task A_card_prefers_the_projects_own_artwork_over_a_generated_card()
    {
        // A search result yields only GitHub's generated preview, which is the repository
        // name and description set in small type. At card size that is unreadable text
        // pretending to be a screenshot, so the card uses its designed tile instead.
        var github = new FakeGitHubClient { DefaultResults = [TestRepositories.Create("timber")] };
        var vm = Discover(github);

        vm.SearchText = "music";
        await vm.SearchCommand.ExecuteAsync(null);

        var card = Assert.Single(vm.Results);

        Assert.Null(card.ImageUrl);
        Assert.True(card.ShowFallback);
        Assert.Equal("T", card.FallbackInitial);
    }

    [Fact]
    public async Task A_card_offers_details_until_a_plan_says_otherwise()
    {
        var github = new FakeGitHubClient { DefaultResults = [TestRepositories.Create("tool")] };
        var vm = Discover(github);

        vm.SearchText = "tool";
        await vm.SearchCommand.ExecuteAsync(null);

        var card = Assert.Single(vm.Results);

        Assert.Equal("DETAILS", card.PrimaryActionLabel);
        Assert.False(card.Installability.AllowsDirectInstall);
    }

    [Fact]
    public async Task A_card_offers_install_only_once_a_workable_plan_exists()
    {
        var github = new FakeGitHubClient { DefaultResults = [TestRepositories.Create("tool")] };
        var vm = Discover(github);

        vm.SearchText = "tool";
        await vm.SearchCommand.ExecuteAsync(null);

        var card = Assert.Single(vm.Results);

        card.ApplyAnalysedInstallability(new Installability
        {
            State = InstallabilityState.ReadyToInstall,
            Confidence = Confidence.Likely,
            Reasons = ["RepoDeck found tool-win-x64.zip in release v2.1."]
        });

        Assert.Equal("INSTALL", card.PrimaryActionLabel);
    }

    [Fact]
    public async Task Every_card_can_explain_its_verdict_and_says_it_is_not_about_safety()
    {
        var github = new FakeGitHubClient
        {
            DefaultResults =
            [
                TestRepositories.Create("tool"),
                TestRepositories.Create("json", "nlohmann", topics: ["library", "header-only"]),
                TestRepositories.Create("dead", archived: true)
            ]
        };

        var vm = Discover(github);

        // Everything mode, so nothing is set aside and every shape of card is examined.
        vm.UseAllProjectsModeCommand.Execute(null);
        vm.SearchText = "anything";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Results.Count);

        foreach (var card in vm.Results)
        {
            Assert.False(string.IsNullOrWhiteSpace(card.InstallabilityLabel));
            Assert.Contains("not whether the software is safe", card.InstallabilityExplanation);
        }
    }

    [Fact]
    public async Task Owner_and_language_share_one_line_of_small_print()
    {
        var github = new FakeGitHubClient
        {
            DefaultResults = [TestRepositories.Create("timber", "naman14", language: "Java")]
        };

        var vm = Discover(github);
        vm.SearchText = "music";
        await vm.SearchCommand.ExecuteAsync(null);

        var card = Assert.Single(vm.Results);

        Assert.Contains("naman14", card.MetadataLine);
        Assert.Contains("Java", card.MetadataLine);

        // The headline is the program's name, not the slug.
        Assert.Equal("Timber", card.FriendlyName);
    }

    // ---- Terminology ------------------------------------------------------

    [Fact]
    public async Task Results_are_counted_as_projects_rather_than_repositories()
    {
        var github = new FakeGitHubClient
        {
            DefaultResults = [TestRepositories.Create("a"), TestRepositories.Create("b")],
            TotalCount = 132_000
        };

        var vm = Discover(github);
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Contains("projects found", vm.ResultSummary);
        Assert.DoesNotContain("repositories", vm.ResultSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nothing_found_is_phrased_without_GitHub_vocabulary()
    {
        var github = new FakeGitHubClient { DefaultResults = [] };
        var vm = Discover(github);

        vm.SearchText = "nothing at all";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.DoesNotContain("repositories", vm.ResultSummary, StringComparison.OrdinalIgnoreCase);
    }

    // ---- View mode --------------------------------------------------------

    [Fact]
    public void Card_view_is_the_default()
    {
        var vm = Discover(new FakeGitHubClient());

        Assert.True(vm.IsCardView);
        Assert.False(vm.IsCompactView);
    }

    [Fact]
    public void Choosing_compact_is_remembered()
    {
        var preferences = new FakePreferences();
        var vm = Discover(new FakeGitHubClient(), preferences);

        vm.UseCompactViewCommand.Execute(null);

        Assert.True(vm.IsCompactView);
        Assert.Equal(ResultsViewMode.Compact, preferences.Current.ResultsView);
    }

    [Fact]
    public void A_remembered_preference_is_used_on_the_next_run()
    {
        var preferences = new FakePreferences(
            new PreferencesSnapshot { ResultsView = ResultsViewMode.Compact });

        var vm = Discover(new FakeGitHubClient(), preferences);

        Assert.True(vm.IsCompactView);
    }

    [Fact]
    public void The_two_views_are_never_both_showing()
    {
        var vm = Discover(new FakeGitHubClient(), new FakePreferences());

        vm.UseCompactViewCommand.Execute(null);
        Assert.NotEqual(vm.IsCardView, vm.IsCompactView);

        vm.UseCardViewCommand.Execute(null);
        Assert.NotEqual(vm.IsCardView, vm.IsCompactView);
    }

    [Fact]
    public void Discover_works_without_a_preferences_store_at_all()
    {
        // The store is optional, and losing a view preference is never worth a crash.
        var vm = Discover(new FakeGitHubClient());

        vm.UseCompactViewCommand.Execute(null);

        Assert.True(vm.IsCompactView);
    }

    [Fact]
    public async Task Enrichment_of_the_selected_card_preserves_selection_scroll_and_visual_place()
    {
        var selectedRepository = TestRepositories.Create(
            "music-player", description: "A desktop music player.",
            topics: ["desktop", "music-player"]);
        var otherRepository = TestRepositories.Create(
            "other-player", description: "Another desktop music player.",
            topics: ["desktop", "music-player"]);
        var github = new FakeGitHubClient
        {
            DefaultResults = [selectedRepository, otherRepository]
        };
        var vm = Discover(github);
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);

        var selected = vm.Results.Single(card => card.Repository.Name == "music-player");
        var next = vm.Results.Single(card => card.Repository.Name == "other-player");
        vm.SelectedResult = selected;
        vm.ResultsScrollOffset = 640;
        var groupEvents = 0;
        selected.SearchResultGroupChanged += _ => groupEvents++;

        selected.ApplyAnalysis(
            new Installability
            {
                State = InstallabilityState.DeveloperFocused,
                Confidence = Confidence.Confirmed,
                Reasons = ["The inspected project is a library."]
            },
            new ProjectClassification
            {
                Kind = ProjectKind.Library,
                Confidence = Confidence.Confirmed,
                Reasons = ["RepoDeck found a library manifest."]
            });

        Assert.Equal(SearchResultGroup.OtherResult, selected.SearchMatch.Group);
        Assert.Equal(1, groupEvents);
        Assert.Same(selected, vm.SelectedResult);
        Assert.Equal(640, vm.ResultsScrollOffset);
        Assert.Contains(selected, vm.BestMatches);
        Assert.DoesNotContain(selected, vm.OtherResults);

        vm.SelectedResult = next;

        Assert.Contains(selected, vm.OtherResults);
        Assert.DoesNotContain(selected, vm.BestMatches);
        Assert.Same(next, vm.SelectedResult);
    }

    [Fact]
    public async Task Loading_more_preserves_existing_results_and_ranks_only_the_new_page()
    {
        var first = TestRepositories.Create(
            "music-player", description: "A desktop music player.",
            topics: ["desktop", "music-player"]);
        var second = TestRepositories.Create("unrelated", description: "A source project.");
        var github = new FakeGitHubClient { DefaultResults = [second, first], TotalCount = 32 };
        var vm = Discover(github);
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);
        var firstPage = vm.Results.ToList();

        var third = TestRepositories.Create("another", description: "Another source project.");
        var fourth = TestRepositories.Create(
            "music-player-two", description: "A desktop music player.",
            topics: ["desktop", "music-player"]);
        github.DefaultResults = [third, fourth];
        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(firstPage, vm.Results.Take(2));
        Assert.Same(fourth, vm.Results[2].Repository);
        Assert.Same(third, vm.Results[3].Repository);
    }
}

/// <summary>Preferences held in memory, so a test never touches the user's own file.</summary>
internal sealed class FakePreferences : IUserPreferences
{
    public FakePreferences(PreferencesSnapshot? initial = null)
    {
        Current = initial ?? new PreferencesSnapshot();
    }

    public PreferencesSnapshot Current { get; private set; }

    public int WriteCount { get; private set; }

    public void Update(Func<PreferencesSnapshot, PreferencesSnapshot> change)
    {
        var updated = change(Current);
        if (updated == Current) return;

        Current = updated;
        WriteCount++;
    }
}

/// <summary>
/// The card's action, and the boundary it must not cross.
/// </summary>
public class CardActionTests
{
    private static RepositoryCardViewModel Card(
        Action<GitHubRepository>? openDetails = null,
        Action<RepositoryCardViewModel>? quickLook = null,
        Action<RepositoryCardViewModel>? requestInstall = null)
    {
        var repository = TestRepositories.Create("tool", "someone");
        var explanations = new HeuristicRepositoryExplanationService();

        return new RepositoryCardViewModel(
            repository,
            explanations.ExplainFromMetadata(repository),
            ApplicationLikelihoodEvaluator.Evaluate(repository),
            SetupAssessment.Unknown,
            imageUrl: null,
            openDetails: openDetails ?? (_ => { }),
            log: NullAppLog.Instance,
            quickLook: quickLook,
            requestInstall: requestInstall);
    }

    private static readonly Installability Ready = new()
    {
        State = InstallabilityState.ReadyToInstall,
        Confidence = Confidence.Likely,
        Reasons = ["RepoDeck found tool-win-x64.zip in release v2.1."]
    };

    [Fact]
    public void Activating_an_unanalysed_card_opens_the_side_panel()
    {
        RepositoryCardViewModel? looked = null;
        var card = Card(quickLook: c => looked = c);

        card.ActivateCommand.Execute(null);

        Assert.Same(card, looked);
    }

    [Fact]
    public void Activating_a_ready_card_asks_to_install_rather_than_opening_the_panel()
    {
        var installs = 0;
        var looks = 0;

        var card = Card(quickLook: _ => looks++, requestInstall: _ => installs++);
        card.ApplyAnalysedInstallability(Ready);

        card.ActivateCommand.Execute(null);

        Assert.Equal(1, installs);
        Assert.Equal(0, looks);
    }

    [Fact]
    public void A_card_with_nowhere_to_send_an_install_request_falls_back_to_looking()
    {
        // The callback is optional, and a card that cannot ask must not silently do nothing.
        var looks = 0;
        var card = Card(quickLook: _ => looks++);
        card.ApplyAnalysedInstallability(Ready);

        card.ActivateCommand.Execute(null);

        Assert.Equal(1, looks);
    }

    [Fact]
    public void A_card_with_no_panel_at_all_opens_the_full_page()
    {
        GitHubRepository? opened = null;
        var card = Card(openDetails: r => opened = r);

        card.ActivateCommand.Execute(null);

        Assert.NotNull(opened);
        Assert.Equal("someone/tool", opened!.FullName);
    }

    [Theory]
    [InlineData(InstallabilityState.Unknown)]
    [InlineData(InstallabilityState.NeedsSetup)]
    [InlineData(InstallabilityState.DeveloperFocused)]
    [InlineData(InstallabilityState.NotCompatible)]
    public void Only_a_ready_card_ever_takes_the_install_route(InstallabilityState state)
    {
        var installs = 0;
        var card = Card(quickLook: _ => { }, requestInstall: _ => installs++);

        card.ApplyAnalysedInstallability(new Installability { State = state });
        card.ActivateCommand.Execute(null);

        Assert.Equal(0, installs);
        Assert.Equal("DETAILS", card.PrimaryActionLabel);
    }

    [Fact]
    public void The_label_and_the_action_can_never_disagree()
    {
        // A button reading INSTALL that opens a panel, or reading DETAILS that installs,
        // would be worse than either.
        foreach (var state in Enum.GetValues<InstallabilityState>())
        {
            var installs = 0;
            var card = Card(quickLook: _ => { }, requestInstall: _ => installs++);

            card.ApplyAnalysedInstallability(new Installability { State = state });
            card.ActivateCommand.Execute(null);

            var saysInstall = card.PrimaryActionLabel == "INSTALL";

            Assert.Equal(saysInstall, installs == 1);
        }
    }

    [Fact]
    public void The_source_link_goes_to_the_projects_own_page()
    {
        var card = Card();

        Assert.Equal("https://github.com/someone/tool", card.Repository.HtmlUrl);
    }
}

/// <summary>
/// Whether the side panel sits beside the results or over them.
/// </summary>
public class PanelDockingTests
{
    [Theory]
    [InlineData(1400)]
    [InlineData(1000)]
    [InlineData(880)]
    public void A_window_with_room_for_both_docks_the_panel(double width)
    {
        Assert.True(PanelDisplayConverter.ShouldDock(width));
        Assert.Equal(1, PanelDisplayConverter.PanelColumn(width));
        Assert.Equal(1, PanelDisplayConverter.PanelColumnSpan(width));
    }

    [Theory]
    [InlineData(879)]
    [InlineData(700)]
    [InlineData(420)]
    public void A_window_without_room_overlays_rather_than_crushing_the_results(double width)
    {
        // Docking on a narrow window left the results about 270px wide, which is not a
        // result. Covering them keeps the grid intact underneath.
        Assert.False(PanelDisplayConverter.ShouldDock(width));
        Assert.Equal(0, PanelDisplayConverter.PanelColumn(width));
        Assert.Equal(2, PanelDisplayConverter.PanelColumnSpan(width));
    }

    [Fact]
    public void A_docked_panel_always_leaves_room_for_a_whole_card()
    {
        // The threshold is only defensible if this holds.
        const double panel = 380;
        const double card = 300;

        Assert.True(PanelDisplayConverter.MinimumDockedContentWidth - panel >= card);
    }

    [Fact]
    public void An_unmeasured_panel_docks_rather_than_covering_everything()
    {
        Assert.True(PanelDisplayConverter.ShouldDock(double.NaN));
        Assert.True(PanelDisplayConverter.ShouldDock(double.PositiveInfinity));
    }
}
