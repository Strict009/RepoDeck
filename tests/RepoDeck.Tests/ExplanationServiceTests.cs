using RepoDeck.Models;
using RepoDeck.Services.Explanation;

namespace RepoDeck.Tests;

public class ExplanationServiceTests
{
    private readonly HeuristicRepositoryExplanationService _service = new();

    [Fact]
    public void Card_explanation_uses_the_repository_description()
    {
        var repository = TestRepositories.Create(description: "Edits video files");

        var explanation = _service.ExplainFromMetadata(repository);

        Assert.Equal("Edits video files.", explanation.WhatItIs);
        Assert.Equal(ExplanationSource.RepositoryDescription, explanation.Source);
        Assert.Equal(Confidence.Confirmed, explanation.Confidence);
    }

    [Fact]
    public void A_missing_description_is_admitted_rather_than_invented()
    {
        var repository = TestRepositories.Create(description: null);

        var explanation = _service.ExplainFromMetadata(repository);

        Assert.Equal(ExplanationSource.Metadata, explanation.Source);
        Assert.Equal(Confidence.Unknown, explanation.Confidence);
        Assert.Contains("cannot say", explanation.WhatItIs);
    }

    [Fact]
    public void The_original_description_is_always_preserved_separately()
    {
        var repository = TestRepositories.Create(description: "**Bold** claim about software");

        var explanation = _service.ExplainFromMetadata(repository);

        Assert.Equal("**Bold** claim about software", explanation.OriginalDescription);
        Assert.Equal("Bold claim about software.", explanation.WhatItIs);
    }

    [Fact]
    public async Task A_longer_readme_introduction_supplements_a_short_description()
    {
        var details = new RepositoryDetails
        {
            Repository = TestRepositories.Create(description: "A video editor"),
            ReadmeMarkdown = "# Shotcut\n\nShotcut is a free, open source, cross-platform video editor "
                             + "with support for a wide range of formats and a timeline interface."
        };

        var explanation = await _service.ExplainAsync(details);

        Assert.Equal(ExplanationSource.Readme, explanation.Source);
        Assert.Contains("A video editor.", explanation.WhatItIs);
        Assert.Contains("Shotcut is a free", explanation.WhatItIs);
    }

    [Fact]
    public async Task A_readme_is_used_when_there_is_no_description_at_all()
    {
        var details = new RepositoryDetails
        {
            Repository = TestRepositories.Create(description: null),
            ReadmeMarkdown = "# Tool\n\nThis tool converts spreadsheets into tidy reports without any fuss."
        };

        var explanation = await _service.ExplainAsync(details);

        Assert.Equal(ExplanationSource.Readme, explanation.Source);
        Assert.Equal(Confidence.Likely, explanation.Confidence);
    }

    [Fact]
    public async Task Nothing_to_go_on_produces_an_honest_admission()
    {
        var details = new RepositoryDetails
        {
            Repository = TestRepositories.Create(description: null),
            ReadmeMarkdown = null
        };

        var explanation = await _service.ExplainAsync(details);

        Assert.Equal(ExplanationSource.None, explanation.Source);
        Assert.Equal(Confidence.Unknown, explanation.Confidence);
    }

    [Fact]
    public async Task Highlights_state_when_there_is_no_licence()
    {
        var details = new RepositoryDetails
        {
            Repository = TestRepositories.Create(licenseSpdx: null)
        };

        var explanation = await _service.ExplainAsync(details);

        Assert.Contains(explanation.Highlights, h => h.Contains("No licence detected"));
    }

    [Fact]
    public async Task Highlights_never_imply_that_popularity_means_safety()
    {
        var details = new RepositoryDetails { Repository = TestRepositories.Create(stars: 90_000) };

        var explanation = await _service.ExplainAsync(details);

        Assert.Contains(explanation.Highlights, h => h.Contains("not a safety guarantee"));
    }

    [Fact]
    public async Task Release_downloads_are_described_when_present()
    {
        var details = new RepositoryDetails
        {
            Repository = TestRepositories.Create(),
            Releases = [TestRepositories.Release("v2.0", false, "app-win-x64.zip", "app-linux-x64.tar.gz")]
        };

        var explanation = await _service.ExplainAsync(details);

        Assert.Contains(explanation.Highlights, h => h.Contains("2 downloadable files"));
    }

    [Fact]
    public async Task Archived_projects_are_flagged_in_the_purpose_text()
    {
        var details = new RepositoryDetails { Repository = TestRepositories.Create(archived: true) };

        var explanation = await _service.ExplainAsync(details);

        Assert.Contains("archived", explanation.WhatYouCanDoWithIt, StringComparison.OrdinalIgnoreCase);
    }
}
