using CommunityToolkit.Mvvm.ComponentModel;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>
/// The system panel and status strip.
/// </summary>
/// <remarks>
/// Written for the person in front of the machine, not for someone debugging an API
/// client. The strip says READY, what is happening, how many applications are installed
/// and roughly how much GitHub allowance is left. The exact request counts live in a
/// tooltip, where anyone who wants them can find them and nobody else has to read them.
/// </remarks>
public sealed partial class MainWindowViewModel
{
    /// <summary>Shown beside the wordmark light.</summary>
    [ObservableProperty] private string _systemStateText = "READY";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    private bool _isBusy;

    [ObservableProperty] private string _activityText = "READY";

    public bool IsReady => !IsBusy;

    // ---- Connection -------------------------------------------------------
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnectionProblem))]
    [NotifyPropertyChangedFor(nameof(ShowSystemPanel))]
    private bool _isConnected = true;
    [ObservableProperty] private string _connectionText = "GitHub";

    // ---- Allowance --------------------------------------------------------
    [ObservableProperty] private bool _hasRateLimit;
    [ObservableProperty] private string _rateLimitLabel = "";
    [ObservableProperty] private double _rateLimitPercent;

    /// <summary>The precise numbers, for the tooltip only.</summary>
    [ObservableProperty] private string _rateLimitDetail = "RepoDeck has not contacted GitHub yet.";

    // ---- Library ----------------------------------------------------------
    [ObservableProperty] private string _libraryText = "";

    /// <summary>
    /// Keeps the strip honest when something is installed, updated or removed.
    /// </summary>
    /// <remarks>
    /// The store raises this from whichever thread did the work, and the strip is a bound
    /// property, so it has to come back to the UI thread before it is touched.
    /// </remarks>
    private void OnLibraryChanged() => _dispatcher.Post(RefreshLibraryCount);

    /// <summary>Refreshes the installed count shown in the status strip.</summary>
    public void RefreshLibraryCount()
    {
        var all = _services.InstalledApps.GetAll();

        var installed = all.Count(m => m.State != InstallationState.Downloaded);
        var downloaded = all.Count - installed;

        LibraryText = downloaded == 0
            ? $"{installed} installed"
            : $"{installed} installed · {downloaded} downloaded";
    }

    /// <summary>
    /// True only when the allowance is low or used up.
    /// </summary>
    /// <remarks>
    /// A healthy allowance used to occupy the sidebar permanently: a label reading
    /// ALLOWANCE OK and a fourteen-segment meter, below a SYSTEM heading, under the
    /// navigation. That is RepoDeck's accounting, shown to somebody who came to find
    /// software. It now appears only when it is about to matter - which is the only time it
    /// answers a question the user has.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSystemPanel))]
    private bool _allowanceNeedsAttention;

    public bool ShowConnectionProblem => !IsConnected;

    /// <summary>The sidebar foot appears only when it has something to warn about.</summary>
    public bool ShowSystemPanel => AllowanceNeedsAttention || ShowConnectionProblem;

    private void ApplyRateLimit(RateLimitStatus status)
    {
        var view = RateLimitPresentation.From(status);

        HasRateLimit = view.IsKnown;
        AllowanceNeedsAttention = view.IsKnown && view.IsLow;
        RateLimitLabel = view.Label;
        RateLimitPercent = view.Percent;
        RateLimitDetail = view.Detail;
        RateLimitText = view.Warning;

        if (view.IsKnown) IsConnected = true;
    }

    /// <summary>Reports what the application is doing, for the status strip.</summary>
    public void ReportActivity(string activity, bool busy)
    {
        ActivityText = busy ? activity.ToUpperInvariant() : "READY";
        SystemStateText = busy ? "WORKING" : "READY";
        IsBusy = busy;
    }
}
