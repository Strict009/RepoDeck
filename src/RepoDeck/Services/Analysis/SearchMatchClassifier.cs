using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// Separates strong software matches from relevant but uncertain repositories.
/// </summary>
/// <remarks>
/// The classifier extends the existing application-likelihood and relevance layers; it
/// does not replace them. It uses only data already held by the card, so classifying a
/// page of results costs no GitHub requests. When Quick Look later supplies confirmed
/// release and file evidence, the same method can refine that one card.
/// </remarks>
public static class SearchMatchClassifier
{
    private const int StrongMatchThreshold = 60;

    public static SearchMatchAssessment Assess(
        GitHubRepository repository,
        string query,
        ApplicationLikelihood likelihood,
        ProjectClassification classification,
        Installability installability,
        Relevance relevance)
    {
        var hasQueryEvidence = HasQueryEvidence(relevance);
        var looksRunnable = classification.IsRunnableSoftware || likelihood.LooksLikeApplication;
        var ruledOut = classification.Kind == ProjectKind.Library
                       || installability.State is InstallabilityState.DeveloperFocused
                           or InstallabilityState.NotCompatible;

        var strong = hasQueryEvidence
                     && looksRunnable
                     && !ruledOut
                     && !repository.IsArchived
                     && relevance.Score >= StrongMatchThreshold;

        var reasons = strong
            ? StrongReasons(repository, query, classification, installability, relevance)
            : OtherReasons(repository, query, likelihood, classification, installability, relevance);

        return new SearchMatchAssessment
        {
            Group = strong ? SearchResultGroup.BestMatch : SearchResultGroup.OtherResult,
            Reasons = reasons.Take(5).ToList()
        };
    }

    private static IReadOnlyList<string> StrongReasons(
        GitHubRepository repository,
        string query,
        ProjectClassification classification,
        Installability installability,
        Relevance relevance)
    {
        var reasons = new List<string>
        {
            classification.IsRunnableSoftware
                ? $"Appears to be an end-user {classification.Label.ToLowerInvariant()}."
                : "Appears to be an end-user application."
        };

        AddQueryReason(reasons, query, relevance);
        AddInstallabilityReason(reasons, installability);

        if (repository.LastActivity is not null)
        {
            reasons.Add($"Last project activity reported by GitHub: "
                        + $"{repository.LastActivity.Value:yyyy-MM-dd}.");
        }

        return Distinct(reasons);
    }

    private static IReadOnlyList<string> OtherReasons(
        GitHubRepository repository,
        string query,
        ApplicationLikelihood likelihood,
        ProjectClassification classification,
        Installability installability,
        Relevance relevance)
    {
        var reasons = new List<string> { "Relevant repository returned by GitHub." };
        AddQueryReason(reasons, query, relevance);

        var readingMaterial = likelihood.Reasons.FirstOrDefault(r =>
            r.Contains("reading material", StringComparison.OrdinalIgnoreCase));
        if (readingMaterial is not null) reasons.Add(readingMaterial);

        if (classification.Kind == ProjectKind.Library
            || installability.State == InstallabilityState.DeveloperFocused)
        {
            reasons.Add("Appears primarily intended for developers rather than end users.");
        }
        else if (!classification.IsRunnableSoftware && !likelihood.LooksLikeApplication)
        {
            reasons.Add("RepoDeck does not yet have enough evidence that this is an end-user application.");
        }

        AddInstallabilityReason(reasons, installability);

        if (repository.IsArchived)
        {
            reasons.Add("The repository is archived and no longer maintained.");
        }

        return Distinct(reasons);
    }

    private static void AddQueryReason(List<string> reasons, string query, Relevance relevance)
    {
        if (!HasQueryEvidence(relevance)) return;

        var trimmed = query.Trim();
        var displayed = trimmed.Length <= 80 ? trimmed : trimmed[..77] + "…";
        reasons.Add(trimmed.Length == 0
            ? "Matches the selected discovery category."
            : $"Matches \"{displayed}\" in its name, description or GitHub tags.");
    }

    private static void AddInstallabilityReason(List<string> reasons, Installability installability)
    {
        switch (installability.State)
        {
            case InstallabilityState.ReadyToInstall:
                reasons.Add("RepoDeck identified a packaged release for this computer.");
                break;
            case InstallabilityState.NeedsSetup:
                reasons.Add("A packaged download was identified, but setup must be finished manually.");
                break;
            case InstallabilityState.NotCompatible:
                reasons.Add("No compatible package was found for this computer.");
                break;
            case InstallabilityState.DeveloperFocused:
                reasons.Add("No end-user installation path is evident from the available information.");
                break;
            default:
                reasons.Add("Packaged download availability has not been checked yet.");
                break;
        }
    }

    private static bool HasQueryEvidence(Relevance relevance) =>
        relevance.Reasons.Any(r =>
            r.Contains("searched for", StringComparison.OrdinalIgnoreCase)
            || r.Contains("whole search", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> Distinct(IEnumerable<string> reasons) =>
        reasons.Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
