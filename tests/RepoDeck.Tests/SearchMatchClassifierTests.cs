using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

/// <summary>
/// The Best Matches / Other Results boundary. These are deliberately awkward shapes:
/// relevance alone must never turn a library, plugin, list or archived project into an
/// end-user application.
/// </summary>
public class SearchMatchClassifierTests
{
    private static readonly MachineProfile Windows =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    private static SearchMatchAssessment Assess(
        GitHubRepository repository,
        string query,
        Installability? installability = null)
    {
        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);
        var classification = ProjectKindClassifier.Classify(repository);
        var capability = installability
                         ?? InstallabilityEvaluator.FromMetadata(
                             repository, likelihood,
                             SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood));
        var relevance = RelevanceScorer.Score(
            repository, query, classification, capability, Windows);

        return SearchMatchClassifier.Assess(
            repository, query, likelihood, classification, capability, relevance, Windows);
    }

    [Fact]
    public void A_relevant_desktop_emulator_is_a_best_match()
    {
        var repository = TestRepositories.Create(
            "pcsx2", description: "A PlayStation 2 emulator for Windows desktop computers.",
            language: "C++", topics: ["emulator", "desktop", "windows"]);

        var result = Assess(repository, "PS2 emulator");

        Assert.Equal(SearchResultGroup.BestMatch, result.Group);
        Assert.Contains(result.Reasons, r => r.Contains("end-user", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Reasons, r => r.Contains("PS2 emulator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_matching_library_stays_under_other_results()
    {
        var repository = TestRepositories.Create(
            "music-player-sdk", description: "A music player library and SDK for developers.",
            topics: ["library", "sdk", "audio"]);

        var result = Assess(repository, "music player");

        Assert.Equal(SearchResultGroup.OtherResult, result.Group);
        Assert.Contains(result.Reasons, r => r.Contains("developers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_plugin_is_not_promoted_as_the_application_it_extends()
    {
        var repository = TestRepositories.Create(
            "video-editor-plugin", description: "A plugin for a popular video editor.",
            topics: ["plugin", "extension", "video"]);

        Assert.Equal(SearchResultGroup.OtherResult, Assess(repository, "video editor").Group);
    }

    [Fact]
    public void An_awesome_list_is_relevant_but_not_presented_as_software()
    {
        var repository = TestRepositories.Create(
            "awesome-file-managers", description: "A curated list of file manager projects.",
            language: "Markdown", topics: ["awesome", "awesome-list", "curated"]);

        var result = Assess(repository, "file manager");

        Assert.Equal(SearchResultGroup.OtherResult, result.Group);
        Assert.Contains(result.Reasons,
            r => r.Contains("reading material", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_archived_application_is_not_a_best_match()
    {
        var repository = TestRepositories.Create(
            "drawing-program", description: "A desktop drawing program.", archived: true,
            topics: ["desktop", "gui", "drawing"]);

        var result = Assess(repository, "drawing program");

        Assert.Equal(SearchResultGroup.OtherResult, result.Group);
        Assert.Contains(result.Reasons, r => r.Contains("archived", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Release_availability_is_unknown_until_it_has_actually_been_checked()
    {
        var repository = TestRepositories.Create(
            "player", description: "A desktop music player.", topics: ["desktop", "music-player"]);

        var result = Assess(repository, "music player");

        Assert.Contains(result.Reasons,
            r => r.Contains("has not been checked", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Reasons,
            r => r.Contains("No packaged", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Confirmed_package_evidence_replaces_the_unknown_statement()
    {
        var repository = TestRepositories.Create(
            "player", description: "A desktop music player.", topics: ["desktop", "music-player"]);
        var installability = new Installability
        {
            State = InstallabilityState.ReadyToInstall,
            Confidence = Confidence.Confirmed,
            Reasons = ["RepoDeck found player-win-x64.zip in release v2.0."]
        };

        var result = Assess(repository, "music player", installability);

        Assert.Equal(SearchResultGroup.BestMatch, result.Group);
        Assert.Contains(result.Reasons,
            r => r.Contains("packaged release", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Reasons,
            r => r.Contains("has not been checked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Explanations_never_claim_quality_safety_or_trust()
    {
        var repository = TestRepositories.Create(
            "player", description: "A desktop music player.", topics: ["desktop", "music-player"]);
        string[] forbidden = ["safe", "trusted", "recommended", "quality", "/100"];

        foreach (var reason in Assess(repository, "music player").Reasons)
        foreach (var word in forbidden)
        {
            Assert.DoesNotContain(word, reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("android-player", "An Android-only music player app.", "android-app")]
    [InlineData("ios-player", "A music player app only for iOS.", "ios-app")]
    public void Explicit_mobile_only_apps_are_not_best_matches_on_windows(
        string name, string description, string topic)
    {
        var result = Assess(TestRepositories.Create(
            name, description: description, topics: [topic, "music-player"]), "music player");

        Assert.Equal(SearchResultGroup.OtherResult, result.Group);
        Assert.Contains(result.Reasons,
            reason => reason.Contains("Windows", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Reasons,
            reason => reason.Contains("has not been checked", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Reasons,
            reason => reason.Contains("No compatible package", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("material-player", "Best Material You Design music player for Android", null)]
    [InlineData("elegant-player", "An elegant and simple iOS music player", null)]
    [InlineData("mobile-player", "A polished music player.", "android")]
    [InlineData("mobile-player", "A polished music player.", "ios")]
    public void Common_explicit_mobile_metadata_is_not_a_best_match_on_windows(
        string name, string description, string? mobileTopic)
    {
        var topics = mobileTopic is null
            ? new[] { "music-player" }
            : new[] { "music-player", mobileTopic };

        var result = Assess(TestRepositories.Create(
            name, description: description, topics: topics), "music player");

        Assert.Equal(SearchResultGroup.OtherResult, result.Group);
        Assert.Contains(result.Reasons,
            reason => reason.Contains("Windows", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Reasons,
            reason => reason.Contains("has not been checked", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Reasons,
            reason => reason.Contains("No compatible package", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("An Android music player for Windows.", "android", "windows")]
    [InlineData("A cross-platform iOS music player.", "ios", "cross-platform")]
    [InlineData("An Android music player for desktop computers.", "android", "desktop")]
    [InlineData("A music player for Windows and Android.", "android", "music-player")]
    [InlineData("A cross-platform music player for iOS and Windows.", "ios", "music-player")]
    [InlineData("An iOS music player also published for PCs.", "ios", "win64")]
    public void Explicit_windows_or_cross_platform_evidence_protects_mobile_mentions(
        string description, string mobileTopic, string protectingTopic)
    {
        var result = Assess(TestRepositories.Create(
            "music-player", description: description,
            topics: ["music-player", mobileTopic, protectingTopic]), "music player");

        Assert.Equal(SearchResultGroup.BestMatch, result.Group);
        Assert.Contains(result.Reasons,
            reason => reason.Contains("has not been checked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_platform_metadata_remains_unknown_rather_than_negative()
    {
        var result = Assess(TestRepositories.Create(
            "music-player", description: "A polished music player.",
            topics: ["music-player"]), "music player");

        Assert.Equal(SearchResultGroup.BestMatch, result.Group);
        Assert.Contains(result.Reasons,
            reason => reason.Contains("has not been checked", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Reasons,
            reason => reason.Contains("Windows", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Reasons,
            reason => reason.Contains("No compatible package", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_platform_evidence_does_not_rule_out_a_best_match()
    {
        var result = Assess(TestRepositories.Create(
            "player", description: "A desktop music player.",
            topics: ["desktop", "music-player"]), "music player");

        Assert.Equal(SearchResultGroup.BestMatch, result.Group);
        Assert.Contains(result.Reasons,
            reason => reason.Contains("has not been checked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_theme_is_not_promoted_as_the_application_it_customizes()
    {
        var result = Assess(TestRepositories.Create(
            "spotify-theme", description: "A dark theme for the Spotify music player.",
            topics: ["theme", "music", "spotify"]), "music player");

        Assert.Equal(SearchResultGroup.OtherResult, result.Group);
        Assert.Contains(result.Reasons,
            reason => reason.Contains("theme", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("desktop-app", "A desktop app for notes.", "desktop", "desktop application")]
    [InlineData("ripgrep", "A command line search program.", "cli", "command-line program")]
    [InlineData("arcade-game", "An arcade game.", "game", "game or emulator")]
    [InlineData("music-player", "An audio music player.", "music-player", "audio or music application")]
    [InlineData("video-player", "A desktop video player.", "video-player", "video application")]
    [InlineData("file-manager", "A file manager utility.", "utility", "end-user utility")]
    public void Best_match_kind_explanations_are_intentional_prose(
        string name, string description, string topic, string expectedPhrase)
    {
        var result = Assess(TestRepositories.Create(
                name, description: description, topics: [topic]), name.Replace('-', ' '),
            new Installability
            {
                State = InstallabilityState.ReadyToInstall,
                Confidence = Confidence.Confirmed,
                Reasons = ["A matching package was inspected."]
            });

        Assert.Equal(SearchResultGroup.BestMatch, result.Group);
        Assert.Contains(result.Reasons,
            reason => reason.Contains(expectedPhrase, StringComparison.OrdinalIgnoreCase));
    }
}

public class SearchDiscoveryPresentationTests
{
    [Fact]
    public void Discover_exposes_both_groups_and_why_in_both_view_modes()
    {
        var discover = File.ReadAllText(Path.Combine(SourceViews(), "DiscoverView.axaml"));
        var card = File.ReadAllText(Path.Combine(SourceViews(), "RepositoryCardView.axaml"));

        Assert.Contains("BEST SEARCH MATCHES", discover);
        Assert.Contains("OTHER RESULTS", discover);
        Assert.Contains("BestMatches", discover);
        Assert.Contains("OtherResults", discover);
        Assert.Contains("Content=\"WHY?\"", discover); // Compact rows.
        Assert.Contains("Header=\"WHY?\"", card);     // Cards.
        Assert.Contains("WhyReasons", discover);
        Assert.Contains("WhyReasons", card);
    }

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
}
