using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

/// <summary>
/// How likely a search result is to be the thing the user meant to find.
/// </summary>
/// <remarks>
/// <para>
/// This answers one question and no others. It is <b>not</b> a quality rating, <b>not</b> a
/// trust or safety rating, and <b>not</b> a popularity rating. Stars are deliberately not an
/// input: a wildly popular library is still the wrong answer for somebody who typed
/// "music player", and treating popularity as relevance is how a search stops surfacing
/// the small useful thing that does exactly what was asked.
/// </para>
/// <para>
/// Every signal is free. The scorer sees only what a search response already contains -
/// name, description, tags, language, archived flag - plus classifications RepoDeck
/// computes locally from those. Nothing here may ever cost a request, because the cost of
/// ranking thirty results must stay zero.
/// </para>
/// <para>
/// Deterministic and pure: the same result and the same query always produce the same
/// score, and every adjustment records a reason, so the ordering can be explained rather
/// than merely asserted.
/// </para>
/// </remarks>
public static class RelevanceScorer
{
    /// <summary>Words too common to count as a match.</summary>
    private static readonly string[] StopWords =
    [
        "a", "an", "and", "for", "from", "in", "my", "of", "on", "or", "the", "to",
        "with", "your", "app", "application", "program", "software", "tool", "open",
        "source", "free", "best", "simple", "easy", "want", "need", "that", "this",
        "can", "how", "what", "do", "does", "i", "it", "is", "be"
    ];

    /// <summary>
    /// Scores one result against the query. Higher is more likely to be what was meant.
    /// </summary>
    /// <remarks>
    /// The baseline is 50 and adjustments are bounded, so a result can be pushed around
    /// but never buried: GitHub's own ordering already encodes text relevance RepoDeck
    /// cannot see, and overriding it wholesale would be arrogant.
    /// </remarks>
    public static Relevance Score(
        GitHubRepository repository,
        string query,
        ProjectClassification classification,
        Installability installability,
        MachineProfile machine)
    {
        var reasons = new List<string>();
        var score = 50;

        var terms = Terms(query);
        var name = (repository.Name ?? "").ToLowerInvariant();
        var normalisedName = name.Replace('-', ' ').Replace('_', ' ').Replace('.', ' ');
        var description = (repository.Description ?? "").ToLowerInvariant();
        var topics = repository.Topics.Select(t => t.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        // ---- Does the name say what they asked for? -------------------------
        if (terms.Count > 0)
        {
            var joined = string.Join(" ", terms);

            if (normalisedName == joined || name == joined.Replace(" ", "-"))
            {
                score += 22;
                reasons.Add("Its name is exactly what you searched for.");
            }
            else
            {
                var inName = terms.Count(t => normalisedName.Contains(t, StringComparison.Ordinal));

                if (inName == terms.Count && terms.Count > 1)
                {
                    score += 16;
                    reasons.Add("Its name contains every word you searched for.");
                }
                else if (inName > 0)
                {
                    score += 8;
                    reasons.Add("Its name matches part of your search.");
                }
            }

            var inTopics = terms.Count(t => topics.Contains(t));
            if (inTopics > 0)
            {
                score += Math.Min(inTopics * 5, 10);
                reasons.Add("It is tagged with what you searched for.");
            }

            var inDescription = terms.Count(t => description.Contains(t, StringComparison.Ordinal));
            if (inDescription == terms.Count && terms.Count > 0)
            {
                score += 6;
                reasons.Add("Its description covers your whole search.");
            }
        }

        // ---- Is it the kind of thing a person runs? -------------------------
        switch (classification.Kind)
        {
            case ProjectKind.Library:
                score -= 18;
                reasons.Add("It looks like a library for programmers rather than a program to run.");
                break;

            case ProjectKind.DeveloperTool:
                score -= 4;
                reasons.Add("It looks like a tool for software development.");
                break;

            case ProjectKind.ThemeOrSkin:
                score -= 18;
                reasons.Add("It looks like a theme or skin for another application.");
                break;

            case ProjectKind.Unknown:
                score -= 6;
                reasons.Add("RepoDeck could not tell what kind of project this is.");
                break;

            default:
                score += 10;
                reasons.Add($"It looks like {Article(classification.Label)} you can run.");
                break;
        }

        // ---- Can it be used on this machine? --------------------------------
        switch (installability.State)
        {
            case InstallabilityState.ReadyToInstall:
                score += 16;
                reasons.Add("RepoDeck has a plan to install it on this computer.");
                break;

            case InstallabilityState.NeedsSetup:
                score += 6;
                reasons.Add("RepoDeck can fetch it, though you would finish the setup.");
                break;

            case InstallabilityState.NotCompatible:
                score -= 20;
                reasons.Add($"It offers nothing for {machine.Description}.");
                break;

            case InstallabilityState.DeveloperFocused:
                score -= 14;
                reasons.Add("It would have to be built rather than installed.");
                break;
        }

        // ---- Is anybody still looking after it? -----------------------------
        if (repository.IsArchived)
        {
            score -= 12;
            reasons.Add("The authors have stopped maintaining it.");
        }

        // ---- Is it software at all? -----------------------------------------
        if (LooksLikeReadingMaterial(topics, normalisedName, description))
        {
            score -= 24;
            reasons.Add("It looks like a list or a set of notes rather than software.");
        }

        return new Relevance
        {
            Score = Math.Clamp(score, 0, 100),
            Reasons = reasons.Take(5).ToList()
        };
    }

    /// <summary>
    /// The score available before anything has been analysed, which is what the grid has
    /// for every result. Installability is Unknown at this point and contributes nothing.
    /// </summary>
    public static Relevance ScoreFromMetadata(
        GitHubRepository repository, string query, MachineProfile machine) =>
        Score(repository, query, ProjectKindClassifier.Classify(repository),
            Installability.Unknown, machine);

    /// <summary>The meaningful words in a query, lowercased, stop words removed.</summary>
    public static IReadOnlyList<string> Terms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        return query.ToLowerInvariant()
            .Split([' ', '-', '_', '.', ',', '"', '\'', '(', ')', '/'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 1 && !StopWords.Contains(t, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();
    }

    private static bool LooksLikeReadingMaterial(
        IReadOnlySet<string> topics, string name, string description)
    {
        string[] markers =
        [
            "awesome", "awesome-list", "curated", "tutorial", "course", "cheatsheet",
            "roadmap", "interview", "dotfiles", "book", "notes"
        ];

        if (markers.Any(topics.Contains)) return true;

        return name.StartsWith("awesome", StringComparison.Ordinal)
               || description.Contains("curated list", StringComparison.Ordinal)
               || description.Contains("list of", StringComparison.Ordinal);
    }

    private static string Article(string label) =>
        "aeiouAEIOU".Contains(label[0]) ? "an " + label.ToLowerInvariant() : "a " + label.ToLowerInvariant();
}

/// <summary>A relevance score and the reasons for it.</summary>
/// <remarks>
/// Carried on every card so the ordering can be explained. It is never displayed as a
/// number and never described as a rating: a person seeing "87" beside a project would
/// read it as a verdict on the software, which is precisely what it is not.
/// </remarks>
public sealed record Relevance
{
    /// <summary>0 to 100, where 50 is "nothing either way". Internal to ordering.</summary>
    public int Score { get; init; } = 50;

    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>
    /// Whether RepoDeck has positive evidence this is a usable program, which is the
    /// question Apps mode asks. Deliberately stricter than the score alone.
    /// </summary>
    public bool LooksLikeSomethingYouCanUse => Score >= 55;

    public static Relevance Neutral { get; } = new();
}
