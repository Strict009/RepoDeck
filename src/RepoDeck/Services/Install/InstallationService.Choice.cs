using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public sealed partial class InstallationService
{
    /// <summary>
    /// Settles which of an ambiguous installation's candidates is the application.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. The choice must be one of the candidates RepoDeck already
    /// recorded, and the resolved path is re-checked to sit inside the installation
    /// directory. Neither check alone would be enough: the candidate list could in
    /// principle be edited in the manifest file, and a relative path can climb out of a
    /// directory with enough dots. This is not a "browse for an executable" feature and
    /// must not become one by accident.
    /// </remarks>
    public ExecutableChoiceResult ChooseExecutable(ApplicationManifest manifest, string relativePath)
    {
        if (manifest.State != InstallationState.AwaitingExecutableChoice)
        {
            return ExecutableChoiceResult.Failed(
                "This installation is not waiting for a choice of program.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return ExecutableChoiceResult.Failed("No program was chosen.");
        }

        // Only something RepoDeck itself offered.
        var candidate = manifest.AlternativeExecutables
            .FirstOrDefault(c => string.Equals(c, relativePath, StringComparison.OrdinalIgnoreCase));

        if (candidate is null)
        {
            _log.Warn("Install",
                $"Refused a choice for {manifest.Id} that was not among the candidates: {relativePath}");

            return ExecutableChoiceResult.Failed(
                "That program was not one of the options RepoDeck found.");
        }

        // And only something that really is inside the folder RepoDeck owns.
        var resolved = ArchivePathGuard.ResolveSafePath(manifest.InstalledPath, candidate);

        if (resolved is null || !ArchivePathGuard.IsInside(_paths.Apps, resolved))
        {
            _log.Error("Install",
                $"Refused a choice for {manifest.Id} that resolves outside the installation: {candidate}");

            return ExecutableChoiceResult.Failed(
                "RepoDeck will only run a program from inside the application's own folder.");
        }

        if (!File.Exists(resolved))
        {
            return ExecutableChoiceResult.Failed("That program is no longer there.");
        }

        var updated = manifest with
        {
            State = InstallationState.Installed,
            ExecutableRelativePath = candidate,
            ExecutableIsAmbiguous = false,
            NotInstalledReason = null,

            // The alternatives are kept: they are provenance, and the choice may be wrong.
            AlternativeExecutables = manifest.AlternativeExecutables
        };

        _store.Save(updated);
        _log.Info("Install", $"{manifest.Id}: executable resolved to {candidate} by the user.");

        return new ExecutableChoiceResult { Succeeded = true, Manifest = updated };
    }
}
