using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using RepoDeck.Infrastructure;
using RepoDeck.Models;

namespace RepoDeck.Services.GitHub;

/// <summary>
/// HTTP implementation of <see cref="IGitHubClient"/>: the only class in RepoDeck that
/// talks to api.github.com. Responses are cached briefly so that navigating back and
/// forth between pages does not spend the rate limit twice.
/// </summary>
public sealed class GitHubClient : IGitHubClient
{
    private const string ApiVersion = "2022-11-28";

    private static readonly TimeSpan SearchCacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RepositoryCacheLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ContentCacheLifetime = TimeSpan.FromMinutes(30);

    private readonly HttpClient _http;
    private readonly ResponseCache _cache;
    private readonly GitHubTokenProvider _tokens;
    private readonly IAppLog _log;

    public GitHubClient(HttpClient http, ResponseCache cache, GitHubTokenProvider tokens, IAppLog log)
    {
        _http = http;
        _cache = cache;
        _tokens = tokens;
        _log = log;
    }

    public RateLimitStatus RateLimit { get; private set; } = RateLimitStatus.Unknown;

    public event Action<RateLimitStatus>? RateLimitChanged;

    public bool IsAuthenticated => _tokens.HasToken;

    public async Task<RepositorySearchResult> SearchRepositoriesAsync(
        RepositorySearchQuery query, CancellationToken cancellationToken = default)
    {
        var uri = GitHubSearchQueryBuilder.BuildRequestUri(query);

        if (_cache.TryGet<RepositorySearchResult>(uri, out var cached))
        {
            _log.Info("GitHub", "Search served from cache: " + uri);
            return cached;
        }

        var envelope = await GetJsonAsync<SearchEnvelope<GitHubRepository>>(uri, cancellationToken).ConfigureAwait(false)
                       ?? new SearchEnvelope<GitHubRepository>();

        var result = new RepositorySearchResult
        {
            Items = envelope.Items,
            TotalCount = envelope.TotalCount,
            IncompleteResults = envelope.IncompleteResults,
            Page = Math.Max(query.Page, 1),
            PerPage = Math.Clamp(query.PerPage, 1, 100)
        };

        _cache.Set(uri, result, SearchCacheLifetime);
        _log.Info("GitHub", $"Search returned {result.Items.Count} of {result.TotalCount} for {uri}");
        return result;
    }

    public async Task<GitHubRepository> GetRepositoryAsync(
        string owner, string name, CancellationToken cancellationToken = default)
    {
        var uri = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";

        if (_cache.TryGet<GitHubRepository>(uri, out var cached)) return cached;

        var repository = await GetJsonAsync<GitHubRepository>(uri, cancellationToken).ConfigureAwait(false)
                         ?? throw new GitHubApiException(
                             GitHubErrorKind.Unexpected,
                             "GitHub returned no information for this repository.");

        _cache.Set(uri, repository, RepositoryCacheLifetime);
        return repository;
    }

    public async Task<string?> GetReadmeAsync(
        string owner, string name, CancellationToken cancellationToken = default)
    {
        var uri = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/readme";
        var cacheKey = uri + "#raw";

        if (_cache.TryGet<string>(cacheKey, out var cached)) return cached;

        using var request = CreateRequest(HttpMethod.Get, uri);
        // Ask for the file itself rather than the base64 metadata envelope.
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _log.Info("GitHub", $"No README for {owner}/{name}");
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var markdown = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _cache.Set(cacheKey, markdown, ContentCacheLifetime);
        return markdown;
    }

    public async Task<IReadOnlyDictionary<string, long>> GetLanguagesAsync(
        string owner, string name, CancellationToken cancellationToken = default)
    {
        var uri = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/languages";

        if (_cache.TryGet<Dictionary<string, long>>(uri, out var cached)) return cached;

        var languages = await GetJsonAsync<Dictionary<string, long>>(uri, cancellationToken).ConfigureAwait(false)
                        ?? new Dictionary<string, long>();

        _cache.Set(uri, languages, ContentCacheLifetime);
        return languages;
    }

    public async Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(
        string owner, string name, int limit = 10, CancellationToken cancellationToken = default)
    {
        var capped = Math.Clamp(limit, 1, 100);
        var uri = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}"
                  + "/releases?per_page=" + capped.ToString(CultureInfo.InvariantCulture);

        if (_cache.TryGet<List<GitHubRelease>>(uri, out var cached)) return cached;

