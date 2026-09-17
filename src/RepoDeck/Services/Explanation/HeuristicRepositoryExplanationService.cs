using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Readme;

namespace RepoDeck.Services.Explanation;

/// <summary>
/// Builds plain-English explanations from repository description, README, topics and
/// languages. No AI, no network calls of its own, entirely deterministic.
/// </summary>
public sealed class HeuristicRepositoryExplanationService : IRepositoryExplanationService
{
    public RepositoryExplanation ExplainFromMetadata(GitHubRepository repository)
    {
        var description = Normalise(repository.Description);
        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);

        var whatItIs = description
            ?? "This project does not describe itself on GitHub, so RepoDeck cannot say "
               + "what it does without looking inside it.";

        return new RepositoryExplanation
        {
            Headline = BuildHeadline(repository, likelihood),
            WhatItIs = whatItIs,
            WhatYouCanDoWithIt = BuildPurpose(repository, likelihood, []),
            Highlights = BuildHighlights(repository, [], null),
            OriginalDescription = repository.Description,
            Source = description is null ? ExplanationSource.Metadata : ExplanationSource.RepositoryDescription,
            Confidence = description is null ? Confidence.Unknown : Confidence.Confirmed
        };
    }

    public Task<RepositoryExplanation> ExplainAsync(
        RepositoryDetails details, CancellationToken cancellationToken = default)
    {
        var repository = details.Repository;
        var description = Normalise(repository.Description);
        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);
        var headings = ReadmeParser.ExtractHeadings(details.ReadmeMarkdown);
        var readmeIntro = ReadmeParser.ExtractIntroduction(details.ReadmeMarkdown);

        string whatItIs;
        ExplanationSource source;
        Confidence confidence;

        if (description is not null && readmeIntro is not null && readmeIntro.Length > description.Length + 40)
        {
            // Both exist and the README says appreciably more: show both, description first.
            whatItIs = description + "\n\n" + readmeIntro;
            source = ExplanationSource.Readme;
            confidence = Confidence.Confirmed;
        }
        else if (description is not null)
        {
            whatItIs = description;
            source = ExplanationSource.RepositoryDescription;
            confidence = Confidence.Confirmed;
        }
        else if (readmeIntro is not null)
        {
            whatItIs = readmeIntro;
            source = ExplanationSource.Readme;
            confidence = Confidence.Likely;
        }
        else
        {
            whatItIs = "This project has no description and no readable introduction in its README, "
                       + "so RepoDeck cannot summarise it. Opening it on GitHub is the quickest way to find out more.";
            source = ExplanationSource.None;
            confidence = Confidence.Unknown;
        }

        var explanation = new RepositoryExplanation
        {
            Headline = BuildHeadline(repository, likelihood),
            WhatItIs = whatItIs,
            WhatYouCanDoWithIt = BuildPurpose(repository, likelihood, headings),
            Highlights = BuildHighlights(repository, details.Releases, details.LanguageShares()),
            OriginalDescription = repository.Description,
            Source = source,
            Confidence = confidence
        };

        return Task.FromResult(explanation);
    }

    // ---- Building blocks --------------------------------------------------

    private static string BuildHeadline(GitHubRepository repository, ApplicationLikelihood likelihood)
    {
        var kind = likelihood.Score switch
        {
            >= 70 => "An application",
            >= 50 => "Possibly an application",
            >= 30 => "A software project",
            _ => "A developer resource"
        };

        return repository.Language is { Length: > 0 } language
            ? $"{kind}, written mainly in {language}"
            : kind;
    }

    private static string BuildPurpose(
        GitHubRepository repository,
        ApplicationLikelihood likelihood,
        IReadOnlyList<string> readmeHeadings)
    {
        var sentences = new List<string>
        {
            likelihood.Score switch
            {
                >= 70 => "This looks like something you can install and run yourself.",
                >= 50 => "This may be something you can run, but RepoDeck is not certain yet.",
                >= 30 => "RepoDeck cannot tell from the description alone whether this can be run directly.",
                _ => "This looks like material for developers rather than a program you run."
            }
        };

        if (repository.Topics.Count > 0)
        {
            var topics = repository.Topics.Take(4);
            sentences.Add("The authors describe it as: " + string.Join(", ", topics) + ".");
        }

        var actionHeadings = readmeHeadings.Where(LooksLikeUsageHeading).Take(3).ToList();
        if (actionHeadings.Count > 0)
        {
            var lowered = actionHeadings.Select(h => h.ToLowerInvariant());
            sentences.Add("Its README covers " + string.Join(", ", lowered) + ".");
        }

        if (repository.IsArchived)
        {
            sentences.Add("Note that the project is archived, so it is no longer being worked on.");
        }

        return string.Join(" ", sentences);
    }

    private static bool LooksLikeUsageHeading(string heading)
    {
        string[] words = ["install", "usage", "getting started", "quick start", "how to", "download", "features", "setup"];
        var lower = heading.ToLowerInvariant();
        return words.Any(w => lower.Contains(w, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> BuildHighlights(
        GitHubRepository repository,
        IReadOnlyList<GitHubRelease> releases,
        IReadOnlyList<(string Language, double Share)>? languages)
    {
        var highlights = new List<string>();

        if (languages is { Count: > 0 })
        {
            var top = languages.Take(3).Select(l => l.Language + " " + Humanize.Percent(l.Share));
            highlights.Add("Built with " + string.Join(", ", top) + ".");
        }
        else if (repository.Language is { Length: > 0 })
        {
            highlights.Add($"Written mainly in {repository.Language}.");
        }

        highlights.Add(repository.HasLicense
            ? $"Licensed under {repository.LicenseSpdxId ?? repository.LicenseName}."
            : "No licence detected, which means the authors have not said how you may use it.");

        highlights.Add(repository.LastActivity is null
            ? "RepoDeck could not determine when this was last worked on."
            : "Last updated " + Humanize.RelativeTime(repository.LastActivity) + ".");

        if (releases.Count > 0)
        {
            var latest = releases.FirstOrDefault(r => r.IsStable) ?? releases[0];
            var assetCount = latest.Assets.Count;
            var plural = assetCount == 1 ? "" : "s";
            highlights.Add(assetCount > 0
                ? $"Latest release {latest.TagName} includes {assetCount} downloadable file{plural}."
                : $"Latest release {latest.TagName} has no attached downloads, only source code.");
        }
        else
        {
            highlights.Add("No published releases, so there is nothing pre-built to download.");
        }

        highlights.Add(Humanize.Count(repository.Stars)
                       + " people have starred it on GitHub. Popularity is not a safety guarantee.");

        return highlights;
    }

    private static string? Normalise(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;

        var cleaned = ReadmeParser.CleanInline(description);
        if (cleaned.Length == 0) return null;

        // Give the sentence a full stop so it reads as prose rather than a label.
        return char.IsLetterOrDigit(cleaned[^1]) ? cleaned + "." : cleaned;
    }
}
