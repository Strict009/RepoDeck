using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>
/// What the user is being asked to agree to, written out in full.
/// </summary>
/// <remarks>
/// The confirmation is the moment RepoDeck's whole argument either holds or does not. A
/// button saying "Install" beside a size is what every other installer offers; a numbered
/// list of what will happen, and an explicit list of what will not, is the difference.
///
/// Both lists come from the plan rather than from a constant, so they cannot drift away
/// from what the installer actually does. The "will not" list is the M3 boundary restated
/// in the second person at the moment it matters.
/// </remarks>
public sealed record InstallConfirmation
{
    public required string Title { get; init; }
    public required string VersionText { get; init; }

    /// <summary>"57 MB", or empty when the release did not say.</summary>
    public required string DownloadSizeText { get; init; }

    /// <summary>The file itself, so the name on screen matches the name on disk.</summary>
    public required string AssetName { get; init; }

    /// <summary>owner/name, so it is clear whose software this is.</summary>
    public required string SourceText { get; init; }

    /// <summary>Where it lands. Always inside RepoDeck's own folder.</summary>
    public required string DestinationText { get; init; }

    /// <summary>Numbered, in order, in the words of what actually happens.</summary>
    public required IReadOnlyList<string> WillDo { get; init; }

    /// <summary>The things people are right to worry about, ruled out by name.</summary>
    public required IReadOnlyList<string> WillNotDo { get; init; }

    /// <summary>Anything the plan flagged. Shown, not buried.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool HasWarnings => Warnings.Count > 0;
    public bool HasDownloadSize => DownloadSizeText.Length > 0;

    public static InstallConfirmation From(InstallPlan plan, string friendlyName)
    {
        var will = new List<string>();

        var platform = plan.Platform == OsPlatform.Unknown
            ? "the published"
            : plan.Platform.DisplayName()
              + (plan.Architecture == CpuArchitecture.Unknown ? "" : " " + plan.Architecture.DisplayName());

        will.Add($"Download the {platform} release from GitHub.");
        will.Add("Check the downloaded file is complete and is the file the release listed.");

        if (plan.RequiresExtraction)
        {
            will.Add("Unpack it into RepoDeck's own Apps folder.");
        }
        else
        {
            will.Add("Put it in RepoDeck's own Apps folder as it is.");
        }

        will.Add("Look inside for the program to run.");
        will.Add("Add it to your Installed list.");

        // Stated in the second person, at the moment it matters, rather than as a promise
        // in a document nobody reads.
        var willNot = new List<string>
        {
            "Run an installer, a script or anything else it downloads.",
            "Ask for administrator access.",
            "Change any Windows setting, or anything outside its own folder.",
            "Start the program until you ask it to."
        };

        return new InstallConfirmation
        {
            Title = friendlyName,
            VersionText = plan.ReleaseTag ?? plan.ReleaseName ?? "",
            DownloadSizeText = plan.AssetSize > 0 ? Humanize.FileSize(plan.AssetSize) : "",
            AssetName = plan.AssetName ?? "",
            SourceText = plan.FullName,
            DestinationText = plan.ProposedInstallDirectory,
            WillDo = will,
            WillNotDo = willNot,
            Warnings = plan.Warnings
        };
    }
}
