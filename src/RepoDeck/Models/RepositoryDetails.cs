namespace RepoDeck.Models;

/// <summary>
/// The full picture of one repository, assembled from several GitHub endpoints.
/// Any part may be missing: a repository can have no README, no releases and no
/// detectable languages, and the details page must degrade gracefully.
/// </summary>
public sealed record RepositoryDetails
{
    public required GitHubRepository Repository { get; init; }

    /// <summary>Raw README markdown, or null when the repository has none.</summary>
    public string? ReadmeMarkdown { get; init; }

    /// <summary>Language name to bytes of code, as reported by GitHub.</summary>
    public IReadOnlyDictionary<string, long> Languages { get; init; } =
        new Dictionary<string, long>();

    public IReadOnlyList<GitHubRelease> Releases { get; init; } = [];

    public GitHubRelease? LatestStableRelease =>
        Releases.FirstOrDefault(r => r.IsStable) ?? Releases.FirstOrDefault(r => !r.Draft);

    public bool HasReleases => Releases.Count > 0;

    /// <summary>Total share of each language, largest first.</summary>
    public IReadOnlyList<(string Language, double Share)> LanguageShares()
    {
        var total = Languages.Values.Sum();
        if (total <= 0) return [];
        return Languages
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (kv.Key, (double)kv.Value / total))
            .ToList();
    }
}
