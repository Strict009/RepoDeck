using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>
/// One repository card on the Discover page. All display formatting happens here so the
/// view stays free of converters and the strings can be unit tested.
/// </summary>
public sealed partial class RepositoryCardViewModel : ViewModelBase
{
    private readonly Action<GitHubRepository> _openDetails;
    private readonly IAppLog _log;

    public RepositoryCardViewModel(
        GitHubRepository repository,
        RepositoryExplanation explanation,
        ApplicationLikelihood likelihood,
        Action<GitHubRepository> openDetails,
        IAppLog log)
    {
        Repository = repository;
        Explanation = explanation;
        Likelihood = likelihood;
        _openDetails = openDetails;
        _log = log;
    }

    public GitHubRepository Repository { get; }
    public RepositoryExplanation Explanation { get; }
    public ApplicationLikelihood Likelihood { get; }

    public string Name => Repository.Name;
    public string Owner => Repository.OwnerLogin;
    public string FullName => Repository.FullName;

    public string Summary => string.IsNullOrWhiteSpace(Explanation.WhatItIs)
        ? "No description provided."
        : Explanation.WhatItIs;

    public string StarsText => Humanize.Count(Repository.Stars);
    public string ForksText => Humanize.Count(Repository.Forks);
    public string UpdatedText => "Updated " + Humanize.RelativeTime(Repository.LastActivity);
    public string LanguageText => Repository.Language ?? "No main language";
    public bool HasLanguage => Repository.Language is { Length: > 0 };

    public string LicenseText => Repository.HasLicense
        ? (Repository.LicenseSpdxId ?? Repository.LicenseName)!
        : "No licence";

    public string VerdictText => Likelihood.Verdict;
    public bool LooksLikeApplication => Likelihood.LooksLikeApplication;

    public bool IsArchived => Repository.IsArchived;

    public IReadOnlyList<string> Topics => Repository.Topics.Take(5).ToList();
    public bool HasTopics => Topics.Count > 0;

    /// <summary>Tooltip explaining how RepoDeck judged this repository.</summary>
    public string VerdictExplanation => Likelihood.Reasons.Count == 0
        ? "RepoDeck has nothing to go on for this repository."
        : "Why: " + string.Join(" ", Likelihood.Reasons);

    [RelayCommand]
    private void OpenDetails() => _openDetails(Repository);

    [RelayCommand]
    private void OpenOnGitHub() => SystemBrowser.OpenUrl(Repository.HtmlUrl, _log);
}
