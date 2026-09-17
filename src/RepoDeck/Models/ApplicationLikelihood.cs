namespace RepoDeck.Models;

/// <summary>
/// How likely a repository is to be something a normal person can run, as opposed to
/// a library, a tutorial, a dotfiles collection or a list of links.
/// </summary>
/// <remarks>
/// This evaluates repository <em>metadata</em> only - it is what the Discover page can
/// afford without an extra API call per card. The deeper file-level classification
/// (RepositoryAnalyzerService) arrives in a later milestone and will supersede the
/// verdict here when it has looked inside the repository.
/// </remarks>
public sealed record ApplicationLikelihood
{
    /// <summary>Score from 0 (clearly not an app) to 100 (very likely a runnable app).</summary>
    public int Score { get; init; }

    public Confidence Confidence { get; init; } = Confidence.Unknown;

    /// <summary>Human-readable evidence. RepoDeck must always be able to say why.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    public bool LooksLikeApplication => Score >= 50;

    public string Verdict => Score switch
    {
        >= 70 => "Looks like an application",
        >= 50 => "Might be an application",
        >= 30 => "Unclear",
        _ => "Looks like a library or resource"
    };
}
