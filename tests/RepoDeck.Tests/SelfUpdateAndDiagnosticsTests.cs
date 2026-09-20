using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.GitHub;
using RepoDeck.Services.Update;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// RepoDeck noticing a newer RepoDeck.
/// </summary>
/// <remarks>
/// Deliberately a notification rather than an updater. RepoDeck's update transaction works
/// by moving the live installation aside and promoting a validated copy into its place, and
/// a running Windows process cannot have its own executable moved - so the mechanism it
/// already owns cannot be pointed at itself.
///
/// The comparison is the same conservative one applied to everything else. RepoDeck
/// refusing to guess about other projects and then guessing about itself would be the
/// worst of both.
/// </remarks>
public class SelfUpdateTests
{
    private static GitHubRelease Release(string tag, bool draft = false, string? body = null) => new()
    {
        Id = tag.GetHashCode(),
        TagName = tag,
        Name = tag,
        Draft = draft,
        Body = body,
        HtmlUrl = $"https://github.com/Strict009/RepoDeck/releases/tag/{tag}",
        PublishedAt = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public void A_newer_release_is_offered()
    {
        var result = SelfUpdateService.Evaluate("0.1.0-alpha", [Release("v0.1.1-alpha")]);

        Assert.Equal(SelfUpdateState.UpdateAvailable, result.State);
        Assert.Equal("v0.1.1-alpha", result.LatestVersion);
        Assert.True(result.HasUpdate);
    }

    [Fact]
    public void The_release_page_is_offered_rather_than_a_download()
    {
        var result = SelfUpdateService.Evaluate("0.1.0-alpha", [Release("v0.1.1-alpha")]);

        Assert.Contains("github.com/Strict009/RepoDeck/releases", result.ReleaseUrl);
    }

    [Fact]
    public void The_current_version_is_up_to_date()
    {
        var result = SelfUpdateService.Evaluate("0.1.0-alpha", [Release("v0.1.0-alpha")]);

        Assert.Equal(SelfUpdateState.UpToDate, result.State);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public void An_older_release_is_not_an_update()
    {
        var result = SelfUpdateService.Evaluate("0.2.0", [Release("v0.1.0")]);

        Assert.Equal(SelfUpdateState.UpToDate, result.State);
    }

    [Fact]
    public void Nothing_comparable_is_unknown_rather_than_a_friendly_lie()
    {
        // Somebody on a finished 0.2.0 where the only release is an old alpha. RepoDeck has
        // not established they are current - it has found nothing it will compare - and
        // saying "up to date" would be asserting something it does not know.
        var result = SelfUpdateService.Evaluate("0.2.0", [Release("v0.1.0-alpha")]);

        Assert.Equal(SelfUpdateState.Unknown, result.State);
        Assert.Contains("none it can compare", result.Explanation);
    }

    [Fact]
    public void A_prerelease_user_is_offered_the_next_prerelease()
    {
        // RepoDeck is itself pre-release. Somebody running an alpha is following alphas.
        var result = SelfUpdateService.Evaluate("0.1.0-alpha", [Release("v0.1.0-beta")]);

        Assert.Equal(SelfUpdateState.UpdateAvailable, result.State);
    }

    [Fact]
    public void A_draft_is_never_offered()
    {
        var result = SelfUpdateService.Evaluate(
            "0.1.0-alpha", [Release("v9.9.9", draft: true)]);

        Assert.NotEqual(SelfUpdateState.UpdateAvailable, result.State);
    }

    [Fact]
    public void The_newest_of_several_is_the_one_offered()
    {
        var result = SelfUpdateService.Evaluate(
            "0.1.0-alpha",
            [Release("v0.1.1-alpha"), Release("v0.3.0"), Release("v0.2.0")]);

        Assert.Equal("v0.3.0", result.LatestVersion);
    }

    [Fact]
    public void An_unreadable_own_version_says_so_rather_than_guessing()
    {
        var result = SelfUpdateService.Evaluate("some-custom-build", [Release("v1.0.0")]);

        Assert.Equal(SelfUpdateState.Unknown, result.State);
        Assert.Contains("cannot read its own version", result.Explanation);
    }

    [Fact]
    public void No_releases_at_all_is_unknown_not_up_to_date()
    {
        // Saying "you are up to date" having found nothing to compare against would be a
        // claim RepoDeck has not earned.
        var result = SelfUpdateService.Evaluate("0.1.0-alpha", []);

        Assert.Equal(SelfUpdateState.Unknown, result.State);
    }

    [Fact]
    public void Releases_with_unreadable_tags_are_skipped_rather_than_guessed_at()
    {
        var result = SelfUpdateService.Evaluate(
            "0.1.0-alpha", [Release("nightly"), Release("latest")]);

        Assert.Equal(SelfUpdateState.Unknown, result.State);
    }

    [Fact]
    public void Release_notes_are_bounded()
    {
        var result = SelfUpdateService.Evaluate(
            "0.1.0-alpha", [Release("v0.2.0", body: new string('x', 50_000))]);

        Assert.True(result.ReleaseNotes!.Length < 3_000);
    }

    [Fact]
    public void Every_state_explains_itself()
    {
        var all = new[]
        {
            SelfUpdateService.Evaluate("0.1.0-alpha", [Release("v0.2.0")]),
            SelfUpdateService.Evaluate("0.1.0-alpha", [Release("v0.1.0-alpha")]),
            SelfUpdateService.Evaluate("nonsense", [Release("v0.2.0")]),
            SelfUpdateService.Evaluate("0.1.0-alpha", [])
        };

        foreach (var result in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
        }
    }
}

/// <summary>
/// The report a user copies into a bug report.
/// </summary>
/// <remarks>
/// The one failure that matters here is a helpful diagnostic quietly publishing somebody's
/// credentials, so that is what most of these are about.
/// </remarks>
public class DiagnosticReportTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-diag-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    public DiagnosticReportTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a temp folder */ }
    }

    private string Compose(bool hasToken = false, int installed = 0) =>
        DiagnosticReport.Compose(_paths, RateLimitStatus.Unknown, hasToken, installed);

    [Fact]
    public void It_names_the_build_and_the_machine()
    {
        var report = Compose();

        Assert.Contains(AppVersion.Current, report);
        Assert.Contains("OS:", report);
        Assert.Contains("Process:", report);
        Assert.Contains(".NET:", report);
    }

    [Fact]
    public void It_says_where_RepoDeck_keeps_its_files()
    {
        var report = Compose();

        // Shown with the profile folder replaced by the variable that stands for it, so
        // the path says which location is in use without carrying a user name.
        Assert.Contains(DiagnosticReport.Shorten(_paths.Root), report);
        Assert.Contains(DiagnosticReport.Shorten(_paths.Logs), report);
    }

    [Fact]
    public void It_reports_whether_the_folder_can_actually_be_written_to()
    {
        // A read-only or redirected profile explains a whole class of failures at once.
        var report = Compose();

        Assert.Contains("Writable:", report);
    }

    // ---- What must never be in it -----------------------------------------

    [Fact]
    public void A_configured_token_is_reported_as_configured_and_never_quoted()
    {
        var report = Compose(hasToken: true);

        Assert.Contains("Token:      configured", report);

        // The distinction between "a token exists" and "the token is" is the whole point.
        Assert.DoesNotContain("gho_", report);
        Assert.DoesNotContain("ghp_", report);
        Assert.DoesNotContain("github_pat_", report);
    }

    [Fact]
    public void An_environment_token_does_not_leak_into_the_report()
    {
        // The real failure mode: a diagnostic that helpfully dumps the environment.
        const string secret = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        var name = GitHubTokenProviderEnvironmentName();

        var previous = Environment.GetEnvironmentVariable(name);

        try
        {
            Environment.SetEnvironmentVariable(name, secret);

            var report = Compose(hasToken: true);

            Assert.DoesNotContain(secret, report);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [Fact]
    public void It_counts_installed_applications_rather_than_listing_them()
    {
        // What somebody has installed is their business.
        var report = Compose(installed: 7);

        Assert.Contains("7 application(s)", report);
    }

    [Fact]
    public void It_says_what_it_contains_and_that_has_to_stay_true()
    {
        var report = Compose();

        Assert.Contains("no tokens, credentials, file contents, search", report);
        Assert.Contains("github.com/Strict009/RepoDeck/issues", report);
    }

    [Fact]
    public void It_admits_the_paths_carry_a_user_name()
    {
        // Rather than claiming to be anonymous while printing C:\Users\Someone.
        var report = Compose();

        Assert.Contains("Windows user name", report);
    }

    [Fact]
    public void A_missing_log_folder_does_not_break_it()
    {
        Directory.Delete(_paths.Logs, recursive: true);

        var report = Compose();

        Assert.Contains("No log folder yet.", report);
    }

    [Fact]
    public async Task The_clipboard_refusing_is_not_a_crash()
    {
        var clipboard = new RecordingClipboardWriter { Succeeds = false };

        Assert.False(await clipboard.SetTextAsync("anything"));
    }

    private static string GitHubTokenProviderEnvironmentName() =>
        Services.GitHub.GitHubTokenProvider.EnvironmentVariableName;
}

/// <summary>
/// Self-update against the failures a real API produces.
/// </summary>
/// <remarks>
/// The rule being defended: RepoDeck must never claim a newer RepoDeck exists unless it can
/// establish that conservatively. Every uncertain answer here has to land on Unknown, and
/// Unknown must never be dressed up as "up to date" - telling somebody they are current when
/// RepoDeck simply could not tell is the same lie in a friendlier voice.
/// </remarks>
public class SelfUpdateAgainstRealFailuresTests
{
    private static SelfUpdateService Service(FakeGitHubClient github, string version = "0.1.1-alpha") =>
        new(github, NullAppLog.Instance, version);

    private static GitHubRelease Release(string tag) => new()
    {
        Id = tag.GetHashCode(),
        TagName = tag,
        Name = tag,
        HtmlUrl = $"https://github.com/Strict009/RepoDeck/releases/tag/{tag}",
        PublishedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };

    [Fact]
    public async Task A_network_failure_is_unknown_and_never_up_to_date()
    {
        var github = new FakeGitHubClient
        {
            ReleasesThrows = new GitHubApiException(
                GitHubErrorKind.Network, "RepoDeck could not reach GitHub.")
        };

        var result = await Service(github).CheckAsync();

        Assert.Equal(SelfUpdateState.Unknown, result.State);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task An_exhausted_rate_limit_is_unknown_and_never_up_to_date()
    {
        var github = new FakeGitHubClient
        {
            ReleasesThrows = new GitHubApiException(
                GitHubErrorKind.RateLimited, "GitHub's hourly allowance is used up.")
        };

        var result = await Service(github).CheckAsync();

        Assert.Equal(SelfUpdateState.Unknown, result.State);
        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
    }

    [Fact]
    public async Task A_missing_repository_is_unknown_rather_than_a_crash()
    {
        var github = new FakeGitHubClient
        {
            ReleasesThrows = new GitHubApiException(
                GitHubErrorKind.NotFound, "That project could not be found.")
        };

        var result = await Service(github).CheckAsync();

        Assert.Equal(SelfUpdateState.Unknown, result.State);
    }

    [Fact]
    public async Task An_unexpected_failure_is_caught_rather_than_thrown_at_the_user()
    {
        var github = new FakeGitHubClient
        {
            ReleasesThrows = new InvalidOperationException("something nobody predicted")
        };

        var result = await Service(github).CheckAsync();

        Assert.Equal(SelfUpdateState.Unknown, result.State);
    }

    [Fact]
    public async Task Cancelling_the_check_is_not_swallowed_into_a_wrong_answer()
    {
        // A cancelled check has no result. Reporting "up to date" because somebody pressed
        // Stop would be inventing an answer.
        var github = new FakeGitHubClient { Releases = [Release("v9.9.9")] };

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(github).CheckAsync(cancelled.Token));
    }

    [Fact]
    public async Task It_spends_exactly_one_request()
    {
        // RepoDeck spends the user's allowance on the software they are looking for, not
        // on checking itself.
        var github = new FakeGitHubClient { Releases = [Release("v0.1.1-alpha")] };

        await Service(github).CheckAsync();

        Assert.Equal(1, github.ReleaseCallCount);
    }

    [Fact]
    public async Task It_asks_about_RepoDeck_and_not_something_else()
    {
        var github = new FakeGitHubClient { Releases = [Release("v0.1.1-alpha")] };

        await Service(github).CheckAsync();

        Assert.Equal(RepoDeckProject.Owner, github.LastReleasesOwner);
        Assert.Equal(RepoDeckProject.Name, github.LastReleasesName);
    }

    // ---- The conservative rule ---------------------------------------------

    [Fact]
    public void A_version_that_cannot_be_ordered_is_never_offered_as_an_update()
    {
        // "0.1.1-alpha" against "0.1.1-build.7": same numbers, suffixes whose meaning
        // RepoDeck has no standing to compare. It must not guess in either direction.
        var result = SelfUpdateService.Evaluate("0.1.1-alpha", [Release("v0.1.1-build.7")]);

        Assert.NotEqual(SelfUpdateState.UpdateAvailable, result.State);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public void An_unorderable_release_does_not_mask_a_genuinely_newer_one()
    {
        var result = SelfUpdateService.Evaluate(
            "0.1.1-alpha", [Release("v0.1.1-build.7"), Release("v0.2.0")]);

        Assert.Equal(SelfUpdateState.UpdateAvailable, result.State);
        Assert.Equal("v0.2.0", result.LatestVersion);
    }

    [Fact]
    public void A_stable_release_is_offered_to_somebody_on_its_prerelease()
    {
        var result = SelfUpdateService.Evaluate("0.1.1-alpha", [Release("v0.1.1")]);

        Assert.Equal(SelfUpdateState.UpdateAvailable, result.State);
    }

    [Fact]
    public void A_prerelease_is_not_offered_to_somebody_on_a_finished_version()
    {
        // Somebody running 0.2.0 is not asking to be moved onto an alpha. RepoDeck applies
        // the same rule to itself that it applies to everybody else.
        var result = SelfUpdateService.Evaluate("0.2.0", [Release("v0.2.1-alpha")]);

        Assert.NotEqual(SelfUpdateState.UpdateAvailable, result.State);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public void A_prerelease_user_is_still_offered_the_next_prerelease()
    {
        var result = SelfUpdateService.Evaluate("0.1.1-alpha", [Release("v0.1.2-alpha")]);

        Assert.Equal(SelfUpdateState.UpdateAvailable, result.State);
    }

    [Fact]
    public void A_finished_version_is_still_offered_to_a_prerelease_user()
    {
        var result = SelfUpdateService.Evaluate("0.1.1-alpha", [Release("v0.2.0")]);

        Assert.Equal(SelfUpdateState.UpdateAvailable, result.State);
    }

    [Fact]
    public void Nothing_but_unreadable_tags_never_becomes_up_to_date()
    {
        var result = SelfUpdateService.Evaluate(
            "0.1.1-alpha", [Release("latest"), Release("nightly"), Release("stable")]);

        Assert.Equal(SelfUpdateState.Unknown, result.State);
    }

    [Fact]
    public void An_update_always_carries_somewhere_to_get_it()
    {
        // An offer with no destination is a button that cannot work.
        var result = SelfUpdateService.Evaluate("0.1.0-alpha", [Release("v0.2.0")]);

        Assert.Equal(SelfUpdateState.UpdateAvailable, result.State);
        Assert.False(string.IsNullOrWhiteSpace(result.ReleaseUrl));
    }

    [Fact]
    public void No_state_other_than_UpdateAvailable_ever_reports_having_an_update()
    {
        var results = new[]
        {
            SelfUpdateService.Evaluate("0.1.1-alpha", []),
            SelfUpdateService.Evaluate("0.1.1-alpha", [Release("v0.1.1-alpha")]),
            SelfUpdateService.Evaluate("0.1.1-alpha", [Release("v0.1.1-build.7")]),
            SelfUpdateService.Evaluate("unreadable", [Release("v9.9.9")]),
            SelfUpdate.NotChecked
        };

        foreach (var result in results)
        {
            Assert.False(result.HasUpdate, $"{result.State} must not report an update");
        }
    }
}

/// <summary>
/// Privacy of the diagnostic report, re-audited for the release candidate.
/// </summary>
/// <remarks>
/// The report is pasted into a public issue tracker by somebody who will not read it first.
/// Everything in it has to be safe on that assumption.
/// </remarks>
public class DiagnosticPrivacyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-privacy-" + Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;

    public DiagnosticPrivacyTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a temp folder */ }
    }

    [Fact]
    public void The_profile_folder_is_replaced_by_the_variable_that_stands_for_it()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var shortened = DiagnosticReport.Shorten(Path.Combine(local, "RepoDeck", "Logs"));

        Assert.StartsWith("%LOCALAPPDATA%", shortened);
        Assert.DoesNotContain(local, shortened);
    }

    [Fact]
    public void The_windows_user_name_does_not_survive_shortening()
    {
        var user = Environment.UserName;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Only meaningful when the profile actually carries the name, which is the usual case.
        if (!local.Contains(user, StringComparison.OrdinalIgnoreCase)) return;

        var shortened = DiagnosticReport.Shorten(Path.Combine(local, "RepoDeck"));

        Assert.DoesNotContain(user, shortened, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_path_outside_the_profile_is_left_alone()
    {
        // If RepoDeck has ended up somewhere unexpected, that is the thing the report
        // exists to show.
        const string elsewhere = @"D:\Portable\RepoDeck";

        Assert.Equal(elsewhere, DiagnosticReport.Shorten(elsewhere));
    }

    [Fact]
    public void A_real_report_carries_no_user_name()
    {
        var user = Environment.UserName;
        var real = new AppPaths();

        var report = DiagnosticReport.Compose(real, RateLimitStatus.Unknown, false, 3);

        if (real.Root.Contains(user, StringComparison.OrdinalIgnoreCase))
        {
            Assert.DoesNotContain(user, report, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_report_does_not_dump_the_environment()
    {
        // The lazy version of a diagnostic tool prints every environment variable and
        // publishes whatever happens to be in them.
        var report = DiagnosticReport.Compose(_paths, RateLimitStatus.Unknown, true, 1);

        Assert.DoesNotContain("PATH=", report);
        Assert.DoesNotContain("USERNAME", report);
        Assert.DoesNotContain(Environment.GetEnvironmentVariable("PATH") ?? "|||", report);
    }

    [Fact]
    public void The_promise_at_the_bottom_matches_what_is_above_it()
    {
        var report = DiagnosticReport.Compose(_paths, RateLimitStatus.Unknown, true, 2);

        Assert.Contains("no tokens, credentials, file contents, search", report);
        Assert.Contains("Windows user name", report);
    }
}

/// <summary>
/// The claim RepoDeck must never start making.
/// </summary>
/// <remarks>
/// RepoDeck has now been installed successfully on a machine that was not the one it was
/// built on. That is evidence the installer works. It is not evidence about any software
/// RepoDeck finds, and the temptation after a success is to let the language drift from
/// "RepoDeck can install this" towards "this is fine to install".
///
/// These exist so that drift fails a test rather than shipping.
/// </remarks>
public class SafetyLanguageTests
{
    [Fact]
    public void The_first_run_still_says_RepoDeck_cannot_judge_safety()
    {
        var onboarding = new OnboardingViewModel(
            new Services.Preferences.UserPreferences(
                new AppPaths(Path.Combine(Path.GetTempPath(), "rd-safety-" + Guid.NewGuid().ToString("N"))),
                NullAppLog.Instance));

        Assert.Contains("cannot tell you whether software is safe", onboarding.Caveat);
        Assert.Contains("is a recommendation", onboarding.Caveat, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apps_mode_says_it_is_not_judging_quality_or_safety()
    {
        // The mode that reorders results is the one most easily mistaken for a verdict.
        var discover = new DiscoverViewModel(
            new FakeGitHubClient(),
            new Services.Explanation.HeuristicRepositoryExplanationService(),
            new Services.Media.RepositoryMediaService(NullAppLog.Instance),
            NullAppLog.Instance);

        Assert.Contains("not judging quality or safety", discover.BrowseModeExplanation);
    }

    [Fact]
    public void Installability_is_described_as_a_fact_about_RepoDeck()
    {
        // "RepoDeck can install this" is a statement about RepoDeck. It must never be
        // phrased as a statement about the software.
        var discover = new DiscoverViewModel(
            new FakeGitHubClient(),
            new Services.Explanation.HeuristicRepositoryExplanationService(),
            new Services.Media.RepositoryMediaService(NullAppLog.Instance),
            NullAppLog.Instance);

        var text = discover.BrowseModeExplanation;

        foreach (var forbidden in new[] { "safe to install", "verified", "trusted", "we recommend" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_self_update_feature_makes_no_claim_about_other_software()
    {
        // Checking RepoDeck's own version says nothing about anything it installs.
        var result = SelfUpdateService.Evaluate("0.1.0-alpha", [new GitHubRelease
        {
            Id = 1,
            TagName = "v0.2.0",
            Name = "v0.2.0",
            HtmlUrl = "https://github.com/Strict009/RepoDeck/releases/tag/v0.2.0"
        }]);

        foreach (var forbidden in new[] { "safe", "trusted", "verified", "recommend" })
        {
            Assert.DoesNotContain(forbidden, result.Explanation, StringComparison.OrdinalIgnoreCase);
        }
    }
}
