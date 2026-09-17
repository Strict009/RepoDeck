namespace RepoDeck.Models;

/// <summary>Where a piece of evidence came from, so the user can weigh it.</summary>
public enum EvidenceSource
{
    /// <summary>GitHub repository metadata: language, topics, licence, dates.</summary>
    Metadata,

    /// <summary>A file seen in the repository's file listing.</summary>
    FileStructure,

    /// <summary>The contents of a project file RepoDeck read.</summary>
    FileContents,

    /// <summary>A published release or one of its assets.</summary>
    Release,

    /// <summary>The README.</summary>
    Readme
}

/// <summary>
/// One observed fact that supports a conclusion.
/// </summary>
/// <remarks>
/// Every classification RepoDeck makes carries these. If a conclusion cannot produce
/// evidence, it should not be stated - which is why the analyzer builds the evidence
/// list as it reasons rather than writing an explanation afterwards.
/// </remarks>
public sealed record Evidence(string Text, EvidenceSource Source, bool Supports = true)
{
    /// <summary>A fact that argues against the conclusion.</summary>
    public static Evidence Against(string text, EvidenceSource source) => new(text, source, Supports: false);

    public string Marker => Supports ? "✓" : "✗";
}
