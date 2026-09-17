namespace RepoDeck.Models;

/// <summary>Where a piece of explanation text came from, so the UI can be honest about it.</summary>
public enum ExplanationSource
{
    /// <summary>Nothing usable was available.</summary>
    None,

    /// <summary>The repository's own one-line GitHub description.</summary>
    RepositoryDescription,

    /// <summary>Derived from the README body.</summary>
    Readme,

    /// <summary>Assembled from structured metadata (topics, language, releases).</summary>
    Metadata,

    /// <summary>Produced by an AI provider. Not used in the MVP.</summary>
    AiProvider
}

/// <summary>
/// A plain-English account of a repository for someone who does not write software.
/// The original repository description is always preserved separately so the UI can
/// show GitHub's own words alongside RepoDeck's interpretation.
/// </summary>
public sealed record RepositoryExplanation
{
    /// <summary>Short headline, e.g. "A desktop app for editing video".</summary>
    public string Headline { get; init; } = "";

    /// <summary>"What is this?" - a couple of sentences.</summary>
    public string WhatItIs { get; init; } = "";

    /// <summary>"What can I do with it?" - practical purpose.</summary>
    public string WhatYouCanDoWithIt { get; init; } = "";

    /// <summary>Short factual bullet points (language, licence, activity).</summary>
    public IReadOnlyList<string> Highlights { get; init; } = [];

    /// <summary>GitHub's own description, unmodified. May be null.</summary>
    public string? OriginalDescription { get; init; }

    public ExplanationSource Source { get; init; } = ExplanationSource.None;
    public Confidence Confidence { get; init; } = Confidence.Unknown;
}
