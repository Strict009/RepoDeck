using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.GitHub;

namespace RepoDeck.Tests;

public class RepositoryAnalyzerServiceTests
{
    private static readonly MachineProfile WindowsX64 =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static RepositoryAnalyzerService Analyzer(FakeGitHubClient github) =>
        new(github, NullAppLog.Instance);

    private static ReleaseAnalysis NoReleases() => ReleaseAnalyzer.Analyze([], WindowsX64);

    [Fact]
    public async Task The_whole_file_listing_costs_exactly_one_request()
    {
        var github = new FakeGitHubClient
        {
            Tree = TestTrees.Of("App.sln", "src/App/App.csproj", "README.md")
        };
        github.TextFiles["src/App/App.csproj"] = TestManifests.AvaloniaDesktopProject;

        await Analyzer(github).AnalyzeAsync(TestRepositories.Create(), NoReleases());

        Assert.Equal(1, github.TreeCallCount);
    }

    [Fact]
    public async Task At_most_three_manifest_files_are_ever_read()
    {
        // Each read is another request against a tight rate limit.
        var github = new FakeGitHubClient
        {
            Tree = TestTrees.Of("App.csproj", "package.json", "Cargo.toml", "pyproject.toml", "go.mod")
        };

        await Analyzer(github).AnalyzeAsync(TestRepositories.Create(), NoReleases());

        Assert.True(github.TextFileCallCount <= 3, $"Read {github.TextFileCallCount} files.");
    }

    [Fact]
    public async Task Manifest_contents_reach_the_classification()
    {
        var github = new FakeGitHubClient { Tree = TestTrees.Of("src/App/App.csproj") };
        github.TextFiles["src/App/App.csproj"] = TestManifests.AvaloniaDesktopProject;

        var analysis = await Analyzer(github).AnalyzeAsync(TestRepositories.Create(), NoReleases());

        Assert.Contains("Avalonia", analysis.Frameworks);
        Assert.Equal(ApplicationType.DesktopApplication, analysis.ApplicationType);
        Assert.Contains(analysis.Evidence, e => e.Text.Contains("Avalonia"));
    }

    [Fact]
    public async Task A_rate_limit_produces_an_incomplete_analysis_not_a_confident_one()
    {
        var github = new FakeGitHubClient
        {
            TreeThrows = new GitHubApiException(
                GitHubErrorKind.RateLimited, "You have used up GitHub's request allowance.")
        };

        var analysis = await Analyzer(github).AnalyzeAsync(TestRepositories.Create(), NoReleases());

        Assert.False(analysis.IsComplete);
        Assert.NotNull(analysis.IncompleteReason);
        Assert.Equal(Confidence.Unknown, analysis.Confidence);
        Assert.Equal(ApplicationType.Unknown, analysis.ApplicationType);
        Assert.NotEmpty(analysis.Unknowns);
    }

    [Fact]
    public async Task A_failure_reading_the_listing_degrades_rather_than_throwing()
    {
        var github = new FakeGitHubClient
        {
            TreeThrows = new GitHubApiException(GitHubErrorKind.ServerError, "GitHub is having problems.")
        };

        var analysis = await Analyzer(github).AnalyzeAsync(TestRepositories.Create(), NoReleases());

        // Still a usable result, but honest about what it could not see.
        Assert.True(analysis.IsComplete);
        Assert.Contains(analysis.Warnings, w => w.Contains("could not read"));
    }

    [Fact]
    public async Task A_truncated_listing_is_surfaced_as_a_warning()
    {
        var github = new FakeGitHubClient { Tree = TestTrees.Truncated("README.md") };

        var analysis = await Analyzer(github).AnalyzeAsync(TestRepositories.Create(), NoReleases());

        Assert.Contains(analysis.Warnings, w => w.Contains("only part of its file listing"));
    }

    [Fact]
    public async Task Progress_is_reported_through_the_stages()
    {
        var github = new FakeGitHubClient { Tree = TestTrees.Of("App.csproj") };
        github.TextFiles["App.csproj"] = TestManifests.ConsoleProject;

        // A synchronous recorder, so the test asserts what RepoDeck reports rather than
        // how Progress<T> happens to marshal callbacks.
        var progress = new RecordingProgress();

        await Analyzer(github).AnalyzeAsync(TestRepositories.Create(), NoReleases(), progress);

        Assert.Contains(AnalysisStage.ReadingProjectStructure, progress.Stages);
        Assert.Contains(AnalysisStage.ReadingProjectFiles, progress.Stages);
        Assert.Contains(AnalysisStage.AnalyzingCompatibility, progress.Stages);
        Assert.Contains(AnalysisStage.PreparingInstallationPlan, progress.Stages);
    }

    private sealed class RecordingProgress : IProgress<AnalysisStage>
    {
        public List<AnalysisStage> Stages { get; } = [];
        public void Report(AnalysisStage value) => Stages.Add(value);
    }

    [Fact]
    public async Task Cancellation_stops_the_analysis()
    {
        var github = new FakeGitHubClient { Tree = TestTrees.Of("App.csproj") };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Analyzer(github).AnalyzeAsync(TestRepositories.Create(), NoReleases(), null, cts.Token));
    }

    [Fact]
    public async Task Release_evidence_reaches_the_platform_conclusions()
    {
        var github = new FakeGitHubClient { Tree = TestTrees.Of("App.csproj") };
        var releases = ReleaseAnalyzer.Analyze(
            [TestRepositories.Release("v1.0", false, "App-win-x64.zip", "App-linux-x64.tar.gz")],
            WindowsX64);

        var analysis = await Analyzer(github).AnalyzeAsync(TestRepositories.Create(), releases);

        Assert.Equal(Confidence.Confirmed, analysis.PlatformSupport(OsPlatform.Windows));
        Assert.Equal(Confidence.Confirmed, analysis.PlatformSupport(OsPlatform.Linux));
        Assert.True(analysis.HasDownloadableBinaries);
        Assert.Contains(CpuArchitecture.X64, analysis.SupportedArchitectures);
    }

    [Fact]
    public async Task An_unknown_platform_is_reported_as_unknown_not_omitted()
    {
        var github = new FakeGitHubClient { Tree = TestTrees.Of("README.md") };

        var analysis = await Analyzer(github).AnalyzeAsync(TestRepositories.Create(), NoReleases());

        Assert.Equal(Confidence.Unknown, analysis.PlatformSupport(OsPlatform.Windows));
    }
}
