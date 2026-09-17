using System.Text.Json;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Install;
using static RepoDeck.Tests.InstallPlanFixtures;

namespace RepoDeck.Tests;

/// <summary>
/// The cases where RepoDeck must refuse to propose an installation, and the guarantees
/// Milestone 3 will depend on.
/// </summary>
public class InstallPlanBoundaryTests
{
    [Fact]
    public void A_source_only_project_produces_a_blocked_source_build_plan()
    {
        var plan = Plan(WindowsX64, "Source code (zip)");

        Assert.Equal(InstallStrategy.SourceBuild, plan.Strategy);
        Assert.False(plan.CanProceed);
        Assert.NotEmpty(plan.BlockingIssues);
        Assert.True(plan.IsDeliberatelyUnsupported);
    }

    [Fact]
    public void A_project_with_no_releases_produces_a_source_build_plan()
    {
        var plan = PlanFor(Analysis(), WindowsX64, []);

        Assert.Equal(InstallStrategy.SourceBuild, plan.Strategy);
        Assert.False(plan.CanProceed);
        Assert.Contains(plan.BlockingIssues, b => b.Contains("no releases"));
    }

    [Fact]
    public void A_release_for_other_platforms_only_produces_no_usable_plan()
    {
        var plan = Plan(WindowsX64, "Tool-linux-x64.tar.gz", "Tool-macos-arm64.dmg");

        Assert.False(plan.CanProceed);
        Assert.NotEmpty(plan.BlockingIssues);
    }

    [Fact]
    public void An_incomplete_analysis_never_produces_a_plan()
    {
        // A rate-limited analysis must not be turned into confident instructions.
        var plan = PlanFor(
            Analysis(complete: false), WindowsX64,
            [TestRepositories.Release("v1.0", false, "Tool-win-x64.zip")]);

        Assert.False(plan.CanProceed);
        Assert.Contains(plan.BlockingIssues, b => b.Contains("could not finish analysing"));
    }

    [Fact]
    public void A_prerelease_is_flagged_in_the_plan()
    {
        var plan = PlanFor(
            Analysis(), WindowsX64,
            [TestRepositories.Release("v2.0.0-beta", true, "Tool-win-x64.zip")]);

        Assert.True(plan.IsPrerelease);
        Assert.Contains(plan.Warnings, w => w.Contains("pre-release"));
    }

    [Fact]
    public void A_library_produces_a_plan_that_warns_it_may_not_be_runnable()
    {
        var plan = PlanFor(
            Analysis(ApplicationType.Library), WindowsX64,
            [TestRepositories.Release("v1.0", false, "Tool-win-x64.zip")]);

        Assert.Contains(plan.Warnings, w => w.Contains("may not be something you run directly"));
    }

    [Fact]
    public void A_plan_survives_a_round_trip_through_JSON()
    {
        // Milestone 3's installer will read a stored plan, so this has to hold.
        var plan = Plan(WindowsX64, "Tool-win-x64.zip");

        var json = JsonSerializer.Serialize(plan);
        var restored = JsonSerializer.Deserialize<InstallPlan>(json);

        Assert.NotNull(restored);
        Assert.Equal(plan.AssetUrl, restored!.AssetUrl);
        Assert.Equal(plan.Strategy, restored.Strategy);
        Assert.Equal(plan.PackageType, restored.PackageType);
        Assert.Equal(plan.ProposedInstallDirectory, restored.ProposedInstallDirectory);
        Assert.Equal(plan.ExecutableCandidates, restored.ExecutableCandidates);
        Assert.Equal(plan.RequiresElevation, restored.RequiresElevation);
    }

    [Fact]
    public void A_plan_contains_no_credentials()
    {
        var plan = Plan(WindowsX64, "Tool-win-x64.zip");
        var json = JsonSerializer.Serialize(plan);

        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_download_url_is_taken_from_the_release_payload_over_https()
    {
        var plan = Plan(WindowsX64, "Tool-win-x64.zip");

        // The URL comes from the release payload, never assembled from README text.
        Assert.NotNull(plan.AssetUrl);
        Assert.StartsWith("https://", plan.AssetUrl);
        Assert.EndsWith("Tool-win-x64.zip", plan.AssetUrl);
    }
}
