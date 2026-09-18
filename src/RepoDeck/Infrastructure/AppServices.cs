using System.Net;
using System.Net.Http.Headers;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Media;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.GitHub;
using RepoDeck.Services.Install;
using RepoDeck.Services.Favorites;
using RepoDeck.Services.History;
using RepoDeck.Services.Preferences;
using RepoDeck.Services.Update;

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
        Machine = PlatformInfo.CurrentMachine();
        Analyzer = new RepositoryAnalyzerService(GitHub, Log);
        InstallPlanner = new InstallPlanner(Paths);

        InstalledApps = new InstalledAppStore(Paths, Log);
        Downloads = new DownloadService(_http, Paths, Log);
        Transfers = new TransferRegistry();
        Preferences = new UserPreferences(Paths, Log);
        Favorites = new FavoritesStore(Paths, Log);
        History = new LifecycleHistory(Paths, Log);

        var installer = new InstallationService(
            Downloads, new ExtractionService(Log), InstalledApps, Paths, Log,
            transfers: Transfers, history: History);

        Installer = installer;
        Launcher = new LaunchService(Paths, InstalledApps, Log, History);
        Media = new RepositoryMediaService(Log);
        Images = new ImageLoader(Log);

        RunningApplications = new RunningApplicationDetector(Log);
        HealthChecker = new InstallationHealthChecker(Paths, Log);
        UpdateChecker = new UpdateChecker(GitHub, Machine, Log);

        var updater = new UpdateService(
            Downloads, new ExtractionService(Log), InstalledApps, RunningApplications,
            History, Machine, Paths, Log, transfers: Transfers);

        Updater = updater;

        // An installation that never promoted out of staging is not an installation;
        // its remains should not accumulate across runs.
        installer.CleanAbandonedStaging();
        updater.CleanAbandonedRollbacks();

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

    /// <summary>The machine every compatibility decision is made against.</summary>
    public MachineProfile Machine { get; }

    public IRepositoryAnalyzerService Analyzer { get; }
    public InstallPlanner InstallPlanner { get; }

    public IInstalledAppStore InstalledApps { get; }
    public IDownloadService Downloads { get; }
    public IInstallationService Installer { get; }
    public LaunchService Launcher { get; }
    public IRepositoryMediaService Media { get; }
    public ImageLoader Images { get; }

    /// <summary>Interface preferences only. Nothing here affects what RepoDeck installs.</summary>
    public IUserPreferences Preferences { get; }

    /// <summary>Projects the user asked RepoDeck to remember. Independent of what is installed.</summary>
    public IFavoritesStore Favorites { get; }

    /// <summary>What RepoDeck has done, in plain English, for the user to read.</summary>
    public ILifecycleHistory History { get; }

    /// <summary>What RepoDeck is fetching right now, or recently tried to. In memory only.</summary>
    public ITransferRegistry Transfers { get; }

    public IRunningApplicationDetector RunningApplications { get; }
    public IInstallationHealthChecker HealthChecker { get; }
    public IUpdateChecker UpdateChecker { get; }
    public IUpdateService Updater { get; }

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
        Images.Dispose();
        _http.Dispose();
    }
}
