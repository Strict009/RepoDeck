using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.Media;
using RepoDeck.Services.Preferences;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// Apps and Everything. Apps prioritises what RepoDeck has evidence is a usable program;
/// Everything is raw GitHub discovery.
/// </summary>
/// <remarks>
/// The rule these tests exist to protect: a result RepoDeck merely could not classify is
/// never hidden. "I could not tell what this is" is not the same as "this is not for you",
/// and only the clearest cases are set aside.
/// </remarks>
public class AppsModeTests
{
    private static DiscoverViewModel Discover(
        FakeGitHubClient github, IUserPreferences? preferences = null) =>
        new(github,
            new HeuristicRepositoryExplanationService(),
            new RepositoryMediaService(NullAppLog.Instance),
            NullAppLog.Instance,
            images: null,
            preferences: preferences,
            quickLook: null,
            machine: MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64));

    private static GitHubRepository Program(string name, string description) =>
        TestRepositories.Create(name, description: description, topics: ["desktop", "app"]);

    private static GitHubRepository Library(string name) =>
        TestRepositories.Create(name, description: "A library for developers.",
            topics: ["library", "sdk"]);

    private static GitHubRepository Mystery(string name) =>
        TestRepositories.Create(name, description: "A thing.", topics: []);

    [Fact]
    public void Apps_is_the_default_mode()
    {
        var vm = Discover(new FakeGitHubClient());

        Assert.True(vm.IsAppsMode);
        Assert.False(vm.IsAllProjectsMode);
    }

    [Fact]
    public async Task Apps_sets_aside_a_library_it_is_sure_about()
    {
        var github = new FakeGitHubClient
        {
            DefaultResults = [Program("player", "A music player."), Library("libfoo")]
        };

        var vm = Discover(github);
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Single(vm.Results);
        Assert.Equal("player", vm.Results[0].Repository.Name);
        Assert.NotNull(vm.SetAsideNotice);
    }

    [Fact]
    public async Task Apps_never_hides_a_result_it_simply_could_not_classify()
    {
        // The whole point. Uncertainty is not a verdict.
        var github = new FakeGitHubClient
        {
            DefaultResults = [Program("player", "A music player."), Mystery("xyzzy")]
        };

        var vm = Discover(github);
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Results.Count);
        Assert.Contains(vm.Results, c => c.Repository.Name == "xyzzy");
    }

    [Fact]
    public async Task Everything_sets_nothing_aside()
    {
        var github = new FakeGitHubClient
        {
            DefaultResults = [Program("player", "A music player."), Library("libfoo"), Mystery("xyzzy")]
        };

        var vm = Discover(github);
        vm.UseAllProjectsModeCommand.Execute(null);
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Results.Count);
        Assert.Null(vm.SetAsideNotice);
    }

    [Fact]
    public async Task Everything_leaves_the_order_results_arrived_in_alone()
    {
        // GitHub knows things about text relevance RepoDeck cannot see. Everything means
        // everything, in the order it arrived.
        var github = new FakeGitHubClient
        {
            DefaultResults =
            [
                Mystery("first"), Program("second", "A music player."), Mystery("third")
            ]
        };

        var vm = Discover(github);
        vm.UseAllProjectsModeCommand.Execute(null);
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(["first", "second", "third"], vm.Results.Select(c => c.Repository.Name));
    }

    [Fact]
    public async Task Apps_puts_the_likelier_answer_first()
    {
        var github = new FakeGitHubClient
        {
            DefaultResults =
            [
                Mystery("something-else"),
                TestRepositories.Create("music-player",
                    description: "A music player for your desktop.",
                    topics: ["music-player", "desktop"])
            ]
        };

        var vm = Discover(github);
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("music-player", vm.Results[0].Repository.Name);
    }

    [Fact]
    public async Task The_notice_tells_the_user_how_to_get_set_aside_results_back()
    {
        var github = new FakeGitHubClient
        {
            DefaultResults = [Program("player", "A music player."), Library("libfoo")]
        };

        var vm = Discover(github);
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Contains("Everything", vm.SetAsideNotice!);
    }

    [Fact]
    public void The_mode_is_remembered()
    {
        var preferences = new FakePreferences();
        var vm = Discover(new FakeGitHubClient(), preferences);

        vm.UseAllProjectsModeCommand.Execute(null);

        Assert.Equal(BrowseMode.Everything, preferences.Current.BrowseMode);
    }

    [Fact]
    public void A_remembered_mode_is_used_on_the_next_run()
    {
        var preferences = new FakePreferences(
            new PreferencesSnapshot { BrowseMode = BrowseMode.Everything });

        Assert.True(Discover(new FakeGitHubClient(), preferences).IsAllProjectsMode);
    }

    [Fact]
    public void The_two_modes_are_never_both_active()
    {
        var vm = Discover(new FakeGitHubClient(), new FakePreferences());

        vm.UseAllProjectsModeCommand.Execute(null);
        Assert.NotEqual(vm.IsAppsMode, vm.IsAllProjectsMode);

        vm.UseAppsModeCommand.Execute(null);
        Assert.NotEqual(vm.IsAppsMode, vm.IsAllProjectsMode);
    }

    [Fact]
    public void Each_mode_explains_itself_without_claiming_to_judge_quality()
    {
        var vm = Discover(new FakeGitHubClient(), new FakePreferences());

        var apps = vm.BrowseModeExplanation;
        vm.UseAllProjectsModeCommand.Execute(null);
        var everything = vm.BrowseModeExplanation;

        Assert.NotEqual(apps, everything);
        Assert.Contains("not judging quality or safety", apps);

        foreach (var text in new[] { apps, everything })
        {
            Assert.DoesNotContain("trusted", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("verified", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Changing_mode_rebuilds_the_results_rather_than_waiting_for_a_new_search()
    {
        var github = new FakeGitHubClient
        {
            DefaultResults = [Program("player", "A music player."), Library("libfoo")]
        };

        var vm = Discover(github, new FakePreferences());
        vm.SearchText = "music player";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Single(vm.Results);

        vm.UseAllProjectsModeCommand.Execute(null);
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Results.Count);
    }

    [Fact]
    public async Task Ranking_thirty_results_costs_no_extra_requests()
    {
        // The whole design rests on this: every signal the scorer uses is already in the
        // search response. A search must never become N+1 requests.
        var many = Enumerable.Range(0, 30)
            .Select(i => Program("project-" + i, "A desktop program number " + i))
            .ToList();

        var github = new FakeGitHubClient { DefaultResults = many };

        var vm = Discover(github);
        vm.SearchText = "desktop program";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(30, vm.Results.Count);
        Assert.Equal(1, github.SearchCallCount);
        Assert.Equal(0, github.RepositoryCallCount);
        Assert.Equal(0, github.ReadmeCallCount);
        Assert.Equal(0, github.ReleaseCallCount);
        Assert.Equal(0, github.TreeCallCount);
    }
}
