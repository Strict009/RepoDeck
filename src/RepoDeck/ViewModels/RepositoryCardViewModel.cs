using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.ViewModels;

/// <summary>
/// One project on the Discover page.
/// </summary>
/// <remarks>
/// Written for someone who has never heard of GitHub. The picture, the friendly name and
/// the plain-English purpose come first; the owner slug, language, licence and star count
/// are still here, but as small print rather than the headline. Nothing is removed - it
/// moves to Details.
/// </remarks>
public sealed partial class RepositoryCardViewModel : ViewModelBase
{
    private readonly Action<GitHubRepository> _openDetails;
    private readonly IAppLog _log;

    public RepositoryCardViewModel(
        GitHubRepository repository,
        RepositoryExplanation explanation,
        ApplicationLikelihood likelihood,
        SetupAssessment setup,
        string? imageUrl,
        Action<GitHubRepository> openDetails,
        IAppLog log)
    {
        Repository = repository;
        Explanation = explanation;
        Likelihood = likelihood;
        Setup = setup;
        ImageUrl = imageUrl;
        _openDetails = openDetails;
        _log = log;
    }

    public GitHubRepository Repository { get; }
    public RepositoryExplanation Explanation { get; }
    public ApplicationLikelihood Likelihood { get; }
    public SetupAssessment Setup { get; }

    /// <summary>The address of the picture, if there is one to try.</summary>
    public string? ImageUrl { get; }

    // ---- What a person reads first ---------------------------------------

    /// <summary>"obs-studio" reads as "Obs Studio". Nobody needs to see the slug first.</summary>
    public string FriendlyName => FriendlyNaming.ForRepository(Repository.Name);

    public string Purpose => string.IsNullOrWhiteSpace(Explanation.WhatItIs)
        ? "No description was provided for this project."
        : Explanation.WhatItIs;

    public string SetupLabel => Setup.Label;
    public bool ShowSetupLabel => Setup.Level != SetupLevel.Unknown;

    /// <summary>Platform hints from tags only - the real answer comes from opening it.</summary>
    public string PlatformText => FriendlyNaming.PlatformHint(Repository.Topics, Repository.Language);

    public bool HasPlatformText => PlatformText.Length > 0;

    // ---- Small print ------------------------------------------------------
    public string Owner => Repository.OwnerLogin;
    public string StarsText => Humanize.Count(Repository.Stars);
    public string UpdatedText => "Updated " + Humanize.RelativeTime(Repository.LastActivity);
    public string LanguageText => Repository.Language ?? "";
    public bool HasLanguage => Repository.Language is { Length: > 0 };
    public bool IsArchived => Repository.IsArchived;

    public IReadOnlyList<string> Topics => Repository.Topics.Take(4).ToList();
    public bool HasTopics => Topics.Count > 0;

    public string VerdictExplanation => Setup.Reasons.Count == 0
        ? "RepoDeck checks this properly when you open it."
        : string.Join(" ", Setup.Reasons);

    // ---- Picture ----------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowImage))]
    [NotifyPropertyChangedFor(nameof(ShowFallback))]
    private Bitmap? _image;

    public bool ShowImage => Image is not null;
    public bool ShowFallback => Image is null;

    /// <summary>The letter shown on the fallback tile.</summary>
    public string FallbackInitial => FriendlyNaming.Initial(Repository.Name);

    /// <summary>
    /// A stable colour derived from the name, so a project looks the same every time and
    /// a page of results looks deliberate rather than grey.
    /// </summary>
    public IBrush FallbackBrush => FriendlyNaming.ColourFor(Repository.FullName);

    /// <summary>Fetches the picture. Failure is silent: the fallback tile is already there.</summary>
    public async Task LoadImageAsync(ImageLoader loader, CancellationToken cancellationToken)
    {
        if (ImageUrl is null) return;

        try
        {
            var bitmap = await loader.LoadImageForCardAsync(ImageUrl, cancellationToken)
                .ConfigureAwait(true);

            if (!cancellationToken.IsCancellationRequested) Image = bitmap;
        }
        catch (OperationCanceledException)
        {
            // The card left the results before its picture arrived. Nothing to do.
        }
    }

    [RelayCommand]
    private void OpenDetails() => _openDetails(Repository);

    [RelayCommand]
    private void OpenOnGitHub() => SystemBrowser.OpenUrl(Repository.HtmlUrl, _log);
}
