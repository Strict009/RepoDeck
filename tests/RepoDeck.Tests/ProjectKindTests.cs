using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

/// <summary>
/// What kind of thing a project is, from a search result alone. Drives the artwork drawn
/// for projects with no pictures and feeds the relevance layer.
/// </summary>
public class ProjectKindTests
{
    private static ProjectClassification Classify(
        string name, string? description = null, params string[] topics) =>
        ProjectKindClassifier.Classify(
            TestRepositories.Create(name, description: description, topics: topics));

    [Theory]
    [InlineData("timber", "Material Design Music Player.", ProjectKind.Audio)]
    [InlineData("nuclear", "Streaming music player that finds free music for you.", ProjectKind.Audio)]
    [InlineData("shotcut", "A free video editor for Windows, Mac and Linux.", ProjectKind.Video)]
    [InlineData("obs-studio", "Software for live streaming and screen recording.", ProjectKind.Video)]
    [InlineData("retroarch", "A frontend for emulators and game engines.", ProjectKind.GameOrEmulator)]
    [InlineData("ripgrep", "Recursively search directories from the command line.", ProjectKind.CommandLineTool)]
    [InlineData("czkawka", "Find duplicate files on your computer.", ProjectKind.Utility)]
    public void A_project_is_classified_from_its_name_and_description(
        string name, string description, ProjectKind expected)
    {
        Assert.Equal(expected, Classify(name, description).Kind);
    }

    [Theory]
    [InlineData(ProjectKind.Audio, "music-player")]
    [InlineData(ProjectKind.Video, "video-editor")]
    [InlineData(ProjectKind.GameOrEmulator, "emulator")]
    [InlineData(ProjectKind.Library, "library")]
    [InlineData(ProjectKind.DeveloperTool, "ide")]
    [InlineData(ProjectKind.CommandLineTool, "cli")]
    public void A_tag_is_stronger_evidence_than_a_word(ProjectKind expected, string topic)
    {
        // Tags are chosen deliberately by the author; words in a description are not.
        Assert.Equal(expected, Classify("thing", "A thing.", topic).Kind);
    }

    [Theory]
    [InlineData("Recursively search directories from the command line.", ProjectKind.GameOrEmulator)]
    [InlineData("A promise-based helper for your desktop.", ProjectKind.GameOrEmulator)]
    [InlineData("Runs in Chrome and Firefox.", ProjectKind.GameOrEmulator)]
    public void A_word_inside_a_longer_word_is_not_a_match(string description, ProjectKind wrong)
    {
        // "from" contains "rom", and substring matching classified a command-line search
        // tool as an emulator. Single words have to be words in their own right.
        Assert.NotEqual(wrong, Classify("thing", description).Kind);
    }

    [Fact]
    public void A_library_that_handles_video_is_a_library()
    {
        // The word "video" appearing first must not decide this.
        var classified = Classify(
            "libvpx", "A library for encoding and decoding video.", "library", "video");

        Assert.Equal(ProjectKind.Library, classified.Kind);
    }

    [Fact]
    public void Reading_material_is_not_given_a_kind_at_all()
    {
        // "Awesome video tools" is a list. Calling it a video program would be worse than
        // admitting RepoDeck does not know.
        var classified = Classify(
            "awesome-video", "A curated list of video tools.", "awesome", "video");

        Assert.Equal(ProjectKind.Unknown, classified.Kind);
        Assert.Contains(classified.Reasons, r => r.Contains("reading material", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nothing_to_go_on_produces_unknown_rather_than_a_guess()
    {
        var classified = Classify("xyzzy", "A thing that does stuff.");

        Assert.Equal(ProjectKind.Unknown, classified.Kind);
        Assert.Equal(Confidence.Unknown, classified.Confidence);
        Assert.NotEmpty(classified.Reasons);
    }

    [Fact]
    public void Metadata_alone_never_claims_certainty()
    {
        foreach (var name in new[] { "timber", "shotcut", "retroarch", "ripgrep" })
        {
            var classified = Classify(name, "A music player, video editor, emulator and cli tool.");

            Assert.NotEqual(Confidence.Confirmed, classified.Confidence);
        }
    }

    [Fact]
    public void Every_classification_can_say_why()
    {
        var classified = Classify("timber", "Material Design Music Player.", "music-player");

        Assert.NotEmpty(classified.Reasons);
        Assert.False(string.IsNullOrWhiteSpace(classified.Label));
    }

    [Fact]
    public void A_near_tie_says_so_rather_than_picking_one_and_keeping_quiet()
    {
        var classified = Classify("media", "A music and video player.");

        Assert.Contains(classified.Reasons, r => r.Contains("could also be", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(ProjectKind.DesktopApplication, true)]
    [InlineData(ProjectKind.GameOrEmulator, true)]
    [InlineData(ProjectKind.Audio, true)]
    [InlineData(ProjectKind.Video, true)]
    [InlineData(ProjectKind.Utility, true)]
    [InlineData(ProjectKind.CommandLineTool, true)]
    [InlineData(ProjectKind.Library, false)]
    [InlineData(ProjectKind.Unknown, false)]
    public void Only_some_kinds_are_things_a_person_runs(ProjectKind kind, bool runnable)
    {
        Assert.Equal(runnable, new ProjectClassification { Kind = kind }.IsRunnableSoftware);
    }

    [Fact]
    public void Every_kind_has_a_label_fit_for_a_card()
    {
        foreach (var kind in Enum.GetValues<ProjectKind>())
        {
            var label = new ProjectClassification { Kind = kind }.Label;

            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.True(label.Length <= 16, $"{kind} label is too long for a chip: {label}");
        }
    }

    // ---- Refinement by the analyzer --------------------------------------

    [Fact]
    public void The_analyzer_overrides_metadata_when_it_has_looked_inside()
    {
        var refined = ProjectKindClassifier.Refine(
            Classify("mystery", "Something."), ApplicationType.CliTool);

        Assert.Equal(ProjectKind.CommandLineTool, refined.Kind);
        Assert.Equal(Confidence.Confirmed, refined.Confidence);
    }

    [Fact]
    public void Finding_a_desktop_application_confirms_rather_than_replaces_a_subject()
    {
        // "Desktop application" is a shape; "audio" is what it is for. The second is more
        // useful to somebody browsing, so it survives.
        var metadata = Classify("timber", "Material Design Music Player.", "music-player");

        var refined = ProjectKindClassifier.Refine(metadata, ApplicationType.DesktopApplication);

        Assert.Equal(ProjectKind.Audio, refined.Kind);
        Assert.Equal(Confidence.Likely, refined.Confidence);
        Assert.Contains(refined.Reasons, r => r.Contains("looked inside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_analyzer_with_no_opinion_changes_nothing()
    {
        var metadata = Classify("timber", "Material Design Music Player.", "music-player");

        var refined = ProjectKindClassifier.Refine(metadata, ApplicationType.Unknown);

        Assert.Equal(metadata, refined);
    }

    [Fact]
    public void The_classifier_is_deterministic()
    {
        var repository = TestRepositories.Create(
            "shotcut", description: "A free video editor.", topics: ["video", "editor"]);

        var first = ProjectKindClassifier.Classify(repository);
        var second = ProjectKindClassifier.Classify(repository);

        Assert.Equal(first.Kind, second.Kind);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.Reasons, second.Reasons);
    }
}
