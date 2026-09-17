using RepoDeck.Models;
using RepoDeck.Services.GitHub;

namespace RepoDeck.Tests;

/// <summary>
/// The details page end to end against a fake GitHub: analysis, recommendation and plan.
/// </summary>
public class RepositoryDetailsViewModelTests
{
    private static FakeGitHubClient AvaloniaAppClient()
    {
        var github = new FakeGitHubClient
        {
            Tree = TestTrees.Of("App.sln", "src/App/App.csproj", "README.md"),
            Releases = [TestRepositories.Release("v1.4.2", false,
                "Tool-win-x64.zip", "Tool-linux-x64.tar.gz", "Source code (zip)")],
            Readme = "# Tool\n\nTool is a desktop program that converts files."
        };

        github.TextFiles["src/App/App.csproj"] = TestManifests.AvaloniaDesktopProject;
        return github;
    }

    [Fact]
    public async Task Opening_a_desktop_application_produces_a_complete_analysis_and_plan()
    {
        var vm = DetailsViewModelFactory.Create(AvaloniaAppClient());

        await vm.LoadAsync(CancellationToken.None);

        Assert.True(vm.HasAnalysis);
        Assert.False(vm.AnalysisIsIncomplete);
        Assert.Equal("Desktop application", vm.ProjectTypeText);
        Assert.Contains("Avalonia", vm.TechnologyText);

        Assert.True(vm.HasRecommendation);
        Assert.Equal("Tool-win-x64.zip", vm.RecommendedAssetName);
        Assert.NotEmpty(vm.RecommendationReasons);

        Assert.True(vm.HasPlan);
        Assert.Equal("Portable archive", vm.PlanStrategyText);
        Assert.Empty(vm.PlanBlockers);
    }

    [Fact]
    public async Task The_install_button_stays_disabled_in_this_milestone()
    {
        var vm = DetailsViewModelFactory.Create(AvaloniaAppClient());

        await vm.LoadAsync(CancellationToken.None);

        Assert.False(vm.CanInstall);
        Assert.Contains("next milestone", vm.InstallButtonText);
    }

    [Fact]
    public async Task The_compatibility_headline_names_this_computer()
    {
        var vm = DetailsViewModelFactory.Create(AvaloniaAppClient());

        await vm.LoadAsync(CancellationToken.None);

        Assert.Contains("Windows x64", vm.CompatibilityHeadline);
        Assert.True(vm.CompatibilityIsFavourable);
        Assert.NotEmpty(vm.CompatibilityEvidence);
    }

    [Fact]
    public async Task A_source_only_project_is_refused_with_a_reason()
    {
        var github = new FakeGitHubClient
        {
            Tree = TestTrees.Of("CMakeLists.txt", "src/main.cpp"),
            Releases = [TestRepositories.Release("v1.0", false, "Source code (zip)")]
        };

        var vm = DetailsViewModelFactory.Create(github);
        await vm.LoadAsync(CancellationToken.None);

        Assert.False(vm.HasPlan);
        Assert.False(vm.HasRecommendation);
        Assert.NotEmpty(vm.PlanBlockers);
        Assert.Contains("compiled", vm.NoRecommendationReason);
    }

    [Fact]
    public async Task A_project_built_only_for_other_platforms_is_reported_as_such()
    {
        var github = new FakeGitHubClient
        {
            Tree = TestTrees.Of("Cargo.toml"),
            Releases = [TestRepositories.Release("v1.0", false, "tool-linux-x64.tar.gz")]
        };

        var vm = DetailsViewModelFactory.Create(github);
        await vm.LoadAsync(CancellationToken.None);

        Assert.False(vm.HasRecommendation);
        Assert.False(vm.HasPlan);
        Assert.Contains("Windows x64", vm.CompatibilityHeadline);
    }

    [Fact]
    public async Task A_rate_limited_analysis_is_shown_as_incomplete()
    {
        var github = new FakeGitHubClient
        {
            Releases = [TestRepositories.Release("v1.0", false, "Tool-win-x64.zip")],
            TreeThrows = new GitHubApiException(
                GitHubErrorKind.RateLimited, "You have used up GitHub's request allowance.")
        };

        var vm = DetailsViewModelFactory.Create(github);
        await vm.LoadAsync(CancellationToken.None);

        Assert.True(vm.AnalysisIsIncomplete);
        Assert.NotNull(vm.AnalysisIncompleteReason);

        // An incomplete analysis must never produce an actionable plan.
        Assert.False(vm.HasPlan);
        Assert.NotEmpty(vm.PlanBlockers);
    }

    [Fact]
    public async Task A_Linux_machine_gets_the_Linux_build()
    {
        var github = new FakeGitHubClient
        {
            Tree = TestTrees.Of("App.csproj"),
            Releases = [TestRepositories.Release("v1.0", false,
                "Tool-win-x64.zip", "Tool-x86_64.AppImage")]
        };

        var vm = DetailsViewModelFactory.Create(
            github, machine: MachineProfile.For(OsPlatform.Linux, CpuArchitecture.X64));

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal("Tool-x86_64.AppImage", vm.RecommendedAssetName);
        Assert.Equal("Linux AppImage", vm.PlanStrategyText);
        Assert.Contains("Linux x64", vm.CompatibilityHeadline);
    }

    [Fact]
    public async Task The_analysis_status_is_cleared_when_it_finishes()
    {
        var vm = DetailsViewModelFactory.Create(AvaloniaAppClient());

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal("", vm.AnalysisStatus);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task The_whole_details_page_costs_a_predictable_number_of_requests()
    {
        // Repository, README, languages, releases, file listing, and one manifest.
        var github = AvaloniaAppClient();
        var vm = DetailsViewModelFactory.Create(github);

        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(1, github.TreeCallCount);
        Assert.Equal(1, github.RepositoryCallCount);
        Assert.Equal(1, github.ReleaseCallCount);
        Assert.True(github.TextFileCallCount <= 3);
    }
}
