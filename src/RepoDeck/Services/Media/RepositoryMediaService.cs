using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Readme;

namespace RepoDeck.Services.Media;

/// <summary>
/// Finds pictures worth showing for a project.
/// </summary>
/// <remarks>
/// Costs no extra GitHub requests. The README and the file listing have both already been
/// fetched by the analyzer, and GitHub's social preview is a predictable address that
/// needs no API call at all. Nothing is downloaded here - candidates are identified and
/// ranked from their addresses, and only the winner is ever fetched, by the image loader,
/// when something actually needs to display it.
/// </remarks>
public interface IRepositoryMediaService
{
    /// <summary>Media from a full analysis: README text plus the repository file listing.</summary>
    RepositoryMedia Discover(GitHubRepository repository, string? readmeMarkdown, RepositoryTree tree);

    /// <summary>The cheap version for a search result, where only metadata is known.</summary>
    RepositoryMedia DiscoverFromMetadata(GitHubRepository repository);
}

public sealed class RepositoryMediaService : IRepositoryMediaService
{
    /// <summary>Folders worth scanning. Anything deeper is not worth the noise.</summary>
    private static readonly string[] MediaFolders =
    [
        "screenshots/", "screenshot/", "images/", "img/", "assets/",
        "docs/", "doc/", "media/", ".github/"
    ];

    private static readonly string[] ImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".webp", ".gif"
    ];

    private const int MaxFilesConsidered = 60;

    private readonly IAppLog _log;

    public RepositoryMediaService(IAppLog log)
    {
        _log = log;
    }

    public RepositoryMedia Discover(
        GitHubRepository repository, string? readmeMarkdown, RepositoryTree tree)
    {
        var candidates = new List<MediaCandidate>();

        candidates.AddRange(FromReadme(repository, readmeMarkdown));
        candidates.AddRange(FromTree(repository, tree));
        candidates.Add(SocialPreview(repository));

        var media = MediaRanker.Rank(candidates);

        _log.Info("Media", $"{repository.FullName}: {candidates.Count} image candidates, "
                           + $"{media.Candidates.Count} kept"
                           + (media.Primary is null ? "" : $", showing {media.Primary.Kind}"));

        return media;
    }

    public RepositoryMedia DiscoverFromMetadata(GitHubRepository repository) =>
        MediaRanker.Rank([SocialPreview(repository)]);

    /// <summary>
    /// GitHub serves a preview card for every repository at a predictable address -
    /// the maintainer's uploaded image when there is one, a generated card otherwise.
    /// No API call, no rate limit, and never worse than an empty tile.
    /// </summary>
    private static MediaCandidate SocialPreview(GitHubRepository repository) => new()
    {
        Url = $"https://opengraph.githubassets.com/1/{repository.OwnerLogin}/{repository.Name}",
        Source = MediaSource.SocialPreview,
        Description = repository.Name
    };

    private static IEnumerable<MediaCandidate> FromReadme(
        GitHubRepository repository, string? markdown)
    {
        foreach (var image in ReadmeImageExtractor.Extract(markdown))
        {
            var url = ResolveUrl(repository, image.Url);
            if (url is null) continue;

            yield return new MediaCandidate
            {
                Url = url,
                Source = MediaSource.Readme,
                Description = image.AltText
            };
        }
    }

    private static IEnumerable<MediaCandidate> FromTree(
        GitHubRepository repository, RepositoryTree tree)
    {
        var branch = repository.DefaultBranch ?? "HEAD";

        var files = tree.Entries
            .Where(e => e.IsFile)
            .Where(e => ImageExtensions.Any(x =>
                e.Path.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
            .Where(e => IsWorthConsidering(e.Path))
            .OrderByDescending(e => e.Size ?? 0)
            .Take(MaxFilesConsidered)
            .ToList();

        foreach (var file in files)
        {
            yield return new MediaCandidate
            {
                Url = RawUrl(repository, branch, file.Path),
                Source = MediaSource.RepositoryFile,
                Description = Path.GetFileNameWithoutExtension(file.Path),
                SizeBytes = file.Size
            };
        }
    }

    /// <summary>
    /// Only images in a folder that suggests documentation, or at the very top level.
    /// Scanning every image in a repository would drown the useful ones in test fixtures
    /// and icon sets.
    /// </summary>
    private static bool IsWorthConsidering(string path)
    {
        var lower = path.ToLowerInvariant();

        if (lower.Contains("node_modules/", StringComparison.Ordinal)) return false;
        if (lower.Contains("/test", StringComparison.Ordinal)) return false;
        if (lower.Contains("fixture", StringComparison.Ordinal)) return false;

        if (!lower.Contains('/')) return true;

        return MediaFolders.Any(f => lower.StartsWith(f, StringComparison.Ordinal)
                                     || lower.Contains("/" + f, StringComparison.Ordinal));
    }

    /// <summary>
    /// Turns a README reference into something fetchable. Relative paths become raw
    /// GitHub addresses; anything that is not plain http(s) is dropped, because a README
    /// is untrusted content and RepoDeck will not follow arbitrary schemes.
    /// </summary>
    private static string? ResolveUrl(GitHubRepository repository, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        // Protocol-relative, data URIs and anything exotic are refused outright.
        if (raw.StartsWith("//", StringComparison.Ordinal)) return null;
        if (raw.Contains(':', StringComparison.Ordinal)) return null;

        var branch = repository.DefaultBranch ?? "HEAD";
        var path = raw.TrimStart('.', '/');

        return path.Length == 0 ? null : RawUrl(repository, branch, path);
    }

    private static string RawUrl(GitHubRepository repository, string branch, string path) =>
        "https://raw.githubusercontent.com/"
        + Uri.EscapeDataString(repository.OwnerLogin) + "/"
        + Uri.EscapeDataString(repository.Name) + "/"
        + Uri.EscapeDataString(branch) + "/"
        + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
