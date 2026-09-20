using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// Which action gets the accent, and what the status pills say.
/// </summary>
/// <remarks>
/// The accent has one job now: it marks the thing that can actually happen. It used to be on
/// the DETAILS button of every card, which meant thirty accent buttons on a page of results
/// and a colour that distinguished nothing. These check the decision, not the markup, so the
/// card can be redrawn without rewriting them.
/// </remarks>
public class ActionHierarchyTests
{
    private static RepositoryCardViewModel Card(InstallabilityState state = InstallabilityState.Unknown)
    {
        var repository = TestRepositories.Create("thing", description: "A thing that does something.");
        var explanations = new HeuristicRepositoryExplanationService();
        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);

        var card = new RepositoryCardViewModel(
            repository,
            explanations.ExplainFromMetadata(repository),
            likelihood,
            SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood),
            imageUrl: null,
            openDetails: _ => { },
            log: NullAppLog.Instance);

        if (state != InstallabilityState.Unknown)
        {
            card.ApplyAnalysedInstallability(new Installability { State = state });
        }

        return card;
    }

    // ---- Search: the accent marks a real action ---------------------------

    [Fact]
    public void An_actionable_install_offers_INSTALL_as_the_dominant_action()
    {
        var card = Card(InstallabilityState.ReadyToInstall);

        Assert.True(card.CanInstallDirectly);
        Assert.Equal("INSTALL", card.PrimaryActionLabel);
    }

    [Theory]
    [InlineData(InstallabilityState.Unknown)]
    [InlineData(InstallabilityState.NeedsSetup)]
    [InlineData(InstallabilityState.DeveloperFocused)]
    [InlineData(InstallabilityState.NotCompatible)]
    public void Anything_else_offers_a_quiet_DETAILS(InstallabilityState state)
    {
        var card = Card(state);

        Assert.False(card.CanInstallDirectly);
        Assert.Equal("DETAILS", card.PrimaryActionLabel);
    }

    [Fact]
    public void DETAILS_is_never_dominant_merely_for_being_the_only_button()
    {
        // The rule the accent budget exists for.
        foreach (var state in Enum.GetValues<InstallabilityState>())
        {
            var card = Card(state);

            if (card.PrimaryActionLabel == "DETAILS")
            {
                Assert.False(card.CanInstallDirectly, $"{state} made DETAILS dominant");
            }
        }
    }

    [Fact]
    public void The_action_prominence_follows_the_evidence_as_it_arrives()
    {
        // A card starts unchecked and quiet, and earns its accent when Quick Look finds a
        // package. Nothing about this should need the view to be reloaded.
        var card = Card();

        Assert.False(card.CanInstallDirectly);

        card.ApplyAnalysedInstallability(new Installability
        {
            State = InstallabilityState.ReadyToInstall
        });

        Assert.True(card.CanInstallDirectly);
        Assert.Equal("INSTALL", card.PrimaryActionLabel);
    }

    // ---- The status strip -------------------------------------------------

    [Fact]
    public void An_unchecked_card_says_so_rather_than_saying_unknown()
    {
        // "Unknown" alone reads as a property of the software. What is unknown is whether
        // RepoDeck has looked.
        var card = Card();

        Assert.Equal("Install not checked", card.InstallabilityPillText);
        Assert.True(card.InstallabilityIsNeutral);
    }

    [Fact]
    public void The_installability_pill_steps_aside_when_the_button_already_says_it()
    {
        // An accent INSTALL beside a pill reading "Ready to install" is one sentence twice.
        var ready = Card(InstallabilityState.ReadyToInstall);

        Assert.False(ready.ShowInstallabilityPill);
        Assert.True(ready.CanInstallDirectly);
    }

    [Theory]
    [InlineData(InstallabilityState.Unknown)]
    [InlineData(InstallabilityState.DeveloperFocused)]
    [InlineData(InstallabilityState.NotCompatible)]
    public void Every_other_state_keeps_its_pill(InstallabilityState state)
    {
        Assert.True(Card(state).ShowInstallabilityPill);
    }

    [Fact]
    public void Needs_setup_is_not_said_twice_in_two_different_wordings()
    {
        // Observed on a real result: "Needs some setup" from the setup assessment sitting
        // next to "Needs setup" from installability. Two axes, five words, no reader is
        // going to separate them.
        var card = Card(InstallabilityState.NeedsSetup);

        if (card.ShowSetupLabel)
        {
            Assert.False(card.ShowInstallabilityPill);
        }
    }

    [Fact]
    public void Needs_setup_still_shows_when_setup_itself_is_unknown()
    {
        // With no setup pill there is nothing duplicating it, so the answer must appear.
        var card = Card(InstallabilityState.NeedsSetup);

        if (!card.ShowSetupLabel)
        {
            Assert.True(card.ShowInstallabilityPill);
        }
    }

    [Fact]
    public void Needing_setup_reads_as_caution_in_the_status_strip()
    {
        Assert.Equal(StatusTone.Caution, new SetupAssessment { Level = SetupLevel.SomeSetup }.Tone);
        Assert.Equal(StatusTone.Caution, new Installability
        {
            State = InstallabilityState.NeedsSetup
        }.Tone);
    }

    [Fact]
    public void Not_knowing_how_much_setup_is_needed_is_neutral()
    {
        var unknown = new SetupAssessment { Level = SetupLevel.Unknown };

        Assert.Equal(StatusTone.Neutral, unknown.Tone);
    }

    [Fact]
    public void Setup_never_reaches_danger()
    {
        // Needing work is not a fault in the software, and "for developers" describes what
        // something is rather than warning about it.
        foreach (var level in Enum.GetValues<SetupLevel>())
        {
            var tone = new SetupAssessment { Level = level }.Tone;

            Assert.NotEqual(StatusTone.Danger, tone);
        }
    }

    [Fact]
    public void Every_setup_level_reaches_exactly_one_tone()
    {
        foreach (var level in Enum.GetValues<SetupLevel>())
        {
            var card = new SetupAssessment { Level = level };
            var matched = new[]
            {
                card.Tone == StatusTone.Positive,
                card.Tone == StatusTone.Caution,
                card.Tone == StatusTone.Neutral,
                card.Tone == StatusTone.Danger
            }.Count(on => on);

            Assert.Equal(1, matched);
        }
    }

    // ---- Publisher identity ------------------------------------------------

    [Fact]
    public void The_publisher_is_the_publisher_and_not_the_language()
    {
        var card = Card();

        Assert.Equal("someone", card.Owner);
        Assert.DoesNotContain("/", card.Owner);

        // MetadataLine still fuses the two for anywhere that genuinely wants both; the
        // identity line uses Owner so a language never reads as part of who somebody is.
        Assert.Contains("someone", card.MetadataLine);
    }
}
