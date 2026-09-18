using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Tests;

/// <summary>
/// What RepoDeck says about which build it is.
/// </summary>
/// <remarks>
/// The version is shown to people - on the Settings page, in the installer, in the artifact
/// names - and it is the thing an alpha tester quotes when reporting a problem. These hold
/// it to being readable, consistent, and honest about not being finished.
/// </remarks>
public class AppVersionTests
{
    [Fact]
    public void The_version_is_readable()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Current));
        Assert.NotEqual("unknown", AppVersion.Current);
    }

    [Fact]
    public void The_version_is_a_version_RepoDeck_itself_can_parse()
    {
        // RepoDeck refuses to compare tags it cannot read. Its own version being one of
        // them would be a poor advertisement, and would break the day it starts looking
        // for updates to itself.
        var parsed = ReleaseVersion.TryParse(AppVersion.Current);

        Assert.NotNull(parsed);
    }

    [Fact]
    public void This_build_does_not_claim_to_be_finished()
    {
        // The guard that matters. RepoDeck installs other people's software onto somebody's
        // machine; dropping the pre-release suffix is a claim about stability, and it should
        // take a deliberate act with a failing test in the way, not an absent-minded edit to
        // Directory.Build.props.
        Assert.True(
            AppVersion.IsPrerelease,
            $"RepoDeck reports version '{AppVersion.Current}', which has no pre-release "
            + "suffix and therefore presents itself as a finished release. If that is "
            + "genuinely intended, change this test deliberately.");
    }

    [Fact]
    public void A_development_build_says_so_in_words()
    {
        Assert.Contains(AppVersion.Current, AppVersion.Description);
        Assert.Contains("development build", AppVersion.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_version_carries_no_build_metadata()
    {
        // "0.1.0-alpha+3f2a9c1" is for a build system. The Settings page is not one.
        Assert.DoesNotContain('+', AppVersion.Current);
    }

    [Fact]
    public void The_version_is_below_one_point_zero()
    {
        var parsed = ReleaseVersion.TryParse(AppVersion.Current)!;

        Assert.Equal(0, parsed.Numbers[0]);
    }
}
