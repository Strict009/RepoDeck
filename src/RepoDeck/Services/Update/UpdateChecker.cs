using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.GitHub;

namespace RepoDeck.Services.Update;

public interface IUpdateChecker
{
    /// <summary>
    /// Asks whether a newer release exists. Never touches the installation.
    /// </summary>
    Task<UpdateCheck> CheckAsync(
        ApplicationManifest manifest, CancellationToken cancellationToken = default);
}

/// <summary>
/// Works out whether an installed application has a newer release worth offering.
/// </summary>
/// <remarks>
/// Conservative by construction, because the cost of being wrong is asymmetric: failing to
/// notice an update is a mild annoyance, whereas offering an "update" that is older than
/// what somebody has, or that will not run on their machine, damages the one thing
/// RepoDeck is for.
///
/// So: stable releases only unless the installed version is itself a pre-release; both
/// tags must parse as versions RepoDeck understands or the answer is Unknown; and the
/// newer release must actually carry an asset this machine can use, or the answer is
/// "update it yourself" with a link rather than a button that cannot work.
///
/// Nothing here writes anything. The result is returned and held in memory - an answer
/// about a remote release goes stale, and a stale answer stored in the manifest as fact
/// would be worse than no answer.
/// </remarks>
public sealed class UpdateChecker : IUpdateChecker
{
    /// <summary>Enough to find a newer stable release past a run of pre-releases.</summary>
    private const int ReleasesToInspect = 15;

    private readonly IGitHubClient _github;
    private readonly MachineProfile _machine;
    private readonly IAppLog _log;
    private readonly TimeProvider _time;

