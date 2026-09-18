namespace RepoDeck.Models;

/// <summary>Where an update has got to.</summary>
/// <remarks>
/// PREPARE and REPLACE replace the install pipeline's Extracting and Registering for a
/// user watching an update: what matters to them is that the new version is being got
/// ready, and then that it is being swapped in.
/// </remarks>
public enum UpdateStage
{
    NotStarted,
    Downloading,
    Verifying,
    Preparing,
    Replacing,
    Finished,
    RollingBack,
    Failed,
    Cancelled
}

/// <summary>Progress of an update as a whole.</summary>
public sealed record UpdateProgress
{
    public UpdateStage Stage { get; init; } = UpdateStage.NotStarted;
    public DownloadProgress? Download { get; init; }
    public string? Detail { get; init; }

    public string Describe() => Stage switch
    {
        UpdateStage.Downloading => Download?.Percent is { } percent
            ? $"Downloading {percent}%"
            : "Downloading the new version...",
        UpdateStage.Verifying => "Checking the download...",
        UpdateStage.Preparing => "Preparing the new version...",
        UpdateStage.Replacing => "Replacing your copy...",
        UpdateStage.RollingBack => "Putting your previous version back...",
        UpdateStage.Finished => "Updated",
        UpdateStage.Failed => "Update failed",
        UpdateStage.Cancelled => "Update cancelled",
        _ => ""
    };
}

/// <summary>What happened when an update was carried out.</summary>
/// <remarks>
/// <see cref="PreviousInstallationRestored"/> is the one that matters after a failure. It
/// is the difference between "the update did not work" and "the update did not work and
/// now you have nothing", and the user is told which of those happened.
/// </remarks>
public sealed record UpdateResult
{
    public bool Succeeded { get; init; }
    public bool WasCancelled { get; init; }

    /// <summary>The manifest now in force: the new one on success, the old one after a rollback.</summary>
    public ApplicationManifest? Manifest { get; init; }

    /// <summary>True when the update failed and the previous version was put back.</summary>
    public bool PreviousInstallationRestored { get; init; }

    /// <summary>
    /// True when the update failed and RepoDeck could not restore the previous version
    /// either. Rare, serious, and never glossed over.
    /// </summary>
    public bool InstallationDamaged { get; init; }

    /// <summary>
    /// True when the application was running and the update was not attempted at all.
    /// Nothing was downloaded, nothing was touched, and Retry is offered.
    /// </summary>
    public bool ApplicationWasRunning { get; init; }

    /// <summary>Plain English, safe to show verbatim.</summary>
    public string? Message { get; init; }

    /// <summary>True when the executable had to be chosen again after the update.</summary>
    public bool ExecutableIsAmbiguous { get; init; }

    public static UpdateResult Failed(string message, bool restored = false) => new()
    {
        Succeeded = false,
        PreviousInstallationRestored = restored,
        Message = message
    };

    public static UpdateResult Cancelled(bool restored = false) => new()
    {
        Succeeded = false,
        WasCancelled = true,
        PreviousInstallationRestored = restored,
        Message = restored
            ? "Update cancelled. Your previous version is still installed."
            : "Update cancelled. Nothing was changed."
    };

    public static UpdateResult Running(string applicationName) => new()
    {
        Succeeded = false,
        ApplicationWasRunning = true,
        Message = $"Close {applicationName} before updating it."
    };
}
