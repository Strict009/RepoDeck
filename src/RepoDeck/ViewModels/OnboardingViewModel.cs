using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDeck.Services.Preferences;

namespace RepoDeck.ViewModels;

/// <summary>
/// What a first-time user sees before anything else.
/// </summary>
/// <remarks>
/// Two steps and then out of the way. The first says what RepoDeck is for and - more
/// importantly - what it will never do, because the audience for this application has been
/// trained by twenty years of download sites to expect a program that fetches software to
/// also install four other things. Saying so plainly at the front door is worth more than
/// any amount of reassurance later.
///
/// The second asks one question with a sensible default already chosen, so somebody who
/// presses the obvious button twice ends up somewhere reasonable.
///
/// It appears once. The flag lives with the interface preferences, so losing it means
/// seeing a welcome screen again rather than anything that matters.
/// </remarks>
public sealed partial class OnboardingViewModel : ViewModelBase
{
    private readonly IUserPreferences _preferences;

    public OnboardingViewModel(IUserPreferences preferences)
    {
        _preferences = preferences;
        _browseMode = preferences.Current.BrowseMode;
    }

    /// <summary>Raised when the user is done and the application should get on with it.</summary>
    public event Action? Finished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcomeStep))]
    [NotifyPropertyChangedFor(nameof(IsModeStep))]
    private OnboardingStep _step = OnboardingStep.Welcome;

    public bool IsWelcomeStep => Step == OnboardingStep.Welcome;
    public bool IsModeStep => Step == OnboardingStep.ChooseMode;

    /// <summary>What RepoDeck does. Four things, not a paragraph.</summary>
    public IReadOnlyList<string> CanDo { get; } =
    [
        "Find programs on GitHub without you needing to understand GitHub",
        "Explain in plain English what each one is for",
        "Check whether it will actually run on your computer",
        "Install the ones it can, into a folder it owns"
    ];

    /// <summary>
    /// What it will never do. These are the M3 boundaries, stated at the front door in the
    /// second person, because they are the reason to use this rather than a download site.
    /// </summary>
    public IReadOnlyList<string> WillNeverDo { get; } =
    [
        "Run anything without you asking it to",
        "Run an installer, a script or a setup program on your behalf",
        "Ask for administrator access, or change any Windows setting",
        "Pretend it understands something when it does not"
    ];

    /// <summary>
    /// The one honest thing that has to be said alongside all of that.
    /// </summary>
    public string Caveat =>
        "RepoDeck cannot tell you whether software is safe or trustworthy. Nothing it shows "
        + "you is a recommendation. It tells you what it found and how it worked it out, and "
        + "the decision stays yours.";

    // ---- Step two ---------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppsChosen))]
    [NotifyPropertyChangedFor(nameof(IsEverythingChosen))]
    private BrowseMode _browseMode;

    public bool IsAppsChosen => BrowseMode == BrowseMode.Apps;
    public bool IsEverythingChosen => BrowseMode == BrowseMode.Everything;

    public string AppsDescription =>
        "Put programs RepoDeck has evidence you can actually run at the top, and set aside "
        + "libraries and reading material. Recommended if you are looking for software to use.";

    public string EverythingDescription =>
        "Show everything GitHub returns, in its own order, with nothing set aside. "
        + "Recommended if you already know your way around.";

    [RelayCommand]
    private void ChooseApps() => BrowseMode = BrowseMode.Apps;

    [RelayCommand]
    private void ChooseEverything() => BrowseMode = BrowseMode.Everything;

    // ---- Moving through ---------------------------------------------------

    [RelayCommand]
    private void GetStarted() => Step = OnboardingStep.ChooseMode;

    [RelayCommand]
    private void Back() => Step = OnboardingStep.Welcome;

    [RelayCommand]
    private void Finish()
    {
        _preferences.Update(p => p with
        {
            BrowseMode = BrowseMode,
            HasSeenWelcome = true
        });

        Finished?.Invoke();
    }

    /// <summary>
    /// Skipping is allowed and is not punished: the defaults are the same ones step two
    /// offers, and the welcome does not come back.
    /// </summary>
    [RelayCommand]
    private void Skip()
    {
        _preferences.Update(p => p with { HasSeenWelcome = true });
        Finished?.Invoke();
    }
}

public enum OnboardingStep
{
    Welcome,
    ChooseMode
}
