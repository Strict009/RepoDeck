using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

/// <summary>
/// How likely a result is to be what the user meant.
/// </summary>
/// <remarks>
/// The rules these tests exist to protect: it answers that one question and no other, it
/// never reads popularity as relevance, and it never costs a request.
/// </remarks>
public class RelevanceTests
{
    private static readonly MachineProfile Windows =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static Relevance Score(GitHubRepository repository, string query) =>
        RelevanceScorer.ScoreFromMetadata(repository, query, Windows);

    private static Relevance Score(
        GitHubRepository repository, string query, Installability installability) =>
        RelevanceScorer.Score(repository, query, ProjectKindClassifier.Classify(repository),
            installability, Windows);

    // ---- The rule that matters most --------------------------------------

    [Fact]
    public void Popularity_is_not_relevance()
    {
        // A wildly popular library is still the wrong answer for "music player", and
        // treating stars as relevance is how a search stops surfacing the small useful
        // thing that does exactly what was asked.
        var obscure = TestRepositories.Create(
            "music-player", description: "A music player for your desktop.", stars: 3);

        var famous = TestRepositories.Create(
            "music-player", description: "A music player for your desktop.", stars: 90_000);

        Assert.Equal(Score(obscure, "music player").Score, Score(famous, "music player").Score);
    }

    [Fact]
    public void The_same_result_and_query_always_score_the_same()
    {
        var repository = TestRepositories.Create(
            "shotcut", description: "A free video editor.", topics: ["video", "editor"]);

        var first = Score(repository, "video editor");
        var second = Score(repository, "video editor");

        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Reasons, second.Reasons);
    }

    [Fact]
    public void Every_score_can_be_explained()
    {
        var scored = Score(TestRepositories.Create("timber", description: "Music player."), "music player");

        Assert.NotEmpty(scored.Reasons);
        Assert.All(scored.Reasons, r => Assert.False(string.IsNullOrWhiteSpace(r)));
    }

    [Fact]
    public void A_score_is_always_within_range()
    {
        GitHubRepository[] awkward =
        [
            TestRepositories.Create("awesome-everything", description: "A curated list of lists.",
                topics: ["awesome", "library"], archived: true),
            TestRepositories.Create("music-player", description: "Music player.", topics: ["music-player"])
        ];

        foreach (var repository in awkward)
        {
            var scored = Score(repository, "music player");

            Assert.InRange(scored.Score, 0, 100);
        }
    }

    // ---- Name matching ----------------------------------------------------

    [Fact]
    public void An_exact_name_match_outranks_a_partial_one()
    {
        var exact = TestRepositories.Create("music-player", description: "Plays things.");
        var partial = TestRepositories.Create("music-player-web-ui-components", description: "Plays things.");

        Assert.True(Score(exact, "music player").Score > Score(partial, "music player").Score);
    }

    [Fact]
    public void A_name_that_matches_nothing_scores_below_one_that_does()
    {
        var matching = TestRepositories.Create("video-editor", description: "Edits video.");
        var unrelated = TestRepositories.Create("quux", description: "Edits video.");

        Assert.True(Score(matching, "video editor").Score > Score(unrelated, "video editor").Score);
    }

    [Fact]
    public void Common_words_are_not_treated_as_a_match()
    {
        // "the", "app", "free" match everything and therefore mean nothing.
        Assert.Empty(RelevanceScorer.Terms("the a free app for my"));
        Assert.Equal(["music", "player"], RelevanceScorer.Terms("the best free music player app"));
    }

    [Fact]
    public void An_empty_query_neither_helps_nor_hurts_anything()
    {
        var repository = TestRepositories.Create("timber", description: "Music player.");

        var scored = Score(repository, "");

        Assert.InRange(scored.Score, 0, 100);
        Assert.DoesNotContain(scored.Reasons, r => r.Contains("searched for", StringComparison.OrdinalIgnoreCase));
    }

    // ---- What kind of thing it is ----------------------------------------

    [Fact]
    public void A_library_scores_below_a_program_for_the_same_search()
    {
        var program = TestRepositories.Create(
            "player", description: "A music player for your desktop.", topics: ["music-player"]);

        var library = TestRepositories.Create(
            "player", description: "A music playback library.", topics: ["library", "audio"]);

        Assert.True(Score(program, "music player").Score > Score(library, "music player").Score);
    }

    [Fact]
    public void Reading_material_scores_below_software()
    {
        var software = TestRepositories.Create("vlc", description: "A video player.");
        var list = TestRepositories.Create(
            "awesome-video", description: "A curated list of video players.", topics: ["awesome"]);

        Assert.True(Score(software, "video player").Score > Score(list, "video player").Score);
    }

    // ---- Can it be used here? --------------------------------------------

    [Fact]
    public void Something_RepoDeck_can_install_outranks_something_it_cannot()
    {
        var repository = TestRepositories.Create("tool", description: "A desktop tool.");

        var ready = Score(repository, "tool", new Installability { State = InstallabilityState.ReadyToInstall });
        var incompatible = Score(repository, "tool", new Installability { State = InstallabilityState.NotCompatible });

        Assert.True(ready.Score > incompatible.Score);
        Assert.Contains(ready.Reasons, r => r.Contains("plan to install", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_unanalysed_result_is_neither_promoted_nor_punished_for_it()
    {
        // The grid scores everything before anything is analysed. Unknown installability
        // has to be neutral or the first page would be ordered by what has been clicked.
        var repository = TestRepositories.Create("tool", description: "A desktop tool.");

        var unknown = Score(repository, "tool", Installability.Unknown);
        var fromMetadata = Score(repository, "tool");

        Assert.Equal(fromMetadata.Score, unknown.Score);
    }

    // ---- Maintenance ------------------------------------------------------

    [Fact]
    public void An_abandoned_project_scores_below_a_maintained_one()
    {
        var alive = TestRepositories.Create("player", description: "A music player.");
        var dead = TestRepositories.Create("player", description: "A music player.", archived: true);

        Assert.True(Score(alive, "music player").Score > Score(dead, "music player").Score);
        Assert.Contains(Score(dead, "music player").Reasons,
            r => r.Contains("stopped maintaining", StringComparison.OrdinalIgnoreCase));
    }

    // ---- What it never says ----------------------------------------------

    [Fact]
    public void No_reason_ever_describes_the_software_as_good_or_safe()
    {
        string[] forbidden = ["safe", "trusted", "verified", "secure", "best", "quality", "popular"];

        GitHubRepository[] all =
        [
            TestRepositories.Create("player", description: "A music player.", stars: 90_000),
            TestRepositories.Create("lib", description: "A library.", topics: ["library"]),
            TestRepositories.Create("dead", description: "A player.", archived: true),
            TestRepositories.Create("awesome-x", description: "A curated list.", topics: ["awesome"])
        ];

        foreach (var repository in all)
        {
            foreach (var reason in Score(repository, "music player").Reasons)
            {
                foreach (var word in forbidden)
                {
                    Assert.DoesNotContain(word, reason.ToLowerInvariant(), StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void A_neutral_result_sits_at_the_midpoint()
    {
        Assert.Equal(50, Relevance.Neutral.Score);
    }
}
