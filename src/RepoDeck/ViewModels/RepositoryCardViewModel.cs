using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.ViewModels;

/// <summary>
/// One application on the Discover page.
/// </summary>
/// <remarks>
/// Ordered the way somebody deciding what to install actually reads: the name, then a
/// picture of it, then what it is for, then whether it runs here, then how much work it
/// will be, then what RepoDeck can do about it, then the action. The owner slug, the
/// language and the star count come last and small - they are facts about a repository,
/// and this is a card about a program.
///
/// Nothing is removed. Everything demoted here is still on the details page.
/// </remarks>
public sealed partial class RepositoryCardViewModel : ViewModelBase
{
    private readonly Action<GitHubRepository> _openDetails;
    private readonly Action<RepositoryCardViewModel>? _quickLook;
    private readonly Action<RepositoryCardViewModel>? _requestInstall;
    private readonly IAppLog _log;

    public RepositoryCardViewModel(
        GitHubRepository repository,
        RepositoryExplanation explanation,
        ApplicationLikelihood likelihood,
        SetupAssessment setup,
        string? imageUrl,
        Action<GitHubRepository> openDetails,
        IAppLog log,
        Action<RepositoryCardViewModel>? quickLook = null,
        Action<RepositoryCardViewModel>? requestInstall = null)
    {
        Repository = repository;
        Explanation = explanation;
        Likelihood = likelihood;
        Setup = setup;
        ImageUrl = imageUrl;
        _openDetails = openDetails;
        _quickLook = quickLook;
        _requestInstall = requestInstall;
        _log = log;

        _installability = InstallabilityEvaluator.FromMetadata(repository, likelihood, setup);
    }

    public GitHubRepository Repository { get; }
    public RepositoryExplanation Explanation { get; }
    public ApplicationLikelihood Likelihood { get; }
    public SetupAssessment Setup { get; }

    /// <summary>The address of the picture, if there is one worth trying.</summary>
    public string? ImageUrl { get; private set; }

    // ---- 1. The name ------------------------------------------------------

    /// <summary>"obs-studio" reads as "Obs Studio". Nobody needs to see the slug first.</summary>
    public string FriendlyName => FriendlyNaming.ForRepository(Repository.Name);

    // ---- 3. What it is for ------------------------------------------------

    public string Purpose => string.IsNullOrWhiteSpace(Explanation.WhatItIs)
        ? "No description was provided for this project."
        : Explanation.WhatItIs;

    // ---- 4. Where it runs -------------------------------------------------

    /// <summary>Platform hints from tags only - the real answer comes from opening it.</summary>
    public string PlatformText => FriendlyNaming.PlatformHint(Repository.Topics, Repository.Language);

    public bool HasPlatformText => PlatformText.Length > 0;

    // ---- 5. How much work -------------------------------------------------

    public string SetupLabel => Setup.Label;
    public bool ShowSetupLabel => Setup.Level != SetupLevel.Unknown;

    public string SetupExplanation => Setup.Reasons.Count == 0
        ? Setup.Summary + " RepoDeck checks this properly when you open it."
        : Setup.Summary + " " + string.Join(" ", Setup.Reasons);

    // ---- 6. What RepoDeck can do about it ---------------------------------

