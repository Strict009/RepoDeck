using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Update;

public sealed partial class UpdateService
{
    /// <summary>
    /// Puts a damaged installation back by fetching the release it already has.
    /// </summary>
    /// <remarks>
    /// Deliberately the same transaction as an update, because it is the same problem:
    /// assemble somewhere else, check it, swap, check again, keep a way back. The only
    /// difference is that the target release is the one already recorded rather than a
    /// newer one, so a repair can never quietly become an upgrade.
    ///
    /// Building an <see cref="UpdatePlan"/> for it rather than writing a second pipeline
    /// means a repair gets every protection an update has - including the rollback - and
    /// that there is one place where a managed installation is ever replaced.
    /// </remarks>
    public Task<UpdateResult> RepairAsync(
        RepairPlan repair,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!repair.CanProceed)
        {
            var reason = repair.BlockingIssues.Count > 0
                ? string.Join(" ", repair.BlockingIssues)
                : "RepoDeck cannot repair this installation.";

            _log.Warn("Repair", $"Refused to repair {repair.Current.Id}: {reason}");
            _history.Record(LifecycleEvent.RepairFailed(repair.Current, reason));

            return Task.FromResult(UpdateResult.Failed(reason, restored: true));
        }

        return RunRepairAsync(repair, progress, cancellationToken);
    }

    private async Task<UpdateResult> RunRepairAsync(
        RepairPlan repair,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var current = repair.Current;

        var plan = new UpdatePlan
        {
            Current = current,
            Owner = current.Owner,
            Name = current.Name,
            InstalledVersionText = current.DisplayVersion,

            // The same release, not a newer one. A repair that upgraded would be doing
            // something the user did not ask for at the moment they are least able to
            // notice.
            TargetReleaseTag = current.ReleaseTag,
            TargetReleaseName = current.ReleaseName,
            TargetReleaseId = current.ReleaseId,
            TargetPublishedAt = current.ReleasePublishedAt,
            TargetIsPrerelease = current.IsPrerelease,
            TargetVersionText = current.DisplayVersion,

            AssetName = repair.AssetName,
            AssetUrl = repair.AssetUrl,
            AssetSize = repair.AssetSize,

            Platform = current.Platform,
            Architecture = current.Architecture,
            PackageType = current.PackageType,

            Strategy = UpdateStrategy.ReplaceManagedInstallation,
            InstallStrategy = current.Strategy,

            RequiresApplicationClosed = current.IsRunnableInstallation,
            ProposedInstallDirectory = current.InstalledPath,
            ExecutableCandidates = ExpectedExecutables(current),

            Confidence = Confidence.Likely,
            Reasons = repair.Reasons,
            IsRepair = true
        };

        var result = await UpdateAsync(plan, progress, cancellationToken).ConfigureAwait(false);

        // The history says what actually happened rather than what was attempted: an
        // update entry for something the user asked to repair would read as a surprise.
        if (result.Succeeded)
        {
            _history.Record(LifecycleEvent.Repaired(result.Manifest ?? current));
        }
        else if (!result.WasCancelled && !result.ApplicationWasRunning)
        {
            _history.Record(LifecycleEvent.RepairFailed(current,
                result.Message ?? "The repair did not work."));
        }

        return result;
    }

    private static IReadOnlyList<string> ExpectedExecutables(ApplicationManifest manifest)
    {
        var names = new List<string>();

        if (manifest.ExecutableRelativePath is { Length: > 0 } existing)
        {
            names.Add(Path.GetFileName(existing));
        }

        foreach (var alternative in manifest.AlternativeExecutables)
        {
            var name = Path.GetFileName(alternative);
            if (name.Length > 0) names.Add(name);
        }

        if (manifest.AssetName is { Length: > 0 } asset && !manifest.PackageType.RequiresExtraction())
        {
            names.Add(asset);
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
