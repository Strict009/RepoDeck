using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

/// <summary>
/// The five answers a card and Quick Look can give about what RepoDeck can do.
/// </summary>
/// <remarks>
/// The rule these tests exist to protect: this is a capability statement, never a safety
/// statement, and the interface must always be able to say why it gave the answer it did.
/// </remarks>
public class InstallabilityTests
{
    private static readonly MachineProfile Windows =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static RepositoryAnalysis Analysis(bool complete = true) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        IsComplete = complete
    };

    private static InstallPlan Workable(
        InstallStrategy strategy = InstallStrategy.PortableArchive,
        bool elevation = false) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        AssetName = "tool-win-x64.zip",
        AssetUrl = "https://example.invalid/tool.zip",
        AssetSize = 4_000_000,
        ReleaseTag = "v2.1",
        Platform = OsPlatform.Windows,
        Architecture = CpuArchitecture.X64,
        Strategy = strategy,
        RequiresElevation = elevation,
        Confidence = Confidence.Likely
    };

    // ---- From a search result --------------------------------------------

    [Fact]
    public void A_search_result_never_claims_something_is_ready_to_install()
    {
        // Nothing has been looked at. Claiming otherwise would be guessing about the one
        // thing the user most wants to be able to rely on.
        var repository = TestRepositories.Create("tool", description: "A tool that does a thing.");
        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);
        var setup = SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood);

        var verdict = InstallabilityEvaluator.FromMetadata(repository, likelihood, setup);

        Assert.NotEqual(InstallabilityState.ReadyToInstall, verdict.State);
        Assert.False(verdict.AllowsDirectInstall);
        Assert.Contains(verdict.Reasons, r => r.Contains("not looked at", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_library_is_called_developer_focused_without_being_looked_at()
    {
        var repository = TestRepositories.Create(
            "json", "nlohmann",
            description: "JSON for Modern C++, a header-only library.",
            topics: ["library", "header-only", "cpp"]);

        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);
        var setup = SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood);
        var verdict = InstallabilityEvaluator.FromMetadata(repository, likelihood, setup);

        Assert.Equal(InstallabilityState.DeveloperFocused, verdict.State);
        Assert.NotEmpty(verdict.Reasons);
    }

    [Fact]
    public void A_metadata_answer_never_exceeds_possible_confidence()
    {
        foreach (var name in new[] { "tool", "some-library", "app" })
        {
            var repository = TestRepositories.Create(name);
            var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);
            var setup = SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood);
            var verdict = InstallabilityEvaluator.FromMetadata(repository, likelihood, setup);

            Assert.True(verdict.Confidence is Confidence.Possible or Confidence.Unknown,
                $"{name} claimed {verdict.Confidence} from metadata alone.");
        }
    }

    [Fact]
    public void An_abandoned_project_says_so_in_its_evidence()
    {
        var repository = TestRepositories.Create("tool", archived: true);
        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);
        var setup = SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood);

        var verdict = InstallabilityEvaluator.FromMetadata(repository, likelihood, setup);

        Assert.Contains(verdict.Reasons, r => r.Contains("stopped maintaining", StringComparison.OrdinalIgnoreCase));
    }

    // ---- From a plan ------------------------------------------------------

    [Fact]
    public void A_workable_plan_is_ready_to_install_and_says_what_it_found()
    {
        var verdict = InstallabilityEvaluator.FromPlan(
            Workable(), Analysis(), ReleaseAnalysis.None(""), Windows);

        Assert.Equal(InstallabilityState.ReadyToInstall, verdict.State);
        Assert.True(verdict.AllowsDirectInstall);
        Assert.Contains(verdict.Reasons, r => r.Contains("tool-win-x64.zip", StringComparison.Ordinal));
        Assert.Contains(verdict.Reasons, r => r.Contains("v2.1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(InstallStrategy.WindowsInstaller)]
    [InlineData(InstallStrategy.LinuxPackage)]
    public void A_system_installer_is_needs_setup_rather_than_ready(InstallStrategy strategy)
    {
        // RepoDeck fetches these and hands them over. That is not installing, and the
        // card must not offer a button that implies otherwise.
        var verdict = InstallabilityEvaluator.FromPlan(
            Workable(strategy), Analysis(), ReleaseAnalysis.None(""), Windows);

        Assert.Equal(InstallabilityState.NeedsSetup, verdict.State);
        Assert.False(verdict.AllowsDirectInstall);
    }

    [Fact]
    public void Anything_needing_elevation_is_never_offered_as_ready_to_install()
    {
        var verdict = InstallabilityEvaluator.FromPlan(
            Workable(elevation: true), Analysis(), ReleaseAnalysis.None(""), Windows);

        Assert.Equal(InstallabilityState.NeedsSetup, verdict.State);
        Assert.False(verdict.AllowsDirectInstall);
    }

    [Fact]
    public void A_source_build_is_developer_focused()
    {
        var plan = InstallPlan.NotPossible(
            "someone", "tool", "https://github.com/someone/tool",
            InstallStrategy.SourceBuild,
            "This project publishes no releases.");

        var verdict = InstallabilityEvaluator.FromPlan(
            plan, Analysis(), ReleaseAnalysis.None("no releases"), Windows);

        Assert.Equal(InstallabilityState.DeveloperFocused, verdict.State);
        Assert.False(verdict.AllowsDirectInstall);
        Assert.Contains(verdict.Reasons, r => r.Contains("compiled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_release_with_downloads_but_none_for_this_machine_is_not_compatible()
    {
        // The distinction that matters: there is something here, just not for you.
        var releases = new ReleaseAnalysis
        {
            Release = new GitHubRelease { TagName = "v1", Name = "v1" },
            Assets =
            [
                new AssetAnalysis
                {
                    Name = "tool-linux-arm64.tar.gz",
                    DownloadUrl = "https://example.invalid/a",
                    Platform = OsPlatform.Linux,
                    Architecture = CpuArchitecture.Arm64,
                    PackageType = PackageType.TarGz,
                    Compatibility = AssetCompatibility.Incompatible
                }
            ],
            NoRecommendationReason = "Every download is for a different operating system."
        };

        var plan = InstallPlan.NotPossible(
            "someone", "tool", "https://github.com/someone/tool",
            InstallStrategy.Unknown, "No suitable download was found.");

        var verdict = InstallabilityEvaluator.FromPlan(plan, Analysis(), releases, Windows);

        Assert.Equal(InstallabilityState.NotCompatible, verdict.State);
        Assert.Equal(Confidence.Confirmed, verdict.Confidence);
        Assert.Contains(verdict.Reasons, r => r.Contains("Windows", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unfinished_analysis_answers_unknown_rather_than_guessing()
    {
        var plan = InstallPlan.NotPossible(
            "someone", "tool", "https://github.com/someone/tool",
            InstallStrategy.Unknown, "RepoDeck could not finish analysing this repository.");

        var verdict = InstallabilityEvaluator.FromPlan(
            plan,
            Analysis(complete: false) with { IncompleteReason = "GitHub stopped responding." },
            ReleaseAnalysis.None(""),
            Windows);

        Assert.Equal(InstallabilityState.Unknown, verdict.State);
        Assert.False(verdict.AllowsDirectInstall);
    }

    // ---- The rule that matters most --------------------------------------

    [Fact]
    public void Only_ready_to_install_permits_a_direct_install_action()
    {
        foreach (var state in Enum.GetValues<InstallabilityState>())
        {
            var verdict = new Installability { State = state };

            Assert.Equal(state == InstallabilityState.ReadyToInstall, verdict.AllowsDirectInstall);
        }
    }

    [Fact]
    public void Every_state_has_a_label_and_a_sentence()
    {
        foreach (var state in Enum.GetValues<InstallabilityState>())
        {
            var verdict = new Installability { State = state };

            Assert.False(string.IsNullOrWhiteSpace(verdict.Label));
            Assert.False(string.IsNullOrWhiteSpace(verdict.Summary));
        }
    }

    [Fact]
    public void No_state_ever_describes_the_software_as_safe()
    {
        // RepoDeck cannot tell anyone that, so nothing it says may be read that way.
        string[] forbidden = ["safe", "trusted", "verified", "secure", "malware"];

        foreach (var state in Enum.GetValues<InstallabilityState>())
        {
            var verdict = new Installability { State = state };
            var text = (verdict.Label + " " + verdict.Summary).ToLowerInvariant();

            foreach (var word in forbidden)
            {
                Assert.DoesNotContain(word, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_disclaimer_says_plainly_what_the_state_is_not()
    {
        Assert.Contains("not whether the software is safe", Installability.NotASafetyJudgement);
    }

    [Fact]
    public void Evidence_is_deduplicated_and_bounded()
    {
        // A tooltip is not a log file.
        var releases = new ReleaseAnalysis { NoRecommendationReason = "Nothing suitable." };

        var plan = Workable() with
        {
            Warnings =
            [
                "One", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight"
            ]
        };

        var verdict = InstallabilityEvaluator.FromPlan(plan, Analysis(), releases, Windows);

        Assert.True(verdict.Reasons.Count <= 6);
        Assert.Equal(verdict.Reasons.Count, verdict.Reasons.Distinct().Count());
    }
}
