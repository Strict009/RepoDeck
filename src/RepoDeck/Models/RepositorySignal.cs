namespace RepoDeck.Models;

/// <summary>How a signal should read to the user. Never a verdict, only a colour of fact.</summary>
public enum SignalTone
{
    /// <summary>Plain fact with no implication either way.</summary>
    Neutral,

    /// <summary>Something that makes the project easier or safer to use.</summary>
    Favourable,

    /// <summary>Something worth pausing over before installing or running.</summary>
    Caution
}

/// <summary>
/// One factual statement about a repository for the information panel.
/// </summary>
/// <remarks>
/// RepoDeck deliberately never renders a single overall "safe" or "unsafe" verdict.
/// Stars are popularity, not safety. The panel presents signals and lets the user judge.
/// </remarks>
public sealed record RepositorySignal(string Text, SignalTone Tone)
{
    public bool IsCaution => Tone == SignalTone.Caution;
    public bool IsFavourable => Tone == SignalTone.Favourable;
}
