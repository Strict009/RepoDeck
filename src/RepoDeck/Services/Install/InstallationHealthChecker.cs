using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public interface IInstallationHealthChecker
{
    InstallationHealth Check(ApplicationManifest manifest);

    /// <summary>Builds a plan to put a damaged installation back, or explains why it cannot.</summary>
    RepairPlan PlanRepair(ApplicationManifest manifest, InstallationHealth health);
}

/// <summary>
/// Confirms an installation's files are still where RepoDeck left them.
/// </summary>
/// <remarks>
/// Shallow on purpose. RepoDeck checks the things it recorded: the folder, the executable,
/// the top-level entries it created. It does not hash every file or look for tampering,
/// because it cannot distinguish an application writing its own settings from something
/// going wrong, and a check that reported every ordinary thing as damage would train
/// people to ignore it.
/// </remarks>
public sealed class InstallationHealthChecker : IInstallationHealthChecker
{
    private readonly AppPaths _paths;
    private readonly IAppLog _log;

    public InstallationHealthChecker(AppPaths paths, IAppLog log)
    {
        _paths = paths;
        _log = log;
    }

    public InstallationHealth Check(ApplicationManifest manifest)
    {
        // A downloaded file is not an installation and has no directory to be damaged.
        if (manifest.State == InstallationState.Downloaded) return InstallationHealth.Healthy(manifest.Id);

        var problems = new List<HealthProblem>();
        var explanations = new List<string>();

        string directory;

        try
        {
            directory = Path.GetFullPath(manifest.InstalledPath);
        }
        catch (Exception ex)
        {
            _log.Warn("Health", $"{manifest.Id} has an unusable recorded path: {ex.Message}");

            return new InstallationHealth
            {
                ApplicationId = manifest.Id,
                Problems = [HealthProblem.InconsistentRecord],
                Explanations = ["RepoDeck's record of where this lives is not a usable folder path."]
            };
        }

        // A record pointing outside the managed root was edited by hand or corrupted.
        // Repair does not fetch files into somewhere RepoDeck does not own.
        if (!ArchivePathGuard.IsInside(_paths.Apps, directory))
        {
            return new InstallationHealth
            {
                ApplicationId = manifest.Id,
                Problems = [HealthProblem.InconsistentRecord],
                Explanations =
                [
                    "RepoDeck's record says this lives outside its own folder, which it never does. "
                    + "The record has been changed by something other than RepoDeck."
                ]
            };
        }

        if (!Directory.Exists(directory))
        {
            problems.Add(HealthProblem.MissingDirectory);
            explanations.Add("The folder RepoDeck installed this into is gone.");

            // Nothing else can be checked once the folder has gone.
            return new InstallationHealth
            {
                ApplicationId = manifest.Id,
                Problems = problems,
                Explanations = explanations
            };
        }

        var isEmpty = !SafeEnumerate(directory).Any();

        if (isEmpty)
        {
            problems.Add(HealthProblem.EmptyDirectory);
            explanations.Add("The folder is there but everything inside it has gone.");
        }

        if (manifest.ExecutablePath is { Length: > 0 } executable && !File.Exists(executable))
        {
            problems.Add(HealthProblem.MissingExecutable);
            explanations.Add(
                $"The program itself, {manifest.ExecutableRelativePath}, is no longer there.");
        }

        if (!isEmpty)
        {
            var missing = manifest.OwnedEntries
                .Where(entry => entry.Length > 0)
                .Where(entry => !Exists(Path.Combine(directory, entry)))
                .Take(5)
                .ToList();

            if (missing.Count > 0)
            {
                problems.Add(HealthProblem.MissingOwnedEntries);
                explanations.Add(
                    $"{missing.Count} file(s) or folder(s) RepoDeck put there are missing: "
                    + string.Join(", ", missing) + ".");
            }
        }

        if (problems.Count == 0) return InstallationHealth.Healthy(manifest.Id);

        return new InstallationHealth
        {
            ApplicationId = manifest.Id,
            Problems = problems,
            Explanations = explanations
        };
    }

    public RepairPlan PlanRepair(ApplicationManifest manifest, InstallationHealth health)
    {
        if (health.IsHealthy)
        {
            return RepairPlan.NotPossible(manifest, health,
                "There is nothing wrong with this installation.");
        }

        if (!health.IsRepairable)
        {
            return RepairPlan.NotPossible(manifest, health,
                "RepoDeck's record of this installation is not one it can act on. Remove it and "
                + "install it again.");
        }

        // Repair reuses the release already recorded. Fetching a *newer* one would be an
        // update wearing a repair's clothes, and the user did not ask for that.
        if (manifest.AssetDownloadUrl is not { Length: > 0 })
        {
            return RepairPlan.NotPossible(manifest, health,
                "RepoDeck did not record where this was downloaded from, so it cannot fetch it "
                + "again. Install it again from its page.");
        }

        return new RepairPlan
        {
            Current = manifest,
            Health = health,
            ReleaseTag = manifest.ReleaseTag,
            AssetName = manifest.AssetName,
            AssetUrl = manifest.AssetDownloadUrl,
            AssetSize = manifest.AssetSize,
            Reasons =
            [
                .. health.Explanations,
                $"RepoDeck will fetch {manifest.DisplayVersion} again - the same version you "
                + "already had, not a newer one."
            ]
        };
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static IEnumerable<string> SafeEnumerate(string directory)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory);
        }
        catch
        {
            return [];
        }
    }
}
