namespace RepoDeck.Models;

/// <summary>Where a result belongs in software-focused discovery.</summary>
public enum SearchResultGroup
{
    BestMatch,
    OtherResult
}

/// <summary>
/// A search-result classification and the evidence that supports it.
/// </summary>
/// <remarks>
/// This is not a quality, safety or trust rating. It answers only whether RepoDeck has
/// enough evidence that the repository is an end-user application relevant to this
/// search. The numeric relevance score remains an internal ordering detail.
/// </remarks>
public sealed record SearchMatchAssessment
{
    public SearchResultGroup Group { get; init; } = SearchResultGroup.OtherResult;

    /// <summary>Short factual statements suitable for the card's expandable WHY panel.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    public bool IsBestMatch => Group == SearchResultGroup.BestMatch;

    public string ReasonHeading => IsBestMatch
        ? "Why this is a strong match"
        : "Why this appears under Other Results";

    public static SearchMatchAssessment Other { get; } = new()
    {
        Reasons = ["RepoDeck does not yet have enough evidence to call this a strong software match."]
    };
}
