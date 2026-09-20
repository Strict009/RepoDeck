using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Services.GitHub;
using RepoDeck.Services.Update;

namespace RepoDeck.ViewModels;

/// <summary>
/// Diagnostics and configuration. Real information only - no settings that do nothing.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClipboardWriter _clipboard;

    public SettingsViewModel(
        AppServices services,
        IUiDispatcher? dispatcher = null,
        IClipboardWriter? clipboard = null)
    {
        _services = services;
        _dispatcher = dispatcher ?? new AvaloniaUiDispatcher();
        _clipboard = clipboard ?? new AvaloniaClipboardWriter();
        RefreshRateLimit();
        RefreshActivity();

        // Both are raised on whichever thread finished the work, so marshal before binding.
        _services.GitHub.RateLimitChanged += _ => _dispatcher.Post(RefreshRateLimit);
        _services.History.Changed += () => _dispatcher.Post(RefreshActivity);
    }

    public string DataFolder => _services.Paths.Root;
    public string LogFolder => _services.Paths.Logs;
    public string PlatformText => PlatformInfo.CurrentDescription;

    /// <summary>Which build this is, so a problem report can name it.</summary>
    public string VersionText => AppVersion.Description;

    // ---- A newer RepoDeck -------------------------------------------------

    /// <summary>
    /// What RepoDeck knows about a newer RepoDeck. A notification, never an installation:
    /// a running process cannot have its own executable replaced underneath it, so this
    /// checks, says what it found, and opens the release page.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelfUpdate))]
    [NotifyPropertyChangedFor(nameof(SelfUpdateExplanation))]
    [NotifyPropertyChangedFor(nameof(SelfUpdateHeading))]
    [NotifyPropertyChangedFor(nameof(HasSelfUpdateNotes))]
    [NotifyPropertyChangedFor(nameof(SelfUpdateNotes))]
    [NotifyPropertyChangedFor(nameof(SelfUpdateReleasedText))]
    [NotifyPropertyChangedFor(nameof(HasCheckedForSelfUpdate))]
    private SelfUpdate _selfUpdate = SelfUpdate.NotChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckForSelfUpdate))]
    private bool _isCheckingForSelfUpdate;

    public bool HasSelfUpdate => SelfUpdate.HasUpdate;
    public bool HasCheckedForSelfUpdate => SelfUpdate.State != SelfUpdateState.NotChecked;
    public bool CanCheckForSelfUpdate => !IsCheckingForSelfUpdate;

    public string SelfUpdateHeading => SelfUpdate.State switch
    {
        SelfUpdateState.UpdateAvailable => $"RepoDeck {SelfUpdate.LatestVersion} is available",
        SelfUpdateState.UpToDate => "RepoDeck is up to date",
        SelfUpdateState.Unknown => "Could not check",
        _ => ""
    };

    public string SelfUpdateExplanation => SelfUpdate.Explanation;

    public string SelfUpdateNotes => SelfUpdate.ReleaseNotes ?? "";
    public bool HasSelfUpdateNotes => SelfUpdateNotes.Length > 0;

    public string SelfUpdateReleasedText => SelfUpdate.PublishedAt is { } published
        ? "Released " + Humanize.RelativeTime(published)
        : "";

    [RelayCommand(CanExecute = nameof(CanCheckForSelfUpdate), IncludeCancelCommand = true)]
    private async Task CheckForSelfUpdateAsync(CancellationToken cancellationToken)
    {
        IsCheckingForSelfUpdate = true;

        try
        {
            SelfUpdate = await _services.SelfUpdate.CheckAsync(cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Asked to stop. Nothing to report.
        }
        finally
        {
            IsCheckingForSelfUpdate = false;
        }
    }

    /// <summary>
    /// Opens the release page. RepoDeck does not download or install itself; the installer
    /// already upgrades in place, and the user decides when.
    /// </summary>
    [RelayCommand]
    private void OpenReleasePage() =>
        SystemBrowser.OpenUrl(SelfUpdate.ReleaseUrl ?? RepoDeckProject.ReleasesUrl, _services.Log);

    // ---- Diagnostics ------------------------------------------------------

    [ObservableProperty] private string _diagnosticStatus = "";

    /// <summary>
    /// Puts a report on the clipboard that somebody can paste into an issue without having
    /// to find any of it themselves. Deliberately contains no token and no list of what is
    /// installed - only how many.
    /// </summary>
    [RelayCommand]
    private async Task CopyDiagnosticsAsync()
    {
        try
        {
            var report = DiagnosticReport.Compose(
                _services.Paths,
                _services.GitHub.RateLimit,
                _services.Tokens.HasToken,
                _services.InstalledApps.GetAll().Count,
                CrashReporter.LastReportPath);

            var copied = await _clipboard.SetTextAsync(report).ConfigureAwait(true);

            DiagnosticStatus = copied
                ? "Copied. Paste it into your report."
                : "Could not reach the clipboard. The same information is in the log folder.";
        }
        catch (Exception ex)
        {
            _services.Log.Warn("Settings", "Could not copy diagnostics: " + ex.Message);
            DiagnosticStatus = "Could not build the report.";
        }
    }

    [RelayCommand]
    private void OpenIssues() => SystemBrowser.OpenUrl(RepoDeckProject.IssuesUrl, _services.Log);

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
