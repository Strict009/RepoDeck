using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Update;

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
        var result = SelfUpdateService.Evaluate("0.2.0", [Release("v0.1.0-alpha")]);

        Assert.Equal(SelfUpdateState.UpToDate, result.State);
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

        Assert.Contains(_paths.Root, report);
        Assert.Contains(_paths.Logs, report);
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
