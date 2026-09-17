namespace RepoDeck.Models;

/// <summary>
/// How sure RepoDeck is about a statement it shows the user. RepoDeck never presents
/// an inference as a fact: every uncertain claim carries one of these.
/// </summary>
public enum Confidence
{
    /// <summary>Directly observed in GitHub data. Safe to state plainly.</summary>
    Confirmed,

    /// <summary>Strongly implied by evidence, but not directly observed.</summary>
    Likely,

    /// <summary>RepoDeck has no useful evidence either way.</summary>
    Unknown,

    /// <summary>Determinable, but only by looking inside the repository contents.</summary>
    RequiresInspection
}

public static class ConfidenceText
{
    public static string ToDisplayString(this Confidence confidence) => confidence switch
    {
        Confidence.Confirmed => "Confirmed",
        Confidence.Likely => "Likely",
        Confidence.Unknown => "Unknown",
        Confidence.RequiresInspection => "Requires inspection",
        _ => "Unknown"
    };
}
