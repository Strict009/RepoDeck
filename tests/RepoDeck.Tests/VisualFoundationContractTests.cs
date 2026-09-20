using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.Favorites;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// The invariants the visual foundation must not break.
/// </summary>
/// <remarks>
/// These protect meaning, not markup. An earlier version of this file froze the shape of the
/// implementation - that an EmptyStateView existed, that a style class was spelled a
/// particular way, that a button said "WHY?", that Project Detail was never touched - which
/// would have made planned work fail for no reason. Structural breakage is XAML compilation's
/// job. What is left here is what a person would still be entitled to expect afterwards.
/// </remarks>
public class VisualFoundationContractTests
{
    private static RepositoryCardViewModel Card(string? description)
    {
        var repository = TestRepositories.Create("thing", description: description);
        var explanations = new HeuristicRepositoryExplanationService();
        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);

        return new RepositoryCardViewModel(
            repository,
            explanations.ExplainFromMetadata(repository),
            likelihood,
            SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood),
            imageUrl: null,
            openDetails: _ => { },
            log: NullAppLog.Instance);
    }

    // ---- Display text is derived, and the source is never touched ---------

    [Fact]
    public void Cleaning_a_card_purpose_leaves_the_repository_description_alone()
    {
        const string original = ":rocket: A fast thing.";

        var card = Card(original);

        Assert.Equal(original, card.Repository.Description);
        Assert.DoesNotContain(":rocket:", card.Purpose);
    }

    [Fact]
    public void A_favourite_keeps_the_description_that_was_saved()
    {
        var entry = new FavoriteEntry
        {
            Owner = "someone",
            Name = "thing",
            RepositoryUrl = "https://github.com/someone/thing",
            Description = ":star: A saved thing.",
            AddedAt = DateTimeOffset.UtcNow
        };

        var favourite = new FavoriteViewModel(
            entry, isInstalled: false, _ => { }, _ => { }, NullAppLog.Instance);

        Assert.Equal(":star: A saved thing.", favourite.Entry.Description);
        Assert.DoesNotContain(":star:", favourite.Description);
    }

    [Fact]
    public void A_project_with_no_description_still_gets_a_sentence()
    {
        // The cleaner invents nothing - null in, empty out. Saying something useful about a
        // project that describes itself nowhere is a decision made above it.
        Assert.Equal("", DescriptionCleaner.Clean(null));

        var purpose = Card(null).Purpose;

        Assert.False(string.IsNullOrWhiteSpace(purpose));
        Assert.DoesNotContain("null", purpose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_purpose_is_computed_once_rather_than_per_binding_read()
    {
        var card = Card("A thing.");

        Assert.Same(card.Purpose, card.Purpose);
    }

    // ---- The semantic tone mapping ----------------------------------------

    [Theory]
    [InlineData(InstallabilityState.ReadyToInstall, StatusTone.Positive)]
    [InlineData(InstallabilityState.NeedsSetup, StatusTone.Caution)]
    [InlineData(InstallabilityState.DeveloperFocused, StatusTone.Caution)]
    [InlineData(InstallabilityState.NotCompatible, StatusTone.Danger)]
    [InlineData(InstallabilityState.Unknown, StatusTone.Neutral)]
    public void Every_installability_state_maps_to_its_intended_tone(
        InstallabilityState state, StatusTone expected)
    {
        Assert.Equal(expected, new Installability { State = state }.Tone);
    }

    [Fact]
    public void Not_having_looked_yet_is_never_a_warning()
    {
        // The rule the whole pill system rests on: an absence of evidence is not a caution,
        // a danger, or good news. It is silence.
        var unknown = new Installability { State = InstallabilityState.Unknown };

        Assert.Equal(StatusTone.Neutral, unknown.Tone);

        var card = Card("A thing.");

        Assert.False(card.IsInstallabilityKnown);
        Assert.True(card.InstallabilityIsNeutral);
        Assert.False(card.InstallabilityIsPositive);
        Assert.False(card.InstallabilityIsCaution);
        Assert.False(card.InstallabilityIsDanger);
    }

    [Fact]
    public void Being_ruled_out_stays_distinct_from_not_having_been_checked()
    {
        var checkedAndRuledOut = new Installability { State = InstallabilityState.NotCompatible };
        var notChecked = new Installability { State = InstallabilityState.Unknown };

        Assert.NotEqual(checkedAndRuledOut.Tone, notChecked.Tone);
    }

    [Fact]
    public void Needing_setup_reads_as_work_rather_than_failure()
    {
        var needsSetup = new Installability { State = InstallabilityState.NeedsSetup };

        Assert.Equal(StatusTone.Caution, needsSetup.Tone);
        Assert.NotEqual(StatusTone.Danger, needsSetup.Tone);
    }

    [Fact]
    public void Every_state_reaches_exactly_one_tone_binding()
    {
        // NeedsSetup previously matched none of the four and fell through to an unstyled
        // pill. Whatever the states become, each must land on exactly one.
        foreach (var state in Enum.GetValues<InstallabilityState>())
        {
            var card = Card("A thing.");
            card.ApplyAnalysedInstallability(new Installability { State = state });

            var matched = new[]
            {
                card.InstallabilityIsPositive,
                card.InstallabilityIsCaution,
                card.InstallabilityIsDanger,
                card.InstallabilityIsNeutral
            }.Count(on => on);

            Assert.True(matched == 1, $"{state} matched {matched} tones");
        }
    }

    [Fact]
    public void A_tone_never_travels_without_words()
    {
        // Colour is never the only signal, so every state must have a label to put in the
        // pill beside it.
        foreach (var state in Enum.GetValues<InstallabilityState>())
        {
            var label = new Installability { State = state }.Label;

            Assert.False(string.IsNullOrWhiteSpace(label), $"{state} had no label");
        }
    }

    // ---- Product rules that outlive any redesign --------------------------

    private static string View(string name) =>
        File.ReadAllText(Path.Combine(SourceViews(), name));

    private static string SourceViews()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "RepoDeck", "Views");
    }

    [Fact]
    public void Downloads_still_has_no_way_to_run_anything()
    {
        // The page exists because RepoDeck declined to run these. However it is redrawn, it
        // never gains a Run. This one is about the product, not the markup.
        var markup = View("DownloadsView.axaml");

        Assert.DoesNotContain("RunCommand", markup);
        Assert.DoesNotContain("Content=\"RUN\"", markup);
    }

    [Fact]
    public void Nothing_in_the_shared_styles_claims_safety_trust_or_quality()
    {
        string[] forbidden = ["safe", "trusted", "recommended", "verified", "approved", "quality"];

        foreach (var name in new[]
                 {
                     "Theme.axaml", "DiscoverView.axaml", "RepositoryCardView.axaml",
                     "FavoritesView.axaml", "InstalledView.axaml", "DownloadsView.axaml"
                 })
        {
            var markup = View(name);

            foreach (var word in forbidden)
            {
                // "safe" appears inside words like "safety" in existing disclaimers, so this
                // looks for it as a claim about the software rather than as a substring.
                Assert.DoesNotContain($"\"{word}\"", markup, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain($">{word}<", markup, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