    /// <summary>
    /// Starts as the metadata answer and is replaced by the authoritative one once Quick
    /// Look has produced a plan. Never a safety judgement.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallabilityLabel))]
    [NotifyPropertyChangedFor(nameof(InstallabilityExplanation))]
    [NotifyPropertyChangedFor(nameof(IsReadyToInstall))]
    [NotifyPropertyChangedFor(nameof(IsNotCompatible))]
    [NotifyPropertyChangedFor(nameof(IsDeveloperFocused))]
    [NotifyPropertyChangedFor(nameof(IsInstallabilityKnown))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionLabel))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionTooltip))]
    private Installability _installability;

    public string InstallabilityLabel => Installability.Label;

    /// <summary>
    /// The evidence, plus the disclaimer. Every surface that shows the state shows this,
    /// so the state can never be read as a verdict on whether the software is safe.
    /// </summary>
    public string InstallabilityExplanation
    {
        get
        {
            var body = Installability.Reasons.Count == 0
                ? Installability.Summary
                : Installability.Summary + "\n\n"
                  + string.Join("\n", Installability.Reasons.Select(r => "- " + r));

            return body + "\n\n" + Models.Installability.NotASafetyJudgement;
        }
    }

    public bool IsReadyToInstall => Installability.State == InstallabilityState.ReadyToInstall;
    public bool IsNotCompatible => Installability.State == InstallabilityState.NotCompatible;
    public bool IsDeveloperFocused => Installability.State == InstallabilityState.DeveloperFocused;
    public bool IsInstallabilityKnown => Installability.State != InstallabilityState.Unknown;

    // ---- 7. The action ----------------------------------------------------

    /// <summary>
    /// INSTALL only once a plan exists and can proceed. Until then the honest offer is to
    /// go and look, because RepoDeck has not yet established there is anything to install.
    /// </summary>
    public string PrimaryActionLabel => Installability.AllowsDirectInstall ? "INSTALL" : "DETAILS";

    public string PrimaryActionTooltip => Installability.AllowsDirectInstall
        ? "Review the installation plan and install this"
        : "Look at this project in detail";

    // ---- 8. Small print ---------------------------------------------------

    public string Owner => Repository.OwnerLogin;
    public string StarsText => Humanize.Count(Repository.Stars);
    public string UpdatedText => "Updated " + Humanize.RelativeTime(Repository.LastActivity);
    public string LanguageText => Repository.Language ?? "";
    public bool HasLanguage => Repository.Language is { Length: > 0 };
    public bool IsArchived => Repository.IsArchived;

    public IReadOnlyList<string> Topics => Repository.Topics.Take(4).ToList();
    public bool HasTopics => Topics.Count > 0;

    /// <summary>Owner and language on one line, because neither deserves its own.</summary>
    public string MetadataLine => HasLanguage ? Owner + "  /  " + LanguageText : Owner;

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

    /// <summary>
    /// Replaces the card's picture with real artwork found by a deeper look.
    /// </summary>
    /// <remarks>
    /// A search result carries no README and no file listing, so a card starts with
    /// nothing better than its own designed tile. Once Quick Look has read the README it
    /// often has an actual screenshot, and putting that back on the card means the grid
    /// improves as someone explores rather than staying stubbornly generic.
    /// </remarks>
    public async Task AdoptArtworkAsync(
        string url, ImageLoader? loader, CancellationToken cancellationToken)
    {
        if (loader is null || string.Equals(url, ImageUrl, StringComparison.Ordinal)) return;

        ImageUrl = url;
        await LoadImageAsync(loader, cancellationToken);
    }

    /// <summary>Applies the authoritative verdict once a plan has been produced.</summary>
    public void ApplyAnalysedInstallability(Installability installability) =>
        Installability = installability;

    // ---- Commands ---------------------------------------------------------

    /// <summary>
    /// The card's main action.
    /// </summary>
    /// <remarks>
    /// INSTALL does not install from here. It goes to the installation plan and its
    /// confirmation step, because Milestone 3's rule is that the plan is shown and
    /// confirmed before anything is downloaded, and that gate has exactly one
    /// implementation. The label names where it takes you, not a one-click install.
    ///
    /// DETAILS opens the side panel when there is one, because at that point RepoDeck has
    /// not established there is anything to install and the honest offer is to go and look.
    /// </remarks>
    [RelayCommand]
    private void Activate()
    {
        if (Installability.AllowsDirectInstall && _requestInstall is not null)
        {
            _requestInstall(this);
            return;
        }

        if (_quickLook is not null)
        {
            _quickLook(this);
            return;
        }

        _openDetails(Repository);
    }

    [RelayCommand]
    private void OpenDetails() => _openDetails(Repository);

    [RelayCommand]
    private void OpenOnGitHub() => SystemBrowser.OpenUrl(Repository.HtmlUrl, _log);
}
