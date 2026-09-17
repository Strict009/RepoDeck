using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Install;

namespace RepoDeck.ViewModels;

/// <summary>
/// The part of the details page that answers the four questions a person actually has,
/// before any GitHub terminology appears.
/// </summary>
/// <remarks>
/// What is this? What can I do with it? Will it work on my computer? Can RepoDeck install
/// it for me? Everything else - owner slugs, licences, languages, release assets, the
/// install plan - is still on the page, one level further in.
/// </remarks>
public sealed partial class RepositoryDetailsViewModel
{
    /// <summary>"obs-studio" reads as "Obs Studio".</summary>
    public string FriendlyTitle => FriendlyNaming.ForRepository(Repository.Name);

    // ---- Question three: will it work here? -------------------------------
    [ObservableProperty] private string _worksHereHeadline = "";
    [ObservableProperty] private string _worksHereDetail = "";
    [ObservableProperty] private bool _worksHereIsGood;

    // ---- Question four: can RepoDeck install it? --------------------------
    [ObservableProperty] private string _canInstallHeadline = "";
    [ObservableProperty] private string _canInstallDetail = "";
    [ObservableProperty] private bool _canInstallIsGood;

    // ---- How much work is involved ----------------------------------------
    [ObservableProperty] private string _setupLabel = "";
    [ObservableProperty] private string _setupSummary = "";
    [ObservableProperty] private bool _hasSetupAssessment;

    public ObservableCollection<string> SetupReasons { get; } = [];

    // ---- Pictures ---------------------------------------------------------
    public ObservableCollection<MediaTileViewModel> Gallery { get; } = [];

    /// <summary>The large image at the top of the page, when there is one worth showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHeroFallback))]
    private MediaTileViewModel? _hero;

    /// <summary>True until a hero picture arrives, and permanently when none does.</summary>
    public bool ShowHeroFallback => Hero is null || !Hero.IsLoaded;

    /// <summary>The same designed fallback the cards use, so the two agree.</summary>
    public string FallbackInitial => FriendlyNaming.Initial(Repository.Name);

    public Avalonia.Media.IBrush FallbackBrush => FriendlyNaming.ColourFor(Repository.FullName);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGallery))]
    private bool _hasGallery;

    public bool ShowGallery => HasGallery;

    private void ApplyFriendlySummary(
        RepositoryAnalysis analysis, ReleaseAnalysis releases, InstallPlan plan)
    {
        var setup = SetupDifficultyEvaluator.Evaluate(analysis, releases, plan);

        SetupLabel = setup.Label;
        SetupSummary = setup.Summary;
        HasSetupAssessment = setup.Level != SetupLevel.Unknown;

        SetupReasons.Clear();
        foreach (var reason in setup.Reasons) SetupReasons.Add(reason);

        ApplyWorksHere(analysis, releases);
        ApplyCanInstall(plan);
    }

    private void ApplyWorksHere(RepositoryAnalysis analysis, ReleaseAnalysis releases)
    {
        var machine = _machine.Description;
        var recommended = releases.Recommended;

        if (recommended is not null)
        {
            WorksHereIsGood = recommended.IsUsable;

            WorksHereHeadline = recommended.Compatibility switch
            {
                AssetCompatibility.Compatible => $"Yes - there is a build for {machine}.",
                AssetCompatibility.CompatibleThroughEmulation =>
                    $"Yes, though not built for {machine} directly.",
                AssetCompatibility.LikelyCompatible => $"Probably - it should run on {machine}.",
                _ => $"RepoDeck is not sure it runs on {machine}."
            };

            WorksHereDetail = recommended.Reasons.Count > 0
                ? recommended.Reasons[0]
                : "";

            return;
        }

        WorksHereIsGood = false;

        var support = analysis.PlatformSupport(_machine.OperatingSystem);

        WorksHereHeadline = support switch
        {
            Confidence.Unsupported => $"No - this does not support {machine}.",
            Confidence.Confirmed or Confidence.Likely =>
                $"Probably, but nothing ready to download is published for {machine}.",
            _ => $"RepoDeck could not tell whether this runs on {machine}."
        };

        WorksHereDetail = releases.NoRecommendationReason ?? "";
    }

    private void ApplyCanInstall(InstallPlan plan)
    {
        CanInstallIsGood = plan.CanProceed;

        if (plan.CanProceed)
        {
            CanInstallHeadline = plan.Strategy is InstallStrategy.WindowsInstaller
                or InstallStrategy.LinuxPackage
                ? "RepoDeck can fetch it, but you run the installer yourself."
                : "Yes - RepoDeck can install this for you.";

            CanInstallDetail = plan.StepSummary;
            return;
        }

        CanInstallHeadline = "No - RepoDeck cannot install this one.";

        CanInstallDetail = plan.BlockingIssues.Count > 0
            ? string.Join(" ", plan.BlockingIssues)
            : "There is nothing here RepoDeck knows how to install.";
    }

    private void ApplyMedia(RepositoryMedia media)
    {
        Gallery.Clear();

        if (_images is null)
        {
            HasGallery = false;
            return;
        }

        foreach (var candidate in media.Gallery)
        {
            Gallery.Add(new MediaTileViewModel(candidate.Url, candidate.Description));
        }

        HasGallery = Gallery.Count > 0;
        Hero = Gallery.FirstOrDefault();

        // Pictures load afterwards and never hold up the page. The hero reports back so
        // the fallback panel can step aside the moment a real image arrives.
        foreach (var tile in Gallery)
        {
            if (ReferenceEquals(tile, Hero))
            {
                tile.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MediaTileViewModel.Image))
                    {
                        OnPropertyChanged(nameof(ShowHeroFallback));
                    }
                };
            }

            _ = tile.LoadAsync(_images, CancellationToken.None);
        }
    }
}

/// <summary>One picture in the details gallery.</summary>
public sealed partial class MediaTileViewModel : ViewModelBase
{
    public MediaTileViewModel(string url, string? description)
    {
        Url = url;
        Description = description;
    }

    public string Url { get; }
    public string? Description { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaded))]
    private Bitmap? _image;

    public bool IsLoaded => Image is not null;

    public async Task LoadAsync(ImageLoader loader, CancellationToken cancellationToken)
    {
        try
        {
            Image = await loader.LoadAsync(Url, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The page was left before the picture arrived.
        }
    }
}
