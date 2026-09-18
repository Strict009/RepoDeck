using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Services.GitHub;

namespace RepoDeck.ViewModels;

/// <summary>
/// Diagnostics and configuration. Real information only - no settings that do nothing.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly IUiDispatcher _dispatcher;

    public SettingsViewModel(AppServices services, IUiDispatcher? dispatcher = null)
    {
        _services = services;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();
        RefreshRateLimit();
        RefreshActivity();

        // Both are raised on whichever thread finished the work, so marshal before binding.
        _services.GitHub.RateLimitChanged += _ => _dispatcher.Post(RefreshRateLimit);
        _services.History.Changed += () => _dispatcher.Post(RefreshActivity);
    }

    public string DataFolder => _services.Paths.Root;
    public string LogFolder => _services.Paths.Logs;
    public string PlatformText => PlatformInfo.CurrentDescription;

    public bool HasToken => _services.Tokens.HasToken;

    public string TokenStatus => HasToken
        ? "A GitHub token was found, so RepoDeck can make far more requests per hour."
        : "No GitHub token configured. RepoDeck is limited to 60 requests an hour, "
          + "and 10 searches a minute.";

    public string TokenInstructions =>
        $"To raise the limits, set the {GitHubTokenProvider.EnvironmentVariableName} environment "
        + "variable to a GitHub personal access token and restart RepoDeck. A token with no scopes "
        + "is enough for searching public repositories. RepoDeck never writes the token to disk and "
        + "never records it in the log.";

    [ObservableProperty] private string _rateLimitText = "";

    // ---- What RepoDeck has done -------------------------------------------

    /// <summary>
    /// The activity list, newest first. Plain English only: these entries are written for
    /// somebody reading them, not for diagnosing a crash, so they carry no paths, no stack
    /// traces and nothing from a token.
    /// </summary>
    public ObservableCollection<ActivityEntryViewModel> Activity { get; } = [];

    public bool HasActivity => Activity.Count > 0;

    public string ActivitySummary => Activity.Count switch
    {
        0 => "Nothing yet. Installing, updating, repairing or removing something will show up here.",
        1 => "One thing so far.",
        _ => $"The last {Activity.Count} things, newest first."
    };

    [RelayCommand]
    private void ClearActivity()
    {
        _services.History.Clear();
        RefreshActivity();
    }

    private void RefreshActivity()
    {
        Activity.Clear();

        // Capped well below the store's own limit: this is a page somebody reads, not an
        // archive they search.
        foreach (var entry in _services.History.Recent(60))
        {
            Activity.Add(new ActivityEntryViewModel(entry));
        }

        OnPropertyChanged(nameof(HasActivity));
        OnPropertyChanged(nameof(ActivitySummary));
    }

    [RelayCommand]
    private void OpenDataFolder() => SystemBrowser.OpenFolder(_services.Paths.Root, _services.Log);

    [RelayCommand]
    private void OpenLogFolder() => SystemBrowser.OpenFolder(_services.Paths.Logs, _services.Log);

    private void RefreshRateLimit()
    {
        var limit = _services.GitHub.RateLimit;
        if (!limit.IsKnown)
        {
            RateLimitText = "RepoDeck has not contacted GitHub yet this session.";
            return;
        }

        RateLimitText = $"{limit.Remaining} of {limit.Limit} requests remaining."
                        + DescribeReset(limit.ResetsAt);
    }

    private static string DescribeReset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null) return "";

        var wait = resetsAt.Value - DateTimeOffset.UtcNow;
        if (wait <= TimeSpan.Zero) return " The allowance has just reset.";
        if (wait.TotalMinutes < 1) return " The allowance resets in less than a minute.";

        var minutes = Math.Ceiling(wait.TotalMinutes).ToString("0", CultureInfo.InvariantCulture);
        return $" The allowance resets in about {minutes} minutes.";
    }
}
