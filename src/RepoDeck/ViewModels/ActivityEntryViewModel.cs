using Avalonia.Media;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>One thing RepoDeck did, as the activity list shows it.</summary>
/// <remarks>
/// Everything here is already plain English by the time it arrives: the summaries are
/// written when the event is recorded, for somebody to read. Nothing is reformatted from a
/// log line, and nothing carries a path, a URL or a token, so every field is safe to put on
/// screen verbatim.
/// </remarks>
public sealed class ActivityEntryViewModel
{
    public ActivityEntryViewModel(LifecycleEvent entry)
    {
        Entry = entry;
    }

    public LifecycleEvent Entry { get; }

    public string Name => Entry.ApplicationName ?? "RepoDeck";
    public string Summary => Entry.Summary;
    public string WhenText => Humanize.RelativeTime(Entry.At);

    /// <summary>
    /// A word for what happened, so the kind of event never depends on noticing a colour.
    /// </summary>
    public string KindText => Entry.Kind switch
    {
        LifecycleEventKind.InstallStarted => "INSTALLING",
        LifecycleEventKind.InstallCompleted => "INSTALLED",
        LifecycleEventKind.InstallFailed => "FAILED",
        LifecycleEventKind.DownloadFailed => "FAILED",
        LifecycleEventKind.UpdateChecked => "CHECKED",
        LifecycleEventKind.UpdateStarted => "UPDATING",
        LifecycleEventKind.UpdateCompleted => "UPDATED",
        LifecycleEventKind.UpdateFailed => "FAILED",
        LifecycleEventKind.UpdateCancelled => "STOPPED",
        LifecycleEventKind.RollbackPerformed => "PUT BACK",
        LifecycleEventKind.Repaired => "REPAIRED",
        LifecycleEventKind.RepairFailed => "FAILED",
        LifecycleEventKind.Uninstalled => "REMOVED",
        LifecycleEventKind.Launched => "STARTED",
        _ => "NOTED"
    };

    /// <summary>
    /// Failures are marked, and so are the events somebody would want to spot while
    /// scrolling - a rollback or a removal. The word above says so too, so the marking is
    /// emphasis rather than the only signal.
    /// </summary>
    public bool IsFailure => Entry.IsFailure;

    public bool IsNoteworthy => Entry.IsNoteworthy && !Entry.IsFailure;
}
