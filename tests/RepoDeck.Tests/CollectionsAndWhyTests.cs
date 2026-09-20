using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.Media;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// The landing page's categories and collections. All of them are searches, and none of
/// them is a recommendation.
/// </summary>
public class DiscoverCollectionTests
{
    private static DiscoverViewModel Discover(FakeGitHubClient github) =>
        new(github,
            new HeuristicRepositoryExplanationService(),
            new RepositoryMediaService(NullAppLog.Instance),
            NullAppLog.Instance);

    [Fact]
    public void There_are_nine_categories_covering_what_people_look_for()
    {
        Assert.Equal(9, DiscoverCategory.All.Count);

        string[] expected =
        [
            "Utilities", "Media", "Games & Emulation", "Graphics", "File Tools",
            "Networking", "Privacy", "Productivity", "Developer Tools"
        ];

        Assert.Equal(expected, DiscoverCategory.All.Select(c => c.Label));
    }

    [Fact]
    public void Every_collection_has_a_label_a_query_and_an_honest_description()
    {
        Assert.NotEmpty(DiscoverCollection.All);

        foreach (var collection in DiscoverCollection.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(collection.Label));
            Assert.False(string.IsNullOrWhiteSpace(collection.Query));
            Assert.False(string.IsNullOrWhiteSpace(collection.Description));
        }
    }

    [Fact]
    public void Weird_and_useful_exists_because_it_had_to()
    {
        Assert.Contains(DiscoverCollection.All, c => c.Label == "Weird & useful");
    }

    [Fact]
    public void Popularity_is_described_as_GitHubs_number_rather_than_as_an_endorsement()
    {
        var popular = DiscoverCollection.All.Single(c => c.Label == "Popular on GitHub");

        Assert.Contains("not a recommendation", popular.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_collection_claims_to_vouch_for_anything()
    {
        string[] forbidden = ["best", "top", "trusted", "verified", "safe", "recommended by"];

        foreach (var collection in DiscoverCollection.All)
        {
            var text = (collection.Label + " " + collection.Description).ToLowerInvariant();

            foreach (var word in forbidden)
            {
                Assert.DoesNotContain(word, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task Choosing_a_collection_runs_an_ordinary_search()
    {
        var github = new FakeGitHubClient { DefaultResults = [TestRepositories.Create()] };
        var vm = Discover(github);

        var weird = DiscoverCollection.All.Single(c => c.Label == "Weird & useful");
        await vm.SearchCollectionCommand.ExecuteAsync(weird);

        Assert.Equal(1, github.SearchCallCount);
        Assert.Equal(weird.Query, vm.SearchText);
        Assert.Equal("Weird & useful", vm.ActiveCategory);
    }

    [Fact]
    public async Task A_collection_that_is_about_ordering_changes_the_sort()
    {
        var github = new FakeGitHubClient { DefaultResults = [TestRepositories.Create()] };
        var vm = Discover(github);

        var recent = DiscoverCollection.All.Single(c => c.Label == "Recently updated");
        await vm.SearchCollectionCommand.ExecuteAsync(recent);

        Assert.Equal(RepositorySort.RecentlyUpdated, vm.SelectedSort.Value);
        Assert.Equal(UpdatedWithin.PastMonth, vm.SelectedUpdated.Value);
    }

    [Fact]
    public async Task A_collection_with_a_star_floor_applies_one()
    {
        var github = new FakeGitHubClient { DefaultResults = [TestRepositories.Create()] };
        var vm = Discover(github);

        var popular = DiscoverCollection.All.Single(c => c.Label == "Popular on GitHub");
        await vm.SearchCollectionCommand.ExecuteAsync(popular);

        Assert.Equal(RepositorySort.Stars, vm.SelectedSort.Value);
        Assert.NotNull(vm.SelectedStars.MinStars);
    }

    [Fact]
    public async Task A_null_collection_does_nothing_rather_than_throwing()
    {
        var github = new FakeGitHubClient();
        var vm = Discover(github);

        await vm.SearchCollectionCommand.ExecuteAsync(null);

        Assert.Equal(0, github.SearchCallCount);
    }
}

/// <summary>
/// The WHY buttons. Every verdict RepoDeck makes can be opened up and read.
/// </summary>
public class WhyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-why-" + Guid.NewGuid().ToString("N"));

    private readonly FakeGitHubClient _github = new();
    private readonly AppPaths _paths;
    private readonly Services.Install.InstalledAppStore _store;

    public WhyTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new Services.Install.InstalledAppStore(_paths, NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test over.
        }
    }

    private QuickLookViewModel Panel() =>
        new(_github,
            new HeuristicRepositoryExplanationService(),
            new Services.Analysis.RepositoryAnalyzerService(_github, NullAppLog.Instance),
            new Services.Install.InstallPlanner(_paths),
            new RepositoryMediaService(NullAppLog.Instance),
            _store,
            new Services.Install.LaunchService(_paths, _store, NullAppLog.Instance),
            MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64),
            NullAppLog.Instance);

    private static RepositoryCardViewModel Card()
    {
        var repository = TestRepositories.Create("tool", description: "A desktop tool.");
        var explanations = new HeuristicRepositoryExplanationService();
        var likelihood = Services.Analysis.ApplicationLikelihoodEvaluator.Evaluate(repository);

        return new RepositoryCardViewModel(
            repository,
            explanations.ExplainFromMetadata(repository),
            likelihood,
            Services.Analysis.SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood),
            imageUrl: null,
            openDetails: _ => { },
            log: NullAppLog.Instance);
    }

    [Fact]
    public async Task Asking_why_opens_the_reasoning()
    {
        var panel = Panel();
        await panel.ShowAsync(Card());

        Assert.False(panel.ReasoningExpanded);

        panel.WhyCommand.Execute(QuickLookViewModel.CompatibilityQuestion);

        Assert.True(panel.ReasoningExpanded);
    }

    [Fact]
    public async Task Asking_why_highlights_only_the_group_that_answers_that_question()
    {
        // Pressing WHY beside "Works on this PC" must not dump six groups on somebody who
        // wanted one answer.
        var panel = Panel();
        await panel.ShowAsync(Card());

        panel.WhyCommand.Execute(QuickLookViewModel.CompatibilityQuestion);

        Assert.Equal(QuickLookViewModel.CompatibilityQuestion, panel.HighlightedQuestion);
        Assert.True(panel.HasHighlightedQuestion);

        var highlighted = panel.Reasoning.Where(g => g.IsHighlighted).ToList();
        Assert.True(highlighted.Count <= 1);
    }

    [Fact]
    public async Task Asking_why_about_a_different_verdict_moves_the_highlight()
    {
        var panel = Panel();
        await panel.ShowAsync(Card());

        panel.WhyCommand.Execute(QuickLookViewModel.CompatibilityQuestion);
        panel.WhyCommand.Execute(QuickLookViewModel.InstallabilityQuestion);

        Assert.Equal(QuickLookViewModel.InstallabilityQuestion, panel.HighlightedQuestion);
        Assert.DoesNotContain(panel.Reasoning,
            g => g.IsHighlighted && g.Question == QuickLookViewModel.CompatibilityQuestion);
    }

    [Fact]
    public async Task Closing_the_reasoning_clears_the_highlight()
    {
        var panel = Panel();
        await panel.ShowAsync(Card());

        panel.WhyCommand.Execute(QuickLookViewModel.SetupQuestion);
        panel.ToggleReasoningCommand.Execute(null);

        Assert.False(panel.ReasoningExpanded);
        Assert.False(panel.HasHighlightedQuestion);
        Assert.DoesNotContain(panel.Reasoning, g => g.IsHighlighted);
    }

    [Fact]
    public void The_questions_the_buttons_point_at_are_defined_in_one_place()
    {
        // The buttons and the groups have to agree, so both come from here.
        Assert.Equal("Will it work on this PC?", QuickLookViewModel.CompatibilityQuestion);
        Assert.Equal("How much setup?", QuickLookViewModel.SetupQuestion);
        Assert.Equal("Can RepoDeck install it?", QuickLookViewModel.InstallabilityQuestion);
        Assert.Equal("What kind of project is this?", QuickLookViewModel.KindQuestion);
    }

    [Fact]
    public async Task Every_question_a_WHY_button_asks_has_a_group_that_answers_it()
    {
        // A WHY button that opens an empty panel would be worse than no button.
        _github.Releases = [];

        var panel = Panel();
        await panel.ShowAsync(Card());

        string[] asked =
        [
            QuickLookViewModel.KindQuestion,
            QuickLookViewModel.CompatibilityQuestion,
            QuickLookViewModel.InstallabilityQuestion
        ];

        foreach (var question in asked)
        {
            Assert.Contains(panel.Reasoning, g => g.Question == question);
        }
    }

    [Fact]
    public void The_view_puts_a_why_button_beside_every_verdict()
    {
        var view = File.ReadAllText(Path.Combine(SourceViews(), "QuickLookView.axaml"));

        // What matters is that each verdict has its own disclosure pointing at its own
        // question, not what the control is called or how it is styled. The parameter is the
        // attribution: one shared control could not say which conclusion it explains.
        var attributed = System.Text.RegularExpressions.Regex
            .Matches(view, "CommandParameter=\"[^\"]+\"").Count;

        // Works on this PC, setup, and what RepoDeck can do.
        Assert.True(attributed >= 3, $"only {attributed} attributed evidence controls in the panel");
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
