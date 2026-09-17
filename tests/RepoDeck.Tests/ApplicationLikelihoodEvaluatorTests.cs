using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

public class ApplicationLikelihoodEvaluatorTests
{
    [Fact]
    public void A_tagged_desktop_application_scores_as_an_application()
    {
        var repository = TestRepositories.Create(
            name: "photoflare",
            description: "A cross-platform image editor application for the desktop.",
            language: "C++",
            topics: ["desktop", "gui", "editor"]);

        var result = ApplicationLikelihoodEvaluator.Evaluate(repository);

        Assert.True(result.LooksLikeApplication);
        Assert.Equal("Looks like an application", result.Verdict);
    }

    [Fact]
    public void A_library_is_not_mistaken_for_an_application()
    {
        var repository = TestRepositories.Create(
            name: "serde",
            description: "A serialization framework library for Rust.",
            language: "Rust",
            topics: ["library", "framework", "serialization"]);

        var result = ApplicationLikelihoodEvaluator.Evaluate(repository);

        Assert.False(result.LooksLikeApplication);
    }

    [Fact]
    public void A_curated_link_list_is_recognised_as_not_software()
    {
        var repository = TestRepositories.Create(
            name: "awesome-selfhosted",
            description: "A curated list of awesome self-hosted software.",
            language: null,
            topics: ["awesome", "awesome-list", "curated"]);

        var result = ApplicationLikelihoodEvaluator.Evaluate(repository);

        Assert.False(result.LooksLikeApplication);
        Assert.Equal("Looks like a library or resource", result.Verdict);
    }

    [Fact]
    public void Every_verdict_carries_reasons_that_explain_it()
    {
        var repository = TestRepositories.Create(
            description: "A desktop application for editing video.",
            topics: ["desktop", "video"]);

        var result = ApplicationLikelihoodEvaluator.Evaluate(repository);

        Assert.NotEmpty(result.Reasons);
        Assert.All(result.Reasons, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    [Fact]
    public void A_documentation_repository_scores_low()
    {
        var repository = TestRepositories.Create(
            name: "docs",
            description: "Documentation and tutorials for the project.",
            language: "HTML",
            topics: ["documentation", "tutorial"]);

        var result = ApplicationLikelihoodEvaluator.Evaluate(repository);

        Assert.False(result.LooksLikeApplication);
    }

    [Fact]
    public void A_repository_with_nothing_to_go_on_is_not_confidently_judged()
    {
        var repository = TestRepositories.Create(description: null, language: null, topics: []);

        var result = ApplicationLikelihoodEvaluator.Evaluate(repository);

        Assert.NotEqual(Confidence.Confirmed, result.Confidence);
    }

    [Fact]
    public void Scores_stay_within_range_for_strongly_signalled_repositories()
    {
        var application = ApplicationLikelihoodEvaluator.Evaluate(TestRepositories.Create(
            description: "A desktop app, a launcher, a game client and an editor utility tool.",
            language: "C#",
            topics: ["desktop", "app", "gui", "game", "launcher", "tool"]));

        var resource = ApplicationLikelihoodEvaluator.Evaluate(TestRepositories.Create(
            description: "A curated list of tutorials, a roadmap and a cheat sheet collection of books.",
            language: "Markdown",
            topics: ["awesome", "list", "book", "course", "roadmap", "library"]));

        Assert.InRange(application.Score, 0, 100);
        Assert.InRange(resource.Score, 0, 100);
        Assert.True(application.Score > resource.Score);
    }

    [Fact]
    public void Archived_status_is_reported_as_a_reason()
    {
        var repository = TestRepositories.Create(archived: true);

        var result = ApplicationLikelihoodEvaluator.Evaluate(repository);

        Assert.Contains(result.Reasons, r => r.Contains("archived", StringComparison.OrdinalIgnoreCase));
    }
}
