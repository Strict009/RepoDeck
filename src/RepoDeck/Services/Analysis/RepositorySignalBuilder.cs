using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Produces the factual signals shown in the repository information panel.
/// </summary>
/// <remarks>
/// Every signal is something directly observed in GitHub data. There is no aggregate
/// score and no "safe" badge: popularity is not safety, and RepoDeck says so explicitly.
/// </remarks>
public static class RepositorySignalBuilder
{
    public static IReadOnlyList<RepositorySignal> Build(RepositoryDetails details, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        var repository = details.Repository;
        var signals = new List<RepositorySignal>();

        if (repository.IsArchived)
        {
            signals.Add(new RepositorySignal(
                "The authors have archived this repository. It is read-only and no longer maintained.",
                SignalTone.Caution));
        }

        var lastActivity = repository.LastActivity;
        if (lastActivity is null)
        {
            signals.Add(new RepositorySignal(
                "RepoDeck could not determine when this was last worked on.", SignalTone.Neutral));
        }
        else
        {
            var age = reference - lastActivity.Value;
            var relative = Humanize.RelativeTime(lastActivity, reference);
            signals.Add(age.TotalDays > 730
                ? new RepositorySignal($"Last updated {relative}, so it may no longer work with current systems.", SignalTone.Caution)
                : age.TotalDays > 365
                    ? new RepositorySignal($"Last updated {relative}.", SignalTone.Caution)
                    : new RepositorySignal($"Actively maintained - last updated {relative}.", SignalTone.Favourable));
        }

        if (repository.CreatedAt is { } created)
        {
            var ageYears = (reference - created).TotalDays / 365.25;
            signals.Add(ageYears < 0.25
                ? new RepositorySignal(
                    $"Created {Humanize.RelativeTime(created, reference)} - a very new project with little track record.",
                    SignalTone.Caution)
                : new RepositorySignal(
                    $"First published {Humanize.RelativeTime(created, reference)}.", SignalTone.Neutral));
        }

        signals.Add(repository.HasLicense
            ? new RepositorySignal(
                $"Licensed under {repository.LicenseSpdxId ?? repository.LicenseName}, so the terms of use are stated.",
                SignalTone.Favourable)
            : new RepositorySignal(
                "No licence detected. The authors have not said how you are permitted to use this.",
                SignalTone.Caution));

        if (details.HasReleases)
        {
            var latest = details.LatestStableRelease ?? details.Releases[0];
            var assets = latest.Assets.Count;

            signals.Add(assets > 0
                ? new RepositorySignal(
                    $"Publishes ready-made downloads - release {latest.TagName} has {assets} attached file{(assets == 1 ? "" : "s")}.",
                    SignalTone.Favourable)
                : new RepositorySignal(
                    $"Release {latest.TagName} contains source code only, with nothing pre-built to download.",
                    SignalTone.Neutral));

            if (latest.Prerelease)
            {
                signals.Add(new RepositorySignal(
                    "The most recent release is marked as a pre-release and may be unfinished.", SignalTone.Caution));
            }
        }
        else
        {
            signals.Add(new RepositorySignal(
                "No published releases. Using this would mean building it from source yourself.",
                SignalTone.Caution));
        }

        if (repository.IsFork)
        {
            signals.Add(new RepositorySignal(
                "This is a fork of another repository rather than the original project.", SignalTone.Caution));
        }

        signals.Add(new RepositorySignal(
            $"{Humanize.Count(repository.Stars)} GitHub stars. Popularity is not a guarantee that software is safe.",
            SignalTone.Neutral));

        return signals;
    }
}
