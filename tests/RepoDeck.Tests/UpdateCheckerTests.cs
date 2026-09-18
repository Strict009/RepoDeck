using RepoDeck.Models;
using RepoDeck.Services.Update;

namespace RepoDeck.Tests;

/// <summary>
/// Reading a release tag as a version number.
/// </summary>
/// <remarks>
/// The rule these protect: RepoDeck parses the shapes it can be sure about and refuses the
/// rest. A wrong answer here means offering somebody an "update" that is older than what
/// they already have.
/// </remarks>
public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("v1.2.3")]
    [InlineData("V1.2.3")]
    [InlineData("0.0.3")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v2.0.0-beta.1")]
    [InlineData("10.20.30")]
    public void The_shapes_RepoDeck_understands_are_read(string tag)
    {
        Assert.NotNull(ReleaseVersion.TryParse(tag));
    }

    [Theory]
    [InlineData("release-final")]
    [InlineData("nightly")]
    [InlineData("2024-03-15")]
    [InlineData("build 417")]
    [InlineData("v1")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("latest")]
    [InlineData("1.2.3.4.5")]
    [InlineData("version 1.2.3")]
    public void Anything_else_is_refused_rather_than_half_read(string? tag)
    {
        Assert.Null(ReleaseVersion.TryParse(tag));
    }

    [Theory]
    [InlineData("1.2.4", "1.2.3")]
    [InlineData("1.3.0", "1.2.9")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("v0.0.4", "v0.0.3")]
    [InlineData("1.2.3", "1.2.3-beta.1")]
    [InlineData("1.10.0", "1.9.0")]
    public void Newer_is_newer(string newer, string older)
    {
        var a = ReleaseVersion.TryParse(newer)!;
        var b = ReleaseVersion.TryParse(older)!;

        Assert.True(a.IsNewerThan(b));
        Assert.False(b.IsNewerThan(a));
    }

    [Fact]
    public void Ten_is_not_less_than_nine()
    {
        // Text ordering would get this wrong, which is the whole reason for parsing.
        Assert.True(ReleaseVersion.TryParse("1.10.0")!.IsNewerThan(ReleaseVersion.TryParse("1.9.0")!));
    }

    [Theory]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.0", "1.2.0.0")]
    [InlineData("v1.2.3", "1.2.3")]
    public void A_missing_component_is_zero(string a, string b)
    {
        Assert.Equal(0, ReleaseVersion.TryParse(a)!.CompareTo(ReleaseVersion.TryParse(b)!));
    }

    [Fact]
    public void A_prerelease_is_older_than_the_release_it_precedes()
    {
        var release = ReleaseVersion.TryParse("2.0.0")!;
        var beta = ReleaseVersion.TryParse("2.0.0-beta.1")!;

        Assert.True(release.IsNewerThan(beta));
        Assert.True(beta.LooksLikePrerelease);
        Assert.False(release.LooksLikePrerelease);
    }

    [Theory]
    [InlineData("2.0.0", "1.0.0", VersionStep.Major)]
    [InlineData("1.3.0", "1.2.0", VersionStep.Minor)]
    [InlineData("1.2.4", "1.2.3", VersionStep.Patch)]
    [InlineData("1.2.3", "1.2.3", VersionStep.NotNewer)]
    [InlineData("1.0.0", "2.0.0", VersionStep.NotNewer)]
    public void The_size_of_the_jump_is_described(string to, string from, VersionStep expected)
    {
        Assert.Equal(expected,
            ReleaseVersion.TryParse(to)!.StepFrom(ReleaseVersion.TryParse(from)!));
    }

    [Fact]
    public void The_original_tag_is_kept_exactly_as_written()
    {
        Assert.Equal("v1.2.3", ReleaseVersion.TryParse("v1.2.3")!.Original);
        Assert.Equal("v1.2.3", ReleaseVersion.TryParse("  v1.2.3  ")!.Original);
    }

    [Fact]
    public void A_pathological_tag_gets_no_answer_rather_than_time()
    {
        // Release tags are remote content.
        var nasty = new string('9', 5000) + "." + new string('9', 5000);

        Assert.Null(ReleaseVersion.TryParse(nasty));
    }
}

