using System.Text.Json.Serialization;

namespace RepoDeck.Models;

/// <summary>What kind of thing happened.</summary>
public enum LifecycleEventKind
{
    DownloadStarted,
    DownloadCompleted,
    DownloadFailed,
    InstallStarted,
    InstallCompleted,
    InstallFailed,
    Launched,
    UpdateChecked,
    UpdateStarted,
    UpdateCompleted,
    UpdateFailed,
    UpdateCancelled,
    RollbackPerformed,
    Repaired,
    RepairFailed,
    Uninstalled
}

/// <summary>
/// One thing RepoDeck did, recorded so the user can see what has happened to their
/// software.
/// </summary>
/// <remarks>
/// This is a user-facing history, not a log. The log file already exists and is the right
/// place for stack traces, request URLs and byte counts; this is the short answer to "what
/// has RepoDeck been doing to my machine?", written in the same plain English as the rest
/// of the interface.
///
/// Consequently it records nothing that would be unsafe or useless to show somebody: no
/// tokens, no authentication headers, no full paths beyond the managed root, no exception
/// detail. A failure records that it failed and the sentence the user was already shown.
/// </remarks>
public sealed record LifecycleEvent
{
    public required LifecycleEventKind Kind { get; init; }
    public required DateTimeOffset At { get; init; }

    /// <summary>owner/name, so an entry can be tied back to an application.</summary>
    public string? ApplicationId { get; init; }

    /// <summary>The name a person would recognise.</summary>
    public string? ApplicationName { get; init; }

    /// <summary>The release involved, when one was.</summary>
    public string? Version { get; init; }

    /// <summary>For an update: what it moved from.</summary>
    public string? FromVersion { get; init; }

    /// <summary>One plain sentence. Safe to show verbatim.</summary>
    public string Summary { get; init; } = "";

    [JsonIgnore]
    public bool IsFailure => Kind is
        LifecycleEventKind.DownloadFailed or LifecycleEventKind.InstallFailed
        or LifecycleEventKind.UpdateFailed or LifecycleEventKind.RepairFailed;

    [JsonIgnore]
    public bool IsNoteworthy => Kind is
        LifecycleEventKind.RollbackPerformed or LifecycleEventKind.Repaired
        or LifecycleEventKind.Uninstalled || IsFailure;

    // ---- Constructors for the things that happen -------------------------

    public static LifecycleEvent InstallStarted(InstallPlan plan) => new()
    {
        Kind = LifecycleEventKind.InstallStarted,
        At = DateTimeOffset.UtcNow,
        ApplicationId = plan.FullName,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(plan.Name),
        Version = plan.ReleaseTag,
        Summary = $"Started installing {plan.ReleaseTag}."
    };

    public static LifecycleEvent InstallCompleted(ApplicationManifest manifest) => new()
    {
        Kind = LifecycleEventKind.InstallCompleted,
        At = DateTimeOffset.UtcNow,
        ApplicationId = manifest.Id,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(manifest.Name),
        Version = manifest.ReleaseTag,
        Summary = $"Installed {manifest.DisplayVersion}."
    };

    public static LifecycleEvent InstallFailed(InstallPlan plan, string reason) => new()
    {
        Kind = LifecycleEventKind.InstallFailed,
        At = DateTimeOffset.UtcNow,
        ApplicationId = plan.FullName,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(plan.Name),
        Version = plan.ReleaseTag,
        Summary = reason
    };

    public static LifecycleEvent Launched(ApplicationManifest manifest) => new()
    {
        Kind = LifecycleEventKind.Launched,
        At = DateTimeOffset.UtcNow,
        ApplicationId = manifest.Id,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(manifest.Name),
        Version = manifest.ReleaseTag,
        Summary = "Started it."
    };

    public static LifecycleEvent UpdateChecked(ApplicationManifest manifest, UpdateCheck check) => new()
    {
        Kind = LifecycleEventKind.UpdateChecked,
        At = DateTimeOffset.UtcNow,
        ApplicationId = manifest.Id,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(manifest.Name),
        Version = manifest.ReleaseTag,
        Summary = check.Explanation
    };

    public static LifecycleEvent UpdateStarted(UpdatePlan plan) => new()
    {
        Kind = LifecycleEventKind.UpdateStarted,
        At = DateTimeOffset.UtcNow,
        ApplicationId = plan.FullName,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(plan.Name),
        FromVersion = plan.InstalledVersionText,
        Version = plan.TargetVersionText,
        Summary = $"Started updating {plan.InstalledVersionText} to {plan.TargetVersionText}."
    };

    public static LifecycleEvent UpdateCompleted(UpdatePlan plan) => new()
    {
        Kind = LifecycleEventKind.UpdateCompleted,
        At = DateTimeOffset.UtcNow,
        ApplicationId = plan.FullName,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(plan.Name),
        FromVersion = plan.InstalledVersionText,
        Version = plan.TargetVersionText,
        Summary = $"Updated to {plan.TargetVersionText}."
    };

    public static LifecycleEvent UpdateFailed(UpdatePlan plan, string reason) => new()
    {
        Kind = LifecycleEventKind.UpdateFailed,
        At = DateTimeOffset.UtcNow,
        ApplicationId = plan.FullName,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(plan.Name),
        FromVersion = plan.InstalledVersionText,
        Version = plan.TargetVersionText,
        Summary = reason
    };

    public static LifecycleEvent UpdateCancelled(UpdatePlan plan) => new()
    {
        Kind = LifecycleEventKind.UpdateCancelled,
        At = DateTimeOffset.UtcNow,
        ApplicationId = plan.FullName,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(plan.Name),
        Version = plan.TargetVersionText,
        Summary = "Update cancelled."
    };

    public static LifecycleEvent RollbackPerformed(UpdatePlan plan) => new()
    {
        Kind = LifecycleEventKind.RollbackPerformed,
        At = DateTimeOffset.UtcNow,
        ApplicationId = plan.FullName,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(plan.Name),
        FromVersion = plan.TargetVersionText,
        Version = plan.InstalledVersionText,
        Summary = $"The update failed, so RepoDeck put {plan.InstalledVersionText} back."
    };

    public static LifecycleEvent Repaired(ApplicationManifest manifest) => new()
    {
        Kind = LifecycleEventKind.Repaired,
        At = DateTimeOffset.UtcNow,
        ApplicationId = manifest.Id,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(manifest.Name),
        Version = manifest.ReleaseTag,
        Summary = $"Reinstalled {manifest.DisplayVersion} to repair it."
    };

    public static LifecycleEvent RepairFailed(ApplicationManifest manifest, string reason) => new()
    {
        Kind = LifecycleEventKind.RepairFailed,
        At = DateTimeOffset.UtcNow,
        ApplicationId = manifest.Id,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(manifest.Name),
        Version = manifest.ReleaseTag,
        Summary = reason
    };

    public static LifecycleEvent Uninstalled(ApplicationManifest manifest) => new()
    {
        Kind = LifecycleEventKind.Uninstalled,
        At = DateTimeOffset.UtcNow,
        ApplicationId = manifest.Id,
        ApplicationName = Infrastructure.FriendlyNaming.ForRepository(manifest.Name),
        Version = manifest.ReleaseTag,
        Summary = "Removed it."
    };
}