    public UpdateChecker(
        IGitHubClient github, MachineProfile machine, IAppLog log, TimeProvider? timeProvider = null)
    {
        _github = github;
        _machine = machine;
        _log = log;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<UpdateCheck> CheckAsync(
        ApplicationManifest manifest, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GitHubRelease> releases;

        try
        {
            releases = await _github
                .GetReleasesAsync(manifest.Owner, manifest.Name, ReleasesToInspect, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GitHubApiException ex)
        {
            _log.Warn("Update", $"Could not check {manifest.Id}: {ex.Message}");
            return UpdateCheck.Undetermined(
                "RepoDeck could not reach GitHub to check for updates.", ex.UserMessage);
        }
        catch (Exception ex)
        {
            _log.Error("Update", $"Unexpected failure checking {manifest.Id}", ex);
            return UpdateCheck.Undetermined("Something went wrong checking for updates.");
        }

        return Evaluate(manifest, releases, _machine, _time.GetUtcNow());
    }

    /// <summary>
    /// The decision, separated from the fetching so it can be tested against every awkward
    /// shape of release list without a network.
    /// </summary>
    public static UpdateCheck Evaluate(
        ApplicationManifest manifest,
        IReadOnlyList<GitHubRelease> releases,
        MachineProfile machine,
        DateTimeOffset now)
    {
        var reasons = new List<string>();

        var usable = releases.Where(r => !r.Draft).ToList();

        if (usable.Count == 0)
        {
            return new UpdateCheck
            {
                State = UpdateState.Unknown,
                CheckedAt = now,
                Explanation = "This project has no releases to compare against.",
                Reasons = ["GitHub returned no published releases for this project."]
            };
        }

        var installedVersion = ReleaseVersion.TryParse(manifest.ReleaseTag);

        if (installedVersion is null)
        {
            // Without a readable installed version there is nothing to compare to. Saying
            // so is the only honest answer: "newer" is meaningless against an unknown.
            return new UpdateCheck
            {
                State = UpdateState.Unknown,
                CheckedAt = now,
                Explanation = "RepoDeck cannot tell which version you have, so it cannot tell "
                              + "whether a newer one exists.",
                Reasons =
                [
                    manifest.ReleaseTag is { Length: > 0 }
                        ? $"The installed release is tagged \"{manifest.ReleaseTag}\", which is not a "
                          + "version number RepoDeck knows how to compare."
                        : "The installed release has no version tag recorded.",
                    "Check the project's releases page yourself to see what is current."
                ]
            };
        }

        // Pre-releases are opt-in by consequence rather than by setting: somebody already
        // running one is presumably following them, and somebody on a stable release is
        // not offered a beta.
        var installedIsPrerelease = manifest.IsPrerelease || installedVersion.LooksLikePrerelease;

        var candidates = usable
            .Where(r => installedIsPrerelease || r.IsStable)
            .Select(r => (Release: r, Version: ReleaseVersion.TryParse(r.TagName)))
            .ToList();

        var unreadable = candidates.Count(c => c.Version is null);
        var readable = candidates.Where(c => c.Version is not null).ToList();

        if (readable.Count == 0)
        {
            return new UpdateCheck
            {
                State = UpdateState.Unknown,
                InstalledVersion = installedVersion,
                CheckedAt = now,
                Explanation = "This project's release names are not version numbers RepoDeck can "
                              + "compare, so it cannot tell whether a newer one exists.",
                Reasons =
                [
                    $"None of the {candidates.Count} release(s) RepoDeck looked at is tagged with a "
                    + "version number it understands.",
                    "Check the project's releases page yourself to see what is current."
                ]
            };
        }

        if (unreadable > 0)
        {
            reasons.Add($"{unreadable} release(s) were skipped because their names are not "
                        + "version numbers RepoDeck can compare.");
        }

        // Only releases RepoDeck can show are newer are candidates. A release it cannot
        // order against the installed one is not evidence of anything, in either
        // direction, and is dealt with below.
        var newer = readable
            .Where(c => c.Version!.IsNewerThan(installedVersion))
            .ToList();

        if (newer.Count == 0)
        {
            var ambiguous = readable
                .Where(c => !c.Version!.CanCompareWith(installedVersion))
                .ToList();

            if (ambiguous.Count > 0)
            {
                // Same numbers, different suffix. "v0.0.3" against "v0.0.3-release.4" is
                // the case that matters: semantic versioning says the plain one wins, but
                // "release.4" is a build number, and treating it as a pre-release would
                // offer a downgrade with the word "update" on the button.
                var closest = ambiguous
                    .OrderByDescending(c => c.Version!, NumericOnly.Instance)
                    .ThenByDescending(c => c.Release.PublishedAt ?? c.Release.CreatedAt)
                    .First();

                return new UpdateCheck
                {
                    State = UpdateState.Unknown,
                    InstalledVersion = installedVersion,
                    LatestRelease = closest.Release,
                    LatestVersion = closest.Version,
                    CheckedAt = now,
                    Explanation = $"RepoDeck cannot tell whether {closest.Version} is newer than "
                                  + $"the {installedVersion} you have.",
                    Reasons =
                    [
                        .. reasons,
                        $"\"{closest.Version}\" and \"{installedVersion}\" are the same version "
                        + "number with different wording after it, and that wording means "
                        + "different things in different projects.",
                        "RepoDeck will not offer an update it cannot show is newer. Check the "
                        + "project's releases page yourself if you think you are behind."
                    ]
                };
            }

            var latest = readable.MaxBy(c => c.Version!)!;

            return new UpdateCheck
            {
                State = UpdateState.UpToDate,
                InstalledVersion = installedVersion,
                LatestRelease = latest.Release,
                LatestVersion = latest.Version,
                CheckedAt = now,
                Explanation = installedIsPrerelease
                    ? $"You have {installedVersion}, which is the newest RepoDeck can see."
                    : $"You have {installedVersion}, which is the newest stable release.",
                Reasons = [.. reasons, $"The newest release RepoDeck found is {latest.Version}."]
            };
        }

        // Which of several newer releases to offer. Highest numbers win, as they should.
        // When the numbers tie and only the suffix differs - "v0.0.3-release.4" against
        // "v0.0.3-release.3-patch.1" - the suffix decides nothing RepoDeck can defend, so
        // the publication date decides instead. That is evidence about which the project
        // shipped last, rather than a guess about what its naming means.
        var newest = newer
            .OrderByDescending(c => c.Version!, NumericOnly.Instance)
            .ThenByDescending(c => c.Release.PublishedAt ?? c.Release.CreatedAt)
            .First();

        // There is something newer. Whether RepoDeck can install it is a separate question,
        // and answering it wrongly means a button that cannot work.
        var analysis = ReleaseAnalyzer.Analyze([newest.Release], machine);
        var step = newest.Version!.StepFrom(installedVersion);

        reasons.Add($"{newest.Version} was published "
                    + Humanize.RelativeTime(newest.Release.PublishedAt ?? newest.Release.CreatedAt) + ".");

        if (analysis.Recommended is null)
        {
            return new UpdateCheck
            {
                State = UpdateState.ManualUpdateRequired,
                InstalledVersion = installedVersion,
                LatestRelease = newest.Release,
                LatestVersion = newest.Version,
                CheckedAt = now,
                Step = step,
                Explanation = $"{newest.Version} is available, but RepoDeck cannot install it for you.",
                Reasons =
                [
                    .. reasons,
                    analysis.NoRecommendationReason
                        ?? $"Nothing in release {newest.Release.TagName} suits {machine.Description}.",
                    "You can get it from the project's releases page yourself."
                ]
            };
        }

        reasons.Add($"It publishes {analysis.Recommended.Name}, which suits {machine.Description}.");

        return new UpdateCheck
        {
            State = UpdateState.UpdateAvailable,
            InstalledVersion = installedVersion,
            LatestRelease = newest.Release,
            LatestVersion = newest.Version,
            CheckedAt = now,
            Step = step,
            AssetName = analysis.Recommended.Name,
            AssetSize = analysis.Recommended.Size,
            Explanation = $"{newest.Version} is available. You have {installedVersion}.",
            Reasons = reasons
        };
    }
}

/// <summary>Orders release versions by their numbers alone, ignoring any suffix.</summary>
file sealed class NumericOnly : IComparer<ReleaseVersion>
{
    public static NumericOnly Instance { get; } = new();

    public int Compare(ReleaseVersion? x, ReleaseVersion? y) =>
        x is null ? (y is null ? 0 : -1)
        : y is null ? 1
        : x.CompareNumericTo(y);
}
