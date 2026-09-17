namespace RepoDeck.Services.GitHub;

/// <summary>
/// Supplies the GitHub token, if the user has configured one.
/// </summary>
/// <remarks>
/// The MVP reads a personal access token from the <c>REPODECK_GITHUB_TOKEN</c>
/// environment variable only. Nothing is written to disk and nothing is committed:
/// storing a token safely (OS credential store / OAuth device flow) is a deliberate
/// later milestone rather than a plaintext file thrown in now.
/// </remarks>
public sealed class GitHubTokenProvider
{
    public const string EnvironmentVariableName = "REPODECK_GITHUB_TOKEN";

    private readonly Func<string?> _reader;

    public GitHubTokenProvider(Func<string?>? reader = null)
    {
        _reader = reader ?? (() => Environment.GetEnvironmentVariable(EnvironmentVariableName));
    }

    public string? GetToken()
    {
        var token = _reader();
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    public bool HasToken => GetToken() is not null;
}
