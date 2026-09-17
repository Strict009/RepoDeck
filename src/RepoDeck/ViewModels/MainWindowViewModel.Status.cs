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
    [ObservableProperty] private bool _isConnected = true;
    [ObservableProperty] private string _connectionText = "GitHub";

    // ---- Allowance --------------------------------------------------------
    [ObservableProperty] private bool _hasRateLimit;
    [ObservableProperty] private string _rateLimitLabel = "";
    [ObservableProperty] private double _rateLimitPercent;

    /// <summary>The precise numbers, for the tooltip only.</summary>
    [ObservableProperty] private string _rateLimitDetail = "RepoDeck has not contacted GitHub yet.";

    // ---- Library ----------------------------------------------------------
    [ObservableProperty] private string _libraryText = "";

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

    private void ApplyRateLimit(RateLimitStatus status)
    {
        var view = RateLimitPresentation.From(status);

        HasRateLimit = view.IsKnown;
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
