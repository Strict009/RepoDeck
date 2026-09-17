namespace RepoDeck.Models;

public sealed record RepositorySearchResult
{
    public IReadOnlyList<GitHubRepository> Items { get; init; } = [];

    /// <summary>Total matches reported by GitHub. Capped by GitHub at 1000 retrievable results.</summary>
    public int TotalCount { get; init; }

    /// <summary>GitHub timed out its own search and returned a partial set.</summary>
    public bool IncompleteResults { get; init; }

    public int Page { get; init; } = 1;
    public int PerPage { get; init; } = 30;

    /// <summary>
    /// GitHub's search API refuses to page beyond 1000 results, so "more" means
    /// both "more matches exist" and "GitHub will actually hand them over".
    /// </summary>
    public bool HasMore => Page * PerPage < Math.Min(TotalCount, 1000);

    public static RepositorySearchResult Empty { get; } = new();
}
