using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public sealed partial class InstallationService
{
    /// <summary>
    /// The folder under Apps where installations are assembled before they count.
    /// Named with a leading dot so it sorts away from real applications and reads as
    /// clearly not one.
    /// </summary>
    internal const string StagingFolderName = ".staging";

    /// <summary>
    /// A scratch directory for one installation attempt. Disposing it removes whatever is
    /// left, so an abandoned attempt cleans itself up whatever went wrong - including a
    /// thrown exception on a path nobody thought about.
    /// </summary>
    private sealed class StagingArea : IDisposable
    {
        private readonly InstallationService _owner;
        private bool _promoted;

        public StagingArea(InstallationService owner, string path)
        {
            _owner = owner;
            Path = path;
        }

        public string Path { get; }

        /// <summary>Called once the contents have been moved to their real home.</summary>
        public void MarkPromoted() => _promoted = true;

        public void Dispose()
        {
            if (_promoted) return;

            try
            {
                if (Directory.Exists(Path)) _owner.DeleteManagedDirectory(Path);
            }
            catch (Exception ex)
            {
                _owner._log.Warn("Install", $"Could not clean up staging at {Path}: {ex.Message}");
            }
        }
    }

    private StagingArea CreateStagingArea()
    {
        var root = System.IO.Path.Combine(_paths.Apps, StagingFolderName);
        Directory.CreateDirectory(root);

        var path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        return new StagingArea(this, path);
    }

    /// <summary>
    /// Moves a completed staging folder into the application's real directory.
    /// </summary>
    /// <remarks>
    /// Staging lives inside Apps, so this is a rename on the same volume rather than a
    /// copy. Any previous installation is moved aside first and deleted afterwards, which
    /// keeps the window where neither version exists as short as the file system allows -
    /// and means a failure part-way leaves the old installation recoverable rather than
    /// destroyed.
    /// </remarks>
    private string PromoteStaging(StagingArea staging, InstallPlan plan)
    {
        var final = ResolveManagedDirectory(plan);
        var displaced = final + ".replacing-" + Guid.NewGuid().ToString("N")[..8];

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(final)!);

        var hadPrevious = Directory.Exists(final);
        if (hadPrevious) Directory.Move(final, displaced);

        try
        {
            Directory.Move(staging.Path, final);
            staging.MarkPromoted();
        }
        catch
        {
            // Put the previous installation back rather than leaving the user with neither.
            if (hadPrevious && Directory.Exists(displaced) && !Directory.Exists(final))
            {
                Directory.Move(displaced, final);
            }

            throw;
        }

        if (hadPrevious)
        {
            try
            {
                DeleteManagedDirectory(displaced);
            }
            catch (Exception ex)
            {
                _log.Warn("Install", $"Replaced installation left behind at {displaced}: {ex.Message}");
            }
        }

        return final;
    }

    /// <summary>
    /// Works out where this application belongs, having confirmed the plan's proposal is
    /// really inside Apps and is not the staging folder itself.
    /// </summary>
    private string ResolveManagedDirectory(InstallPlan plan)
    {
        var directory = System.IO.Path.GetFullPath(plan.ProposedInstallDirectory);

        if (!ArchivePathGuard.IsInside(_paths.Apps, directory))
        {
            throw new InvalidOperationException(
                $"Refused to install outside RepoDeck's managed folder: {directory}");
        }

        var stagingRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(_paths.Apps, StagingFolderName));

        if (ArchivePathGuard.IsInside(stagingRoot, directory)
            || string.Equals(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                stagingRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refused to install into RepoDeck's staging folder.");
        }

        return directory;
    }

    /// <summary>
    /// Removes staging folders left by a previous run that was interrupted. Called at
    /// startup: an installation that never promoted is not an installation, and its
    /// remains should not accumulate.
    /// </summary>
    public int CleanAbandonedStaging()
    {
        var root = Path.Combine(_paths.Apps, StagingFolderName);
        if (!Directory.Exists(root)) return 0;

        var removed = 0;

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            try
            {
                DeleteManagedDirectory(directory);
                removed++;
            }
            catch (Exception ex)
            {
                _log.Warn("Install", $"Could not remove abandoned staging {directory}: {ex.Message}");
            }
        }

        // Displaced copies from an interrupted promotion are also dead weight.
        foreach (var directory in Directory.EnumerateDirectories(_paths.Apps, "*.replacing-*"))
        {
            try
            {
                DeleteManagedDirectory(directory);
                removed++;
            }
            catch (Exception ex)
            {
                _log.Warn("Install", $"Could not remove displaced installation {directory}: {ex.Message}");
            }
        }

        if (removed > 0)
        {
            _log.Info("Install", $"Cleared {removed} abandoned installation folder(s) from a previous run.");
        }

        return removed;
    }
}
