using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

public class SetupDifficultyTests
{
    private static readonly MachineProfile WindowsX64 =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static AppPaths Paths() => new(Path.Combine(Path.GetTempPath(), "RepoDeckSetupTests"));

    private static RepositoryAnalysis Analysis(
        ApplicationType type = ApplicationType.DesktopApplication,
        ProjectType project = ProjectType.DotNet,
        bool complete = true) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        ApplicationType = type,
        ApplicationTypeConfidence = Confidence.Likely,
        ProjectTypes = project == ProjectType.Unknown ? [] : [project],
        IsComplete = complete
    };

    private static SetupAssessment Assess(
        RepositoryAnalysis analysis, params string[] assets)
    {
        var releases = assets.Length == 0
            ? ReleaseAnalyzer.Analyze([], WindowsX64)
            : ReleaseAnalyzer.Analyze([TestRepositories.Release("v1.0", false, assets)], WindowsX64);

        var plan = new InstallPlanner(Paths())
            .Create(TestRepositories.Create(), analysis, releases, WindowsX64);

        return SetupDifficultyEvaluator.Evaluate(analysis, releases, plan);
    }

    [Fact]
    public void A_portable_build_for_this_machine_is_easy()
    {
        var setup = Assess(Analysis(), "tool-win-x64.zip");

        Assert.Equal(SetupLevel.Easy, setup.Level);
        Assert.Equal("Ready to use", setup.Label);
        Assert.Contains(setup.Reasons, r => r.Contains("install it and start it for you"));
    }

    [Fact]
    public void An_installer_needs_some_setup_because_the_user_runs_it()
    {
        var setup = Assess(Analysis(), "tool-setup-x64.msi");

        Assert.Equal(SetupLevel.SomeSetup, setup.Level);
        Assert.Equal("Needs some setup", setup.Label);
        Assert.Contains(setup.Reasons, r => r.Contains("you run it yourself"));
    }

    [Fact]
    public void A_library_is_for_developers_however_easy_it_would_be_to_download()
    {
        var setup = Assess(Analysis(ApplicationType.Library), "tool-win-x64.zip");

        Assert.Equal(SetupLevel.DeveloperFocused, setup.Level);
        Assert.Contains(setup.Reasons, r => r.Contains("not a program you run"));
    }

    [Fact]
    public void A_compiled_project_with_no_builds_is_advanced()
    {
        var setup = Assess(Analysis(project: ProjectType.CPlusPlus));

        Assert.Equal(SetupLevel.Advanced, setup.Level);
        Assert.Contains(setup.Reasons, r => r.Contains("compiling it yourself"));
    }

    [Theory]
    [InlineData(ProjectType.Python, "Python")]
    [InlineData(ProjectType.Node, "Node.js")]
    public void An_interpreted_project_needs_its_runtime(ProjectType project, string runtime)
    {
        var setup = Assess(Analysis(project: project));

        Assert.Equal(SetupLevel.SomeSetup, setup.Level);
        Assert.Contains(setup.Reasons, r => r.Contains(runtime));
    }

    [Fact]
    public void An_incomplete_analysis_yields_no_claim()
    {
        var setup = Assess(Analysis(complete: false), "tool-win-x64.zip");

        Assert.Equal(SetupLevel.Unknown, setup.Level);
        Assert.Equal(Confidence.Unknown, setup.Confidence);
    }

    [Fact]
    public void Difficulty_is_never_inferred_from_popularity()
    {
        // Two identical projects, one wildly popular. The verdict must not move.
        var quiet = SetupDifficultyEvaluator.EvaluateFromMetadata(
            TestRepositories.Create(stars: 3, description: "A desktop tool.", language: "C#"),
            ApplicationLikelihoodEvaluator.Evaluate(TestRepositories.Create(stars: 3, language: "C#")));

        var famous = SetupDifficultyEvaluator.EvaluateFromMetadata(
            TestRepositories.Create(stars: 300_000, description: "A desktop tool.", language: "C#"),
            ApplicationLikelihoodEvaluator.Evaluate(TestRepositories.Create(stars: 300_000, language: "C#")));

        Assert.Equal(quiet.Level, famous.Level);
    }

    [Fact]
    public void A_metadata_assessment_never_claims_more_than_Possible()
    {
        var repository = TestRepositories.Create(language: "Python", description: "A tool.");
        var setup = SetupDifficultyEvaluator.EvaluateFromMetadata(
            repository, ApplicationLikelihoodEvaluator.Evaluate(repository));

        Assert.True(setup.Confidence is Confidence.Possible or Confidence.Unknown,
            $"Metadata claimed {setup.Confidence}.");
    }

    [Fact]
    public void Every_assessment_can_explain_itself()
    {
        var setup = Assess(Analysis(), "tool-win-x64.zip");

        Assert.NotEmpty(setup.Reasons);
        Assert.All(setup.Reasons, r => Assert.False(string.IsNullOrWhiteSpace(r)));
        Assert.False(string.IsNullOrWhiteSpace(setup.Summary));
    }
}

public class FriendlyNamingTests
{
    [Theory]
    [InlineData("obs-studio", "Obs Studio")]
    [InlineData("youtube-dl", "Youtube Dl")]
    [InlineData("free_download_manager", "Free Download Manager")]
    [InlineData("bat", "Bat")]
    [InlineData("ShareX", "ShareX")]
    [InlineData("shotcut", "Shotcut")]
    public void Repository_slugs_are_made_readable(string name, string expected)
    {
        Assert.Equal(expected, FriendlyNaming.ForRepository(name));
    }

    [Theory]
    [InlineData("my-ui-toolkit", "My UI Toolkit")]
    [InlineData("some-cli-tool", "Some CLI Tool")]
    [InlineData("pdf-reader", "PDF Reader")]
    public void Known_abbreviations_are_not_mangled(string name, string expected)
    {
        Assert.Equal(expected, FriendlyNaming.ForRepository(name));
    }

    [Fact]
    public void An_empty_name_does_not_produce_an_empty_card()
    {
        Assert.Equal("Untitled", FriendlyNaming.ForRepository(""));
    }

    [Theory]
    [InlineData("shotcut", "S")]
    [InlineData("7zip", "7")]
    [InlineData("-dash", "D")]
    public void A_fallback_tile_always_has_a_letter(string name, string expected)
    {
        Assert.Equal(expected, FriendlyNaming.Initial(name));
    }

    [Fact]
    public void The_fallback_colour_is_stable_for_a_given_project()
    {
        // The same project should look the same on every visit.
        Assert.Equal(
            FriendlyNaming.ColourFor("someone/tool").ToString(),
            FriendlyNaming.ColourFor("someone/tool").ToString());
    }

    [Theory]
    [InlineData(new[] { "windows", "gui" }, "Windows")]
    [InlineData(new[] { "linux", "cli" }, "Linux")]
    [InlineData(new[] { "cross-platform" }, "Cross-platform")]
    public void Platform_hints_come_from_tags(string[] topics, string expected)
    {
        Assert.Equal(expected, FriendlyNaming.PlatformHint(topics, null));
    }

    [Fact]
    public void No_platform_tags_means_no_claim_at_all()
    {
        Assert.Equal("", FriendlyNaming.PlatformHint(["editor"], "C#"));
    }
}