/// <summary>
/// Deciding whether an installed application has a newer release worth offering.
/// </summary>
public class UpdateCheckerTests
{
    private static readonly MachineProfile Windows =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationManifest Installed(
        string tag = "v1.0.0", bool prerelease = false,
        InstallationState state = InstallationState.Installed) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        ReleaseTag = tag,
        IsPrerelease = prerelease,
        State = state,
        InstalledPath = @"C:\RepoDeck\Apps\someone__tool",
        ExecutableRelativePath = "tool.exe",
        Strategy = InstallStrategy.PortableArchive
    };

    private static GitHubRelease Release(
        string tag, bool prerelease = false, bool draft = false, params string[] assets) => new()
    {
        Id = tag.GetHashCode(),
        TagName = tag,
        Name = tag,
        PublishedAt = Now.AddDays(-1),
        Prerelease = prerelease,
        Draft = draft,
        Assets = assets.Length == 0
            ? [Asset("tool-" + tag + "-win-x64.zip")]
            : assets.Select(Asset).ToList()
    };

    private static GitHubReleaseAsset Asset(string name) => new()
    {
        Id = name.GetHashCode(),
        Name = name,
        BrowserDownloadUrl = "https://example.invalid/" + name,
        Size = 4_000_000
    };

    private static UpdateCheck Check(ApplicationManifest manifest, params GitHubRelease[] releases) =>
        UpdateChecker.Evaluate(manifest, releases, Windows, Now);

    // ---- The ordinary answers --------------------------------------------

    [Fact]
    public void No_newer_release_is_up_to_date()
    {
        var check = Check(Installed("v1.0.0"), Release("v1.0.0"));

        Assert.Equal(UpdateState.UpToDate, check.State);
        Assert.False(check.HasUpdate);
        Assert.Contains("1.0.0", check.Explanation);
    }

    [Fact]
    public void A_newer_release_with_a_usable_asset_is_an_update()
    {
        var check = Check(Installed("v1.0.0"), Release("v1.1.0"), Release("v1.0.0"));

        Assert.Equal(UpdateState.UpdateAvailable, check.State);
        Assert.True(check.HasUpdate);
        Assert.Equal("v1.1.0", check.AvailableVersionText);
        Assert.Equal(VersionStep.Minor, check.Step);
        Assert.NotEmpty(check.Reasons);
    }

    [Fact]
    public void An_older_release_appearing_first_does_not_become_an_update()
    {
        // GitHub returns releases newest-first, but a project can publish a patch to an
        // old branch afterwards. Ordering is by version, not by position in the list.
        var check = Check(Installed("v2.0.0"), Release("v1.9.9"), Release("v2.0.0"));

        Assert.Equal(UpdateState.UpToDate, check.State);
    }

    // ---- Prereleases ------------------------------------------------------

    [Fact]
    public void A_prerelease_is_not_offered_to_somebody_on_a_stable_release()
    {
        var check = Check(Installed("v1.0.0"), Release("v2.0.0-beta.1", prerelease: true), Release("v1.0.0"));

        Assert.Equal(UpdateState.UpToDate, check.State);
        Assert.False(check.HasUpdate);
    }

    [Fact]
    public void Somebody_already_running_a_prerelease_is_offered_newer_ones()
    {
        // They are evidently following them, so hiding the next one helps nobody.
        var check = Check(
            Installed("v2.0.0-beta.1", prerelease: true),
            Release("v2.0.0-beta.2", prerelease: true),
            Release("v2.0.0-beta.1", prerelease: true));

        Assert.Equal(UpdateState.UpdateAvailable, check.State);
    }

    [Fact]
    public void A_stable_release_is_offered_to_somebody_on_a_prerelease_of_it()
    {
        var check = Check(
            Installed("v2.0.0-beta.1", prerelease: true),
            Release("v2.0.0"),
            Release("v2.0.0-beta.1", prerelease: true));

        Assert.Equal(UpdateState.UpdateAvailable, check.State);
        Assert.Equal("v2.0.0", check.AvailableVersionText);
    }

    [Fact]
    public void A_draft_release_is_never_considered()
    {
        var check = Check(Installed("v1.0.0"), Release("v9.9.9", draft: true), Release("v1.0.0"));

        Assert.Equal(UpdateState.UpToDate, check.State);
    }

    // ---- When RepoDeck cannot tell ---------------------------------------

    [Fact]
    public void An_uncomparable_installed_tag_gives_Unknown_rather_than_a_guess()
    {
        var check = Check(Installed("release-final"), Release("v2.0.0"));

        Assert.Equal(UpdateState.Unknown, check.State);
        Assert.Contains("cannot tell which version you have", check.Explanation);
        Assert.Contains(check.Reasons, r => r.Contains("releases page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Release_names_that_are_not_versions_give_Unknown()
    {
        var check = Check(Installed("v1.0.0"), Release("nightly"), Release("latest-build"));

        Assert.Equal(UpdateState.Unknown, check.State);
        Assert.Contains("not version numbers", check.Explanation);
    }

    [Fact]
    public void A_project_with_no_releases_gives_Unknown()
    {
        var check = Check(Installed("v1.0.0"));

        Assert.Equal(UpdateState.Unknown, check.State);
        Assert.Contains("no releases", check.Explanation);
    }

    [Fact]
    public void Unreadable_releases_alongside_readable_ones_are_skipped_and_mentioned()
    {
        var check = Check(Installed("v1.0.0"), Release("nightly"), Release("v1.1.0"));

        Assert.Equal(UpdateState.UpdateAvailable, check.State);
        Assert.Contains(check.Reasons, r => r.Contains("skipped", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Newer, but not for this machine ---------------------------------

    [Fact]
    public void A_newer_release_with_nothing_for_this_machine_is_a_manual_update()
    {
        var check = Check(
            Installed("v1.0.0"),
            Release("v2.0.0", assets: ["tool-linux-arm64.tar.gz"]),
            Release("v1.0.0"));

        Assert.Equal(UpdateState.ManualUpdateRequired, check.State);
        Assert.True(check.HasUpdate);
        Assert.Contains("cannot install it for you", check.Explanation);
        Assert.Contains(check.Reasons, r => r.Contains("releases page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_newer_release_containing_only_source_is_a_manual_update()
    {
        // A source archive is not an update to a portable installation.
        var check = Check(
            Installed("v1.0.0"),
            Release("v2.0.0", assets: ["tool-2.0.0-source.tar.gz"]),
            Release("v1.0.0"));

        Assert.NotEqual(UpdateState.UpdateAvailable, check.State);
    }

    // ---- What it never does ----------------------------------------------

    [Fact]
    public void Checking_never_modifies_the_manifest()
    {
        var manifest = Installed("v1.0.0");

        var before = manifest with { };
        Check(manifest, Release("v2.0.0"));

        Assert.Equal(before, manifest);
    }

    [Fact]
    public void Every_answer_other_than_NotChecked_explains_itself()
    {
        UpdateCheck[] all =
        [
            Check(Installed("v1.0.0"), Release("v1.0.0")),
            Check(Installed("v1.0.0"), Release("v2.0.0")),
            Check(Installed("release-final"), Release("v2.0.0")),
            Check(Installed("v1.0.0")),
            Check(Installed("v1.0.0"), Release("v2.0.0", assets: ["tool-linux-arm64.tar.gz"]))
        ];

        foreach (var check in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(check.Explanation));
            Assert.False(string.IsNullOrWhiteSpace(check.Label));
            Assert.NotNull(check.CheckedAt);
        }
    }

    [Fact]
    public void Every_state_has_a_label_that_is_not_a_colour()
    {
        foreach (var state in Enum.GetValues<UpdateState>())
        {
            Assert.False(string.IsNullOrWhiteSpace(new UpdateCheck { State = state }.Label));
        }
    }
}

/// <summary>
/// The suffix problem, which live use found rather than imagination.
/// </summary>
/// <remarks>
/// RepoDeck offered "v0.0.3" as an update to somebody running "v0.0.3-release.4". Under
/// semantic versioning that is correct - any suffix marks a pre-release, so the bare
/// version outranks it - but the project meant "build 4 of release 0.0.3", so the offer
/// was a downgrade with the word "update" on the button. These tests hold the line at:
/// a suffix RepoDeck does not recognise carries no ordering information at all.
/// </remarks>
public class AmbiguousVersionTests
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

    private static GitHubRelease Release(string tag) => new()
    {
        Id = tag.GetHashCode(),
        TagName = tag,
        Name = tag,
        PublishedAt = Now.AddDays(-30),
        Assets =
        [
            new GitHubReleaseAsset
            {
                Id = tag.GetHashCode(),
                Name = "tool-win-x64.zip",
                BrowserDownloadUrl = "https://example.invalid/tool-win-x64.zip",
                Size = 4_000_000
            }
        ]
    };

    private static UpdateCheck Check(string installed, params string[] releases) =>
        UpdateChecker.Evaluate(
            Installed(installed), releases.Select(Release).ToList(), Windows, Now);

    // ---- The comparison itself -------------------------------------------

    [Fact]
    public void A_build_suffix_is_not_treated_as_a_prerelease()
    {
        var build = ReleaseVersion.TryParse("v0.0.3-release.4")!;

        Assert.False(build.LooksLikePrerelease);
        Assert.True(build.HasUnrecognisedQualifier);
    }

    [Fact]
    public void A_real_prerelease_word_still_is_one()
    {
        foreach (var tag in new[]
                 {
                     "v2.0.0-beta.1", "v2.0.0-alpha", "v2.0.0-rc1", "v2.0.0-preview.3",
                     "v2.0.0-nightly", "v2.0.0-dev", "v2.0.0-canary.2"
                 })
        {
            var version = ReleaseVersion.TryParse(tag)!;

            Assert.True(version.LooksLikePrerelease, tag);
            Assert.False(version.HasUnrecognisedQualifier, tag);
        }
    }

    [Fact]
    public void Plain_version_beats_a_recognised_prerelease_of_the_same_number()
    {
        var stable = ReleaseVersion.TryParse("v2.0.0")!;
        var beta = ReleaseVersion.TryParse("v2.0.0-beta.1")!;

        Assert.True(stable.IsNewerThan(beta));
        Assert.False(beta.IsNewerThan(stable));
        Assert.True(stable.CanCompareWith(beta));
    }

    [Fact]
    public void Plain_version_does_not_beat_an_unrecognised_suffix_of_the_same_number()
    {
        var plain = ReleaseVersion.TryParse("v0.0.3")!;
        var build = ReleaseVersion.TryParse("v0.0.3-release.4")!;

        Assert.False(plain.IsNewerThan(build));
        Assert.False(build.IsNewerThan(plain));
        Assert.False(plain.CanCompareWith(build));
        Assert.Null(plain.CompareOrNull(build));
    }

    [Fact]
    public void Two_unrecognised_suffixes_of_the_same_number_cannot_be_ordered()
    {
        var four = ReleaseVersion.TryParse("v1.2.0-release.4")!;
        var five = ReleaseVersion.TryParse("v1.2.0-release.5")!;

        Assert.Null(five.CompareOrNull(four));
        Assert.False(five.IsNewerThan(four));
    }

    [Fact]
    public void Recognised_prereleases_of_one_version_are_still_ordered()
    {
        // Refusing to compare these would strand somebody following a beta series.
        var one = ReleaseVersion.TryParse("v2.0.0-beta.1")!;
        var two = ReleaseVersion.TryParse("v2.0.0-beta.2")!;

        Assert.True(two.IsNewerThan(one));
        Assert.False(one.IsNewerThan(two));
    }

    [Fact]
    public void Prerelease_numbers_are_compared_as_numbers_not_text()
    {
        // Text comparison puts "beta.10" before "beta.9", which is how people get
        // offered an older build than the one they are running.
        var nine = ReleaseVersion.TryParse("v2.0.0-beta.9")!;
        var ten = ReleaseVersion.TryParse("v2.0.0-beta.10")!;

        Assert.True(ten.IsNewerThan(nine));
        Assert.False(nine.IsNewerThan(ten));
    }

    [Fact]
    public void A_bare_prerelease_word_precedes_a_numbered_one()
    {
        var bare = ReleaseVersion.TryParse("v2.0.0-beta")!;
        var numbered = ReleaseVersion.TryParse("v2.0.0-beta.1")!;

        Assert.True(numbered.IsNewerThan(bare));
    }

    [Fact]
    public void An_identical_suffix_is_the_same_version()
    {
        var a = ReleaseVersion.TryParse("v1.2.0-release.4")!;
        var b = ReleaseVersion.TryParse("v1.2.0-RELEASE.4")!;

        Assert.Equal(0, a.CompareOrNull(b));
        Assert.True(a.CanCompareWith(b));
    }

    [Fact]
    public void A_higher_number_wins_whatever_the_suffix_says()
    {
        var newer = ReleaseVersion.TryParse("v0.0.4")!;
        var older = ReleaseVersion.TryParse("v0.0.3-release.4")!;

        Assert.True(newer.IsNewerThan(older));
        Assert.Equal(VersionStep.Patch, newer.StepFrom(older));
    }

    [Fact]
    public void A_suffixed_build_can_still_be_newer_than_a_lower_number()
    {
        var newer = ReleaseVersion.TryParse("v1.3.0-release.1")!;
        var older = ReleaseVersion.TryParse("v1.2.9")!;

        Assert.True(newer.IsNewerThan(older));
        Assert.Equal(VersionStep.Minor, newer.StepFrom(older));
    }

    // ---- What the user is told -------------------------------------------

    [Fact]
    public void The_downgrade_that_was_offered_is_no_longer_offered()
    {
        var check = Check("v0.0.3-release.4", "v0.0.3");

        Assert.NotEqual(UpdateState.UpdateAvailable, check.State);
        Assert.Equal(UpdateState.Unknown, check.State);
    }

    [Fact]
    public void An_uncomparable_pair_is_explained_rather_than_hidden()
    {
        var check = Check("v0.0.3-release.4", "v0.0.3");

        Assert.Contains("cannot tell", check.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v0.0.3", check.Explanation);
        Assert.NotEmpty(check.Reasons);
        Assert.Contains(check.Reasons, r => r.Contains("releases page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_genuinely_newer_release_is_still_offered_alongside_an_uncomparable_one()
    {
        // The ambiguous v0.0.3 must not mask the real v0.0.4.
        var check = Check("v0.0.3-release.4", "v0.0.3", "v0.0.4");

        Assert.Equal(UpdateState.UpdateAvailable, check.State);
        Assert.Equal("v0.0.4", check.LatestRelease!.TagName);
    }

    [Fact]
    public void An_equal_suffixed_version_is_up_to_date_not_unknown()
    {
        var check = Check("v0.0.3-release.4", "v0.0.3-release.4");

        Assert.Equal(UpdateState.UpToDate, check.State);
    }

    [Fact]
    public void An_older_plain_release_next_to_an_equal_one_is_up_to_date()
    {
        var check = Check("v1.2.0", "v1.1.0", "v1.2.0");

        Assert.Equal(UpdateState.UpToDate, check.State);
    }

    // ---- The size the user is shown before agreeing -----------------------

    [Fact]
    public void An_available_update_records_what_would_be_downloaded()
    {
        var check = Check("v1.0.0", "v1.1.0");

        Assert.Equal(UpdateState.UpdateAvailable, check.State);
        Assert.Equal("tool-win-x64.zip", check.AssetName);
        Assert.Equal(4_000_000, check.AssetSize);
    }

    [Fact]
    public void A_state_with_nothing_to_download_records_no_asset()
    {
        var check = Check("v1.0.0", "v1.0.0");

        Assert.Null(check.AssetName);
        Assert.Equal(0, check.AssetSize);
    }
}
