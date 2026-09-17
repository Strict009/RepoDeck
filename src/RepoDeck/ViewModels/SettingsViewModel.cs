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

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        RefreshRateLimit();
        _services.GitHub.RateLimitChanged += _ => RefreshRateLimit();
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