        List<GitHubRelease> releases;
        try
        {
            releases = await GetJsonAsync<List<GitHubRelease>>(uri, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (GitHubApiException ex) when (ex.Kind == GitHubErrorKind.NotFound)
        {
            // A repository with releases disabled behaves the same as one with no releases.
            releases = [];
        }

        _cache.Set(uri, releases, ContentCacheLifetime);
        return releases;
    }

    // ---- Plumbing ---------------------------------------------------------

    private async Task<T?> GetJsonAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, uri);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, GitHubJson.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _log.Error("GitHub", "Could not read the response from " + uri, ex);
            throw new GitHubApiException(
                GitHubErrorKind.Unexpected,
                "GitHub sent back something RepoDeck could not read.",
                ex.Message, RateLimit, ex);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        var token = _tokens.GetToken();
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // A user-initiated cancellation is not an error.
        }
        catch (TaskCanceledException ex)
        {
            _log.Warn("GitHub", "Request timed out: " + request.RequestUri, ex);
            throw new GitHubApiException(
                GitHubErrorKind.Network,
                "GitHub took too long to respond. Check your connection and try again.",
                ex.Message, RateLimit, ex);
        }
        catch (HttpRequestException ex)
        {
            _log.Warn("GitHub", "Request failed: " + request.RequestUri, ex);
            throw new GitHubApiException(
                GitHubErrorKind.Network,
                "RepoDeck could not reach GitHub. Check your internet connection.",
                ex.Message, RateLimit, ex);
        }

        UpdateRateLimit(response);
        return response;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        var detail = $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}";

        // A 403/429 with no remaining quota is the rate limit, not a permissions problem.
        var rateLimited =
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
            && ((RateLimit.IsKnown && RateLimit.Remaining <= 0)
                || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase));

        if (rateLimited)
        {
            _log.Warn("GitHub", "Rate limited. " + detail);
            throw new GitHubApiException(GitHubErrorKind.RateLimited, RateLimitMessage(), detail, RateLimit);
        }

        var kind = GitHubErrorKind.Unexpected;
        var message = "Something went wrong talking to GitHub.";

        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                kind = GitHubErrorKind.NotFound;
                message = "That repository could not be found on GitHub. "
                          + "It may have been renamed, deleted or made private.";
                break;
            case HttpStatusCode.Forbidden:
                kind = GitHubErrorKind.Forbidden;
                message = "GitHub refused this request.";
                break;
            case HttpStatusCode.UnprocessableEntity:
                kind = GitHubErrorKind.InvalidQuery;
                message = "GitHub could not understand that search. Try simpler words.";
                break;
            default:
                if ((int)response.StatusCode >= 500)
                {
                    kind = GitHubErrorKind.ServerError;
                    message = "GitHub is having problems right now. Try again in a moment.";
                }
                break;
        }

        _log.Warn("GitHub", "Request failed. " + detail);
        throw new GitHubApiException(kind, message, detail, RateLimit);
    }

    private string RateLimitMessage()
    {
        var suffix = IsAuthenticated
            ? ""
            : " Adding a GitHub token raises the limit considerably - see Settings.";

        var reset = RateLimit.ResetsAt;
        if (reset is null) return "You have used up GitHub's request allowance." + suffix;

        var wait = reset.Value - DateTimeOffset.UtcNow;
        string when;
        if (wait <= TimeSpan.Zero) when = "any moment now";
        else if (wait.TotalMinutes < 1) when = "in less than a minute";
        else when = "in about " + Math.Ceiling(wait.TotalMinutes).ToString("0", CultureInfo.InvariantCulture) + " minutes";

        return $"You have used up GitHub's request allowance. It resets {when}.{suffix}";
    }

    private static async Task<string> SafeReadBodyAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return body.Length > 500 ? body[..500] : body;
        }
        catch
        {
            return "<no body>";
        }
    }

    private void UpdateRateLimit(HttpResponseMessage response)
    {
        if (!TryGetHeaderInt(response, "x-ratelimit-limit", out var limit) ||
            !TryGetHeaderInt(response, "x-ratelimit-remaining", out var remaining))
        {
            return;
        }

        DateTimeOffset? resetsAt = TryGetHeaderLong(response, "x-ratelimit-reset", out var reset)
            ? DateTimeOffset.FromUnixTimeSeconds(reset)
            : null;

        RateLimit = new RateLimitStatus
        {
            Limit = limit,
            Remaining = remaining,
            ResetsAt = resetsAt,
            IsAuthenticated = IsAuthenticated
        };

        RateLimitChanged?.Invoke(RateLimit);
    }

    private static bool TryGetHeaderInt(HttpResponseMessage response, string name, out int value)
    {
        value = 0;
        return response.Headers.TryGetValues(name, out var values)
               && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetHeaderLong(HttpResponseMessage response, string name, out long value)
    {
        value = 0;
        return response.Headers.TryGetValues(name, out var values)
               && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
