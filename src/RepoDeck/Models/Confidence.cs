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

    /// <summary>One reasonable reading of weak or partial evidence. Weaker than Likely.</summary>
    Possible,

    /// <summary>RepoDeck has no useful evidence either way.</summary>
    Unknown,

    /// <summary>Determinable, but only by looking inside the repository contents.</summary>
    RequiresInspection,

    /// <summary>Positively ruled out by evidence. Not the same as Unknown.</summary>
    Unsupported
}

public static class ConfidenceText
{
    public static string ToDisplayString(this Confidence confidence) => confidence switch
    {
        Confidence.Confirmed => "Confirmed",
        Confidence.Likely => "Likely",
        Confidence.Possible => "Possible",
        Confidence.Unknown => "Unknown",
        Confidence.Unsupported => "Not supported",
        Confidence.RequiresInspection => "Requires inspection",
        _ => "Unknown"
    };
}
