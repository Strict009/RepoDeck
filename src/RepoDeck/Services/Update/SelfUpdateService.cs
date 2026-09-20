using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.GitHub;

namespace RepoDeck.Services.Update;

/// <summary>Where RepoDeck stands against its own published releases.</summary>
public enum SelfUpdateState
{
    NotChecked,
    UpToDate,
    UpdateAvailable,

    /// <summary>The check failed, or the versions could not be honestly compared.</summary>
    Unknown
}

/// <summary>What RepoDeck knows about a newer RepoDeck.</summary>
public sealed record SelfUpdate
{
    public SelfUpdateState State { get; init; } = SelfUpdateState.NotChecked;

    public string InstalledVersion { get; init; } = "";
    public string? LatestVersion { get; init; }

    /// <summary>The release page. Where the user goes; not something RepoDeck downloads.</summary>
    public string? ReleaseUrl { get; init; }

    /// <summary>
    /// The release notes, as plain text. Remote content, shown read-only and never
    /// rendered as markup.
    /// </summary>
    public string? ReleaseNotes { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>In plain English. Always populated for anything but NotChecked.</summary>
    public string Explanation { get; init; } = "";

    public bool HasUpdate => State == SelfUpdateState.UpdateAvailable;

    public static SelfUpdate NotChecked { get; } = new()
    {
        State = SelfUpdateState.NotChecked,
        InstalledVersion = AppVersion.Current,
        Explanation = "RepoDeck has not checked for a newer version of itself."
    };
}

public interface ISelfUpdateService
{
    Task<SelfUpdate> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Tells the user when a newer RepoDeck exists. Does not install it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a notification rather than an updater. RepoDeck's update transaction works
/// by moving the live installation aside and promoting a validated copy into its place, and
/// a running Windows process cannot have its own executable moved - so the mechanism
/// RepoDeck already owns cannot be pointed at RepoDeck. Building a second mechanism, a
/// separate updater process that outlives the exit, is a real piece of work with its own
/// failure modes, and shipping it half-done would mean the component responsible for
/// recovering from bad updates is itself the thing that breaks.
/// </para>
/// <para>
/// So this checks, says what it found, shows the release notes, and opens the release page.
/// The installer already handles upgrading in place correctly. That is the whole feature,
/// and it is honest about being the whole feature.
/// </para>
/// <para>
/// One request, only when asked. RepoDeck spends the user's GitHub allowance on the software
/// they are looking for, not on checking itself.
/// </para>
/// </remarks>
public sealed class SelfUpdateService : ISelfUpdateService
{
    /// <summary>Enough to find a newer release past a run of older ones.</summary>
    private const int ReleasesToInspect = 10;

    /// <summary>Release notes are remote text shown to a person, not a document.</summary>
    private const int MaxNotesLength = 2000;

    private readonly IGitHubClient _github;
    private readonly IAppLog _log;
    private readonly string _version;

    public SelfUpdateService(IGitHubClient github, IAppLog log, string? version = null)
    {
        _github = github;
        _log = log;
        _version = version ?? AppVersion.Current;
    }

    public async Task<SelfUpdate> CheckAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GitHubRelease> releases;

        try
        {
            releases = await _github
                .GetReleasesAsync(RepoDeckProject.Owner, RepoDeckProject.Name, ReleasesToInspect, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GitHubApiException ex)
        {
            _log.Warn("SelfUpdate", $"Could not check for a newer RepoDeck: {ex.Message}");

            return Undetermined("RepoDeck could not reach GitHub to check for a newer version.");
        }
        catch (Exception ex)
        {
            _log.Error("SelfUpdate", "Unexpected failure checking for a newer RepoDeck", ex);

            return Undetermined("Something went wrong checking for a newer version.");
        }

        return Evaluate(_version, releases);
    }

    /// <summary>
    /// The decision, separated from the fetching so every awkward shape of release list is
    /// testable without a network.
    /// </summary>
    public static SelfUpdate Evaluate(string installedVersion, IReadOnlyList<GitHubRelease> releases)
    {
        var installed = ReleaseVersion.TryParse(installedVersion);

        if (installed is null)
        {
            return new SelfUpdate
            {
                State = SelfUpdateState.Unknown,
                InstalledVersion = installedVersion,
                Explanation = $"RepoDeck cannot read its own version (\"{installedVersion}\"), "
                              + "so it cannot tell whether a newer one exists."
            };
        }

        var candidates = releases
            .Where(r => !r.Draft)
            .Select(r => (Release: r, Version: ReleaseVersion.TryParse(r.TagName)))
            .Where(c => c.Version is not null)
            .ToList();

        if (candidates.Count == 0)
        {
            return new SelfUpdate
            {
                State = SelfUpdateState.Unknown,
                InstalledVersion = installedVersion,
                Explanation = "RepoDeck found no published releases of itself to compare against."
            };
        }

        // Only releases RepoDeck can show are newer. The same conservative comparison it
        // applies to everything else applies to itself: a version it cannot order is not
        // an update.
        var newer = candidates
            .Where(c => c.Version!.IsNewerThan(installed))
            .OrderByDescending(c => c.Version!)
            .ToList();

        if (newer.Count == 0)
        {
            return new SelfUpdate
            {
                State = SelfUpdateState.UpToDate,
                InstalledVersion = installedVersion,
                LatestVersion = candidates.OrderByDescending(c => c.Version!).First().Version!.ToString(),
                Explanation = $"RepoDeck {installedVersion} is the newest version published."
            };
        }

        var latest = newer[0];

        return new SelfUpdate
        {
            State = SelfUpdateState.UpdateAvailable,
            InstalledVersion = installedVersion,
            LatestVersion = latest.Version!.ToString(),
            ReleaseUrl = latest.Release.HtmlUrl,
            ReleaseNotes = TrimNotes(latest.Release.Body),
            PublishedAt = latest.Release.PublishedAt ?? latest.Release.CreatedAt,
            Explanation = $"RepoDeck {latest.Version} is available. You have {installedVersion}."
        };
    }

    private SelfUpdate Undetermined(string explanation) => new()
    {
        State = SelfUpdateState.Unknown,
        InstalledVersion = _version,
        Explanation = explanation
    };

    /// <summary>
    /// Release notes are remote text of unknown length. Shown to a person, bounded, and
    /// never rendered as markup by the view that displays them.
    /// </summary>
    private static string? TrimNotes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        var text = body.Trim();
        return text.Length <= MaxNotesLength ? text : text[..MaxNotesLength] + "...";
    }
}

/// <summary>Where RepoDeck itself lives, in one place.</summary>
public static class RepoDeckProject
{
    public const string Owner = "Strict009";
    public const string Name = "RepoDeck";

    public const string Url = "https://github.com/Strict009/RepoDeck";
    public const string ReleasesUrl = Url + "/releases";
    public const string IssuesUrl = Url + "/issues";
}
