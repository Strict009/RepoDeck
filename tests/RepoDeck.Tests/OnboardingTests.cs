using RepoDeck.Services.Preferences;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// The first-run welcome. Two steps, then gone for good.
/// </summary>
/// <remarks>
/// What it promises never to do is part of the contract, not decoration, so these tests
/// hold it to the same wording the rest of the application is held to.
/// </remarks>
public class OnboardingTests
{
    private static OnboardingViewModel Create(IUserPreferences? preferences = null) =>
        new(preferences ?? new FakePreferences());

    [Fact]
    public void It_starts_on_the_welcome()
    {
        var onboarding = Create();

        Assert.True(onboarding.IsWelcomeStep);
        Assert.False(onboarding.IsModeStep);
    }

    [Fact]
    public void Get_started_moves_to_the_one_question()
    {
        var onboarding = Create();

        onboarding.GetStartedCommand.Execute(null);

        Assert.True(onboarding.IsModeStep);
        Assert.False(onboarding.IsWelcomeStep);
    }

    [Fact]
    public void Back_returns_to_the_welcome()
    {
        var onboarding = Create();

        onboarding.GetStartedCommand.Execute(null);
        onboarding.BackCommand.Execute(null);

        Assert.True(onboarding.IsWelcomeStep);
    }

    [Fact]
    public void The_two_steps_are_never_both_showing()
    {
        var onboarding = Create();

        Assert.NotEqual(onboarding.IsWelcomeStep, onboarding.IsModeStep);

        onboarding.GetStartedCommand.Execute(null);
        Assert.NotEqual(onboarding.IsWelcomeStep, onboarding.IsModeStep);
    }

    // ---- What it says -----------------------------------------------------

    [Fact]
    public void It_says_what_RepoDeck_can_do_and_what_it_will_never_do()
    {
        var onboarding = Create();

        Assert.NotEmpty(onboarding.CanDo);
        Assert.NotEmpty(onboarding.WillNeverDo);
    }

    [Fact]
    public void The_promises_it_makes_are_the_boundaries_the_installer_actually_keeps()
    {
        // If these ever stop being true, the welcome becomes a lie on the first screen.
        var never = string.Join(" ", Create().WillNeverDo).ToLowerInvariant();

        Assert.Contains("without you asking", never);
        Assert.Contains("installer", never);
        Assert.Contains("administrator", never);
    }

    [Fact]
    public void It_admits_what_RepoDeck_cannot_tell_anybody()
    {
        // The welcome is exactly the wrong place to imply RepoDeck vets software.
        var caveat = Create().Caveat.ToLowerInvariant();

        Assert.Contains("cannot tell you whether software is safe", caveat);
        Assert.Contains("is a recommendation", caveat);
    }

    [Fact]
    public void Nothing_it_says_claims_to_vet_or_endorse_software()
    {
        string[] forbidden = ["trusted", "verified", "safe software", "we recommend", "curated"];

        var onboarding = Create();
        var everything = string.Join(" ",
            [.. onboarding.CanDo, .. onboarding.WillNeverDo, onboarding.Caveat,
             onboarding.AppsDescription, onboarding.EverythingDescription]).ToLowerInvariant();

        foreach (var word in forbidden)
        {
            Assert.DoesNotContain(word, everything, StringComparison.Ordinal);
        }
    }

    // ---- The one question -------------------------------------------------

    [Fact]
    public void Apps_is_offered_as_the_default()
    {
        Assert.True(Create().IsAppsChosen);
    }

    [Fact]
    public void The_choice_can_be_changed_before_finishing()
    {
        var onboarding = Create();

        onboarding.ChooseEverythingCommand.Execute(null);
        Assert.True(onboarding.IsEverythingChosen);
        Assert.False(onboarding.IsAppsChosen);

        onboarding.ChooseAppsCommand.Execute(null);
        Assert.True(onboarding.IsAppsChosen);
    }

    [Fact]
    public void Finishing_stores_the_choice()
    {
        var preferences = new FakePreferences();
        var onboarding = Create(preferences);

        onboarding.ChooseEverythingCommand.Execute(null);
        onboarding.FinishCommand.Execute(null);

        Assert.Equal(BrowseMode.Everything, preferences.Current.BrowseMode);
        Assert.True(preferences.Current.HasSeenWelcome);
    }

    [Fact]
    public void Finishing_tells_the_shell_to_get_on_with_it()
    {
        var finished = 0;
        var onboarding = Create();
        onboarding.Finished += () => finished++;

        onboarding.FinishCommand.Execute(null);

        Assert.Equal(1, finished);
    }

    // ---- Skipping ---------------------------------------------------------

    [Fact]
    public void Skipping_is_not_punished()
    {
        // Somebody who skips gets the same defaults the second step offers, and the
        // welcome does not come back to ask again.
        var preferences = new FakePreferences();
        var onboarding = Create(preferences);

        onboarding.SkipCommand.Execute(null);

        Assert.True(preferences.Current.HasSeenWelcome);
        Assert.Equal(BrowseMode.Apps, preferences.Current.BrowseMode);
    }

    [Fact]
    public void Skipping_also_finishes()
    {
        var finished = 0;
        var onboarding = Create();
        onboarding.Finished += () => finished++;

        onboarding.SkipCommand.Execute(null);

        Assert.Equal(1, finished);
    }

    [Fact]
    public void An_existing_preference_is_offered_rather_than_overridden()
    {
        // Somebody who cleared the welcome flag but kept their mode should not silently
        // have it changed back.
        var preferences = new FakePreferences(
            new PreferencesSnapshot { BrowseMode = BrowseMode.Everything });

        Assert.True(Create(preferences).IsEverythingChosen);
    }
}
