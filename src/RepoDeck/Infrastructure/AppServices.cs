using System.Net;
using System.Net.Http.Headers;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.GitHub;

namespace RepoDeck.Infrastructure;

/// <summary>
/// RepoDeck's composition root: the single place where services are built and wired.
/// </summary>
/// <remarks>
/// Deliberately hand-rolled rather than a DI container. At this size a container would
/// add a dependency and a layer of indirection without removing any real work, and
/// because construction happens in exactly one file, introducing a container later is
/// a one-file change rather than a rewrite.
/// </remarks>
public sealed class AppServices : IDisposable
{
    public const string UserAgent = "RepoDeck/0.1 (+https://github.com)";

    private readonly HttpClient _http;

    public AppServices(string? dataRootOverride = null)
    {
        Paths = new AppPaths(dataRootOverride);
        Paths.EnsureCreated();

        Log = new FileAppLog(Paths);
        Cache = new ResponseCache();
        Tokens = new GitHubTokenProvider();

        _http = CreateHttpClient();
        GitHub = new GitHubClient(_http, Cache, Tokens, Log);
        Explanations = new HeuristicRepositoryExplanationService();

        Log.Info("App", $"RepoDeck starting on {PlatformInfo.CurrentDescription}. Data root: {Paths.Root}");
        Log.Info("App", Tokens.HasToken
            ? "Using a GitHub token from the environment."
            : "No GitHub token configured; using unauthenticated rate limits.");
    }

    public AppPaths Paths { get; }
    public IAppLog Log { get; }
    public ResponseCache Cache { get; }
    public GitHubTokenProvider Tokens { get; }
    public IGitHubClient GitHub { get; }
    public IRepositoryExplanationService Explanations { get; }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        var http = new HttpClient(handler)
        {
            // The trailing slash matters: relative request URIs are resolved against it.
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        // GitHub rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return http;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
