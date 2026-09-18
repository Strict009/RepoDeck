using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Update;

namespace RepoDeck.Tests;

/// <summary>
/// Turning "there is a newer release" into exactly what RepoDeck would do about it.
/// </summary>
/// <remarks>
/// The planner refuses more readily than the install planner does, and deliberately: a
/// first install that goes wrong leaves nothing behind, whereas a bad update replaces
/// something that was working.
/// </remarks>
public class UpdatePlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-updateplan-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    private static readonly MachineProfile Windows =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public UpdatePlannerTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
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

    private ApplicationManifest Installed(
        InstallStrategy strategy = InstallStrategy.PortableArchive,
        InstallationState state = InstallationState.Installed,
        string? path = null) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        ReleaseTag = "v1.0.0",
        AssetName = "tool-1.0.0-win-x64.zip",
        AssetDownloadUrl = "https://example.invalid/old.zip",
        State = state,
        InstalledPath = path ?? Path.Combine(_paths.Apps, "someone__tool"),
        ExecutableRelativePath = state == InstallationState.Installed ? "tool.exe" : null,
        Platform = OsPlatform.Windows,
        Architecture = CpuArchitecture.X64,
        PackageType = PackageType.Zip,
        Strategy = strategy
    };

    private static GitHubRelease Release(string tag, params string[] assets) => new()
    {
        Id = 1,
        TagName = tag,
        Name = tag,
        PublishedAt = Now.AddDays(-1),
        Body = "Fixed some things.",
        HtmlUrl = "https://github.com/someone/tool/releases/tag/" + tag,
        Assets = (assets.Length == 0 ? new[] { "tool-" + tag + "-win-x64.zip" } : assets)
            .Select(name => new GitHubReleaseAsset
            {
                Id = name.GetHashCode(),
                Name = name,
                BrowserDownloadUrl = "https://example.invalid/" + name,
                Size = 5_000_000
            })
            .ToList()
    };

    private UpdateCheck CheckFor(ApplicationManifest manifest, params GitHubRelease[] releases) =>
        UpdateChecker.Evaluate(manifest, releases, Windows, Now);

    private UpdatePlan PlanFor(ApplicationManifest manifest, params GitHubRelease[] releases) =>
        UpdatePlanner.Create(manifest, CheckFor(manifest, releases), Windows, _paths);

    // ---- The ordinary case ------------------------------------------------

    [Fact]
    public void A_newer_release_produces_a_plan_that_can_proceed()
    {
        var plan = PlanFor(Installed(), Release("v1.1.0"), Release("v1.0.0"));

        Assert.True(plan.CanProceed);
        Assert.Equal(UpdateStrategy.ReplaceManagedInstallation, plan.Strategy);
        Assert.Equal("v1.1.0", plan.TargetReleaseTag);
        Assert.Equal("tool-v1.1.0-win-x64.zip", plan.AssetName);
        Assert.True(plan.AssetSize > 0);
    }

    [Fact]
    public void The_plan_describes_the_transition_in_both_directions()
    {
        var plan = PlanFor(Installed(), Release("v1.1.0"));

        Assert.Equal("v1.0.0", plan.InstalledVersionText);
        Assert.Equal("v1.1.0", plan.TargetVersionText);
        Assert.Contains("v1.0.0", plan.TransitionText);
        Assert.Contains("v1.1.0", plan.TransitionText);
    }

    [Fact]
    public void The_plan_carries_the_manifest_it_is_replacing()
    {
        // A rollback needs the old record as much as the old files.
        var installed = Installed();
        var plan = PlanFor(installed, Release("v1.1.0"));

        Assert.Equal(installed, plan.Current);
    }

    [Fact]
    public void The_plan_carries_release_notes_as_plain_text()
    {
        var plan = PlanFor(Installed(), Release("v1.1.0"));

        Assert.Equal("Fixed some things.", plan.TargetReleaseNotes);
        Assert.NotNull(plan.TargetReleaseUrl);
    }

    [Fact]
    public void A_running_program_has_to_be_closed_before_its_files_are_replaced()
    {
        Assert.True(PlanFor(Installed(), Release("v1.1.0")).RequiresApplicationClosed);
    }

    [Fact]
    public void The_executable_already_working_is_the_best_prediction_of_the_next_one()
    {
        var plan = PlanFor(Installed(), Release("v1.1.0"));

        Assert.Contains("tool.exe", plan.ExecutableCandidates);
    }

    // ---- Refusals ---------------------------------------------------------

    [Fact]
    public void No_newer_release_produces_no_plan()
    {
        var plan = PlanFor(Installed(), Release("v1.0.0"));

        Assert.False(plan.CanProceed);
        Assert.NotEmpty(plan.BlockingIssues);
    }

    [Fact]
    public void A_newer_release_with_nothing_for_this_machine_is_not_a_plan()
    {
        var plan = PlanFor(Installed(), Release("v2.0.0", "tool-linux-arm64.tar.gz"));

        Assert.False(plan.CanProceed);
        Assert.Equal(UpdateStrategy.NotSupported, plan.Strategy);
    }

    [Fact]
    public void A_source_archive_never_becomes_an_update()
    {
        // Building from source is out of scope, and always will be.
        var plan = PlanFor(Installed(), Release("v2.0.0", "tool-2.0.0-source.tar.gz"));

        Assert.False(plan.CanProceed);
    }

    [Fact]
    public void A_system_installer_never_replaces_a_working_managed_installation()
    {
        // RepoDeck will not run installers, so swapping a working copy for one would
        // leave the user with a file where a program used to be.
        var plan = PlanFor(Installed(), Release("v2.0.0", "tool-2.0.0-setup.exe"));

        Assert.False(plan.CanProceed);
        Assert.Equal(UpdateStrategy.DownloadOnly, plan.Strategy);
        Assert.Contains(plan.BlockingIssues, b => b.Contains("worse off", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(plan.TargetReleaseUrl);
    }

    [Fact]
    public void A_download_only_record_has_nothing_to_replace()
    {
        var manifest = Installed(state: InstallationState.Downloaded);

        var plan = PlanFor(manifest, Release("v2.0.0"));

        Assert.False(plan.CanProceed);
        Assert.Equal(UpdateStrategy.DownloadOnly, plan.Strategy);
    }

    [Fact]
    public void An_installation_recorded_outside_the_managed_folder_is_refused()
    {
        var outside = Path.Combine(_root, "elsewhere");
        var plan = PlanFor(Installed(path: outside), Release("v1.1.0"));

        Assert.False(plan.CanProceed);
        Assert.Contains(plan.BlockingIssues, b => b.Contains("outside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_uncomparable_version_produces_no_plan()
    {
        var manifest = Installed() with { ReleaseTag = "release-final" };

        var plan = UpdatePlanner.Create(
            manifest, CheckFor(manifest, Release("v2.0.0")), Windows, _paths);

        Assert.False(plan.CanProceed);
    }

    // ---- Warnings rather than refusals ------------------------------------

    [Fact]
    public void A_change_of_package_shape_is_warned_about_rather_than_refused()
    {
        // Still installable, but worth saying: the thing being replaced was a different
        // kind of download.
        var manifest = Installed(strategy: InstallStrategy.StandaloneExecutable);

        var plan = PlanFor(manifest, Release("v1.1.0"));

        Assert.True(plan.CanProceed);
        Assert.Contains(plan.Warnings, w => w.Contains("published as", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_unresolved_executable_choice_is_warned_about()
    {
        var manifest = Installed(state: InstallationState.AwaitingExecutableChoice) with
        {
            AlternativeExecutables = ["one.exe", "two.exe"]
        };

        var plan = PlanFor(manifest, Release("v1.1.0"));

        Assert.Contains(plan.Warnings, w => w.Contains("which file to run", StringComparison.OrdinalIgnoreCase));
    }

    // ---- What it never does ----------------------------------------------

    [Fact]
    public void The_planner_never_picks_an_asset_the_analysis_did_not()
    {
        // Asset selection lives in one place. Two places that choose which file to
        // download are two places that can disagree.
        var plan = PlanFor(Installed(),
            Release("v2.0.0", "tool-2.0.0-win-x64.zip", "tool-2.0.0-linux-x64.tar.gz"));

        Assert.Equal("tool-2.0.0-win-x64.zip", plan.AssetName);
    }

    [Fact]
    public void Every_refusal_says_why()
    {
        UpdatePlan[] refusals =
        [
            PlanFor(Installed(), Release("v1.0.0")),
            PlanFor(Installed(), Release("v2.0.0", "tool-linux-arm64.tar.gz")),
            PlanFor(Installed(), Release("v2.0.0", "tool-2.0.0-setup.exe")),
            PlanFor(Installed(state: InstallationState.Downloaded), Release("v2.0.0"))
        ];

        foreach (var plan in refusals)
        {
            Assert.False(plan.CanProceed);
            Assert.NotEmpty(plan.BlockingIssues);
            Assert.All(plan.BlockingIssues, b => Assert.False(string.IsNullOrWhiteSpace(b)));
        }
    }
}

/// <summary>
/// Which of several newer releases gets offered.
/// </summary>
/// <remarks>
/// Live use turned up a project publishing both "v0.0.3-release.4" and
/// "v0.0.3-release.3-patch.1". Semantic versioning ranks an alphanumeric identifier above
/// a numeric one, so "3-patch" beat "4" and RepoDeck offered the older build. The numbers
/// are identical and the suffix settles nothing, so the publication date settles it.
/// </remarks>
public class ReleaseSelectionTests
{
    private static readonly MachineProfile Windows =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationManifest Installed(string tag) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        ReleaseTag = tag,
        State = InstallationState.Installed,
        InstalledPath = @"C:\RepoDeck\Apps\someone__tool",
        ExecutableRelativePath = "tool.exe",
        Strategy = InstallStrategy.PortableArchive
    };

    private static GitHubRelease Release(string tag, int daysAgo) => new()
    {
        Id = tag.GetHashCode(),
        TagName = tag,
        Name = tag,
        PublishedAt = Now.AddDays(-daysAgo),
        Assets =
        [
            new GitHubReleaseAsset
            {
                Id = tag.GetHashCode(),
                Name = "tool-win-x64.zip",
                BrowserDownloadUrl = "https://example.invalid/tool-win-x64.zip",
                Size = 1_000_000
            }
        ]
    };

    [Fact]
    public void The_most_recently_published_of_two_equal_numbers_is_offered()
    {
        var check = UpdateChecker.Evaluate(
            Installed("v0.0.2"),
            [Release("v0.0.3-release.3-patch.1", daysAgo: 10), Release("v0.0.3-release.4", daysAgo: 9)],
            Windows,
            Now);

        Assert.Equal(UpdateState.UpdateAvailable, check.State);
        Assert.Equal("v0.0.3-release.4", check.LatestRelease!.TagName);
    }

    [Fact]
    public void The_order_they_arrive_in_does_not_change_the_answer()
    {
        var check = UpdateChecker.Evaluate(
            Installed("v0.0.2"),
            [Release("v0.0.3-release.4", daysAgo: 9), Release("v0.0.3-release.3-patch.1", daysAgo: 10)],
            Windows,
            Now);

        Assert.Equal("v0.0.3-release.4", check.LatestRelease!.TagName);
    }

    [Fact]
    public void A_higher_number_still_beats_a_more_recent_lower_one()
    {
        // The date only settles ties. A newer publication of an older version number is
        // still an older version.
        var check = UpdateChecker.Evaluate(
            Installed("v1.0.0"),
            [Release("v2.0.0", daysAgo: 30), Release("v1.5.0", daysAgo: 1)],
            Windows,
            Now);

        Assert.Equal("v2.0.0", check.LatestRelease!.TagName);
    }

    [Fact]
    public void Numbers_alone_decide_when_only_the_suffix_differs()
    {
        var four = ReleaseVersion.TryParse("v0.0.3-release.4")!;
        var patch = ReleaseVersion.TryParse("v0.0.3-release.3-patch.1")!;

        Assert.Equal(0, four.CompareNumericTo(patch));
    }
}
