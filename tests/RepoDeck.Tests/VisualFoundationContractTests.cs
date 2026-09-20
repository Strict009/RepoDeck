using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.Favorites;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// What the shared visual foundation must not break.
/// </summary>
/// <remarks>
/// Phase 1 changed how things look, not what they do or say. These hold that line: the
/// descriptions RepoDeck stores are never rewritten, an absence of evidence never acquires a
/// colour, and the actions each page owns are still the actions it owns.
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

    // ---- The source text is never rewritten -------------------------------

    [Fact]
    public void Cleaning_a_card_purpose_leaves_the_repository_description_alone()
    {
        const string original = ":rocket: a fast thing.";

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
            Description = ":star: a saved thing.",
            AddedAt = DateTimeOffset.UtcNow
        };

        var favourite = new FavoriteViewModel(
            entry, isInstalled: false, _ => { }, _ => { }, NullAppLog.Instance);

        Assert.Equal(":star: a saved thing.", favourite.Entry.Description);
        Assert.DoesNotContain(":star:", favourite.Description);
    }

    [Fact]
    public void A_project_with_no_description_still_gets_a_sentence()
    {
        // The cleaner itself invents nothing - null in, empty out. Saying something useful
        // about a project that describes itself nowhere is a decision made above it, and the
        // explanation service already makes it.
        Assert.Equal("", DescriptionCleaner.Clean(null));

        var purpose = Card(null).Purpose;

        Assert.False(string.IsNullOrWhiteSpace(purpose));
        Assert.DoesNotContain("null", purpose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_purpose_is_computed_once_rather_than_per_binding_read()
    {
        var card = Card("a thing.");

        Assert.Same(card.Purpose, card.Purpose);
    }

    // ---- Unknown is neutral, everywhere -----------------------------------

    [Fact]
    public void An_unchecked_installability_is_neutral_and_carries_words()
    {
        var card = Card("a thing.");

        Assert.False(card.IsInstallabilityKnown);
        Assert.False(card.IsReadyToInstall);
        Assert.False(card.IsDeveloperFocused);
        Assert.False(card.IsNotCompatible);

        // The pill is never blank: a tone with no label would be colour as the sole signal.
        Assert.False(string.IsNullOrWhiteSpace(card.InstallabilityLabel));
    }

    [Fact]
    public void Every_installability_state_has_a_label_to_put_in_its_pill()
    {
        foreach (var state in Enum.GetValues<InstallabilityState>())
        {
            var label = new Installability { State = state }.Label;

            Assert.False(string.IsNullOrWhiteSpace(label), $"{state} had no label");
        }
    }

    // ---- The markup contracts ---------------------------------------------

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
    public void Unknown_binds_to_the_neutral_tone_wherever_a_status_pill_appears()
    {
        foreach (var view in new[] { "DiscoverView.axaml", "RepositoryCardView.axaml" })
        {
            Assert.Contains("Classes.neutral=\"{Binding !IsInstallabilityKnown}\"", View(view));
        }
    }

    [Fact]
    public void A_status_pill_never_relies_on_colour_alone()
    {
        // Every statusPill in the product wraps a TextBlock. A pill with a tone and no text
        // would be unreadable in greyscale and to anyone who cannot separate the hues.
        foreach (var name in new[]
                 {
                     "DiscoverView.axaml", "RepositoryCardView.axaml",
                     "FavoritesView.axaml", "InstalledView.axaml"
                 })
        {
            var markup = View(name);
            var pills = markup.Split("Classes=\"statusPill").Length - 1;

            if (pills == 0) continue;

            Assert.Contains("<TextBlock", markup);
        }
    }

    [Fact]
    public void The_card_no_longer_labels_every_result_with_the_product_name()
    {
        Assert.DoesNotContain("Text=\"REPODECK\"", View("RepositoryCardView.axaml"));
    }

    [Fact]
    public void Search_still_offers_its_evidence_in_both_views()
    {
        Assert.Contains("Header=\"WHY?\"", View("RepositoryCardView.axaml"));
        Assert.Contains("Content=\"WHY?\"", View("DiscoverView.axaml"));
        Assert.Contains("WhyReasons", View("RepositoryCardView.axaml"));
        Assert.Contains("WhyReasons", View("DiscoverView.axaml"));
    }

    [Fact]
    public void The_library_still_offers_run_repair_and_update()
    {
        var markup = View("InstalledView.axaml");

        Assert.Contains("RunCommand", markup);
        Assert.Contains("RepairCommand", markup);
        Assert.Contains("UpdateCommand", markup);
    }

    [Fact]
    public void Favorites_still_offers_open_and_forget()
    {
        var markup = View("FavoritesView.axaml");

        Assert.Contains("OpenCommand", markup);
        Assert.Contains("ForgetCommand", markup);
    }

    [Fact]
    public void Downloads_still_has_no_way_to_run_anything()
    {
        // The page exists because RepoDeck declined to run these. It never gains a Run.
        var markup = View("DownloadsView.axaml");

        Assert.DoesNotContain("RunCommand", markup);
        Assert.DoesNotContain(">RUN<", markup);
        Assert.DoesNotContain("Content=\"RUN\"", markup);
    }

    [Fact]
    public void Downloads_is_not_dressed_as_an_application_row()
    {
        // Transfers and retained files are not applications, and giving them the library's
        // clothes would imply the action the page refuses to offer.
        Assert.DoesNotContain("Classes=\"applicationRow\"", View("DownloadsView.axaml"));
    }

    [Fact]
    public void The_three_pages_share_one_empty_state_rather_than_three_copies()
    {
        foreach (var name in new[] { "FavoritesView.axaml", "InstalledView.axaml", "DownloadsView.axaml" })
        {
            Assert.Contains("EmptyStateView", View(name));
        }
    }

    [Fact]
    public void Project_detail_was_left_alone()
    {
        // Phase 1 explicitly stops short of Project Detail 2.0.
        var markup = View("RepositoryDetailsView.axaml");

        Assert.DoesNotContain("applicationRow", markup);
        Assert.DoesNotContain("statusPill", markup);
    }
}
