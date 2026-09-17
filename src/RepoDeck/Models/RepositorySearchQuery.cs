namespace RepoDeck.Models;

public enum RepositorySort
{
    BestMatch,
    Stars,
    RecentlyUpdated,
    Forks
}

public enum UpdatedWithin
{
    Any,
    PastMonth,
    PastSixMonths,
    PastYear,
    PastTwoYears
}

/// <summary>
/// Everything the Discover page needs to ask GitHub a question. Kept as a value object
/// so the query-string building is pure, testable logic (see GitHubSearchQueryBuilder).
/// </summary>
public sealed record RepositorySearchQuery
{
    public string Text { get; init; } = "";
    public string? Language { get; init; }
    public int? MinStars { get; init; }
    public UpdatedWithin UpdatedWithin { get; init; } = UpdatedWithin.Any;
    public RepositorySort Sort { get; init; } = RepositorySort.BestMatch;
    public bool ExcludeArchived { get; init; }
    public int Page { get; init; } = 1;
    public int PerPage { get; init; } = 30;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Text)
        && string.IsNullOrWhiteSpace(Language)
        && MinStars is null;
}
