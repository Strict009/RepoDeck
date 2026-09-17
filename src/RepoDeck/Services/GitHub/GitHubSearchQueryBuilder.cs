using System.Globalization;
using System.Text;
using RepoDeck.Models;

namespace RepoDeck.Services.GitHub;

/// <summary>
/// Turns a <see cref="RepositorySearchQuery"/> into GitHub search API parameters.
/// Pure and side-effect free so it can be unit tested without touching the network.
/// </summary>
public static class GitHubSearchQueryBuilder
{
    /// <summary>Builds the value of the <c>q</c> parameter (unencoded).</summary>
    public static string BuildQualifiedQuery(RepositorySearchQuery query, DateTimeOffset? now = null)
    {
        var parts = new List<string>();

        var text = query.Text?.Trim();
        if (!string.IsNullOrEmpty(text)) parts.Add(text);

        if (!string.IsNullOrWhiteSpace(query.Language))
        {
            var language = query.Language.Trim();
            // Languages containing spaces must be quoted, e.g. language:"Jupyter Notebook".
            parts.Add(language.Contains(' ')
                ? $"language:\"{language}\""
                : $"language:{language}");
        }

        if (query.MinStars is > 0)
        {
            parts.Add($"stars:>={query.MinStars.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        var since = ResolvePushedSince(query.UpdatedWithin, now ?? DateTimeOffset.UtcNow);
        if (since is not null)
        {
            parts.Add($"pushed:>={since.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        if (query.ExcludeArchived) parts.Add("archived:false");

        // GitHub rejects an empty q. Fall back to something broad but valid.
        return parts.Count == 0 ? "stars:>=100" : string.Join(' ', parts);
    }

    public static DateTimeOffset? ResolvePushedSince(UpdatedWithin within, DateTimeOffset now) => within switch
    {
        UpdatedWithin.PastMonth => now.AddDays(-30),
        UpdatedWithin.PastSixMonths => now.AddDays(-182),
        UpdatedWithin.PastYear => now.AddDays(-365),
        UpdatedWithin.PastTwoYears => now.AddDays(-730),
        _ => null
    };

    /// <summary>GitHub's <c>sort</c> parameter. Empty string means "best match".</summary>
    public static string SortParameter(RepositorySort sort) => sort switch
    {
        RepositorySort.Stars => "stars",
        RepositorySort.RecentlyUpdated => "updated",
        RepositorySort.Forks => "forks",
        _ => ""
    };

    /// <summary>The full relative request URI, ready for HttpClient.</summary>
    public static string BuildRequestUri(RepositorySearchQuery query, DateTimeOffset? now = null)
    {
        var q = BuildQualifiedQuery(query, now);
        var perPage = Math.Clamp(query.PerPage, 1, 100);
        var page = Math.Max(query.Page, 1);

        var uri = new StringBuilder("search/repositories?q=")
            .Append(Uri.EscapeDataString(q))
            .Append("&per_page=").Append(perPage.ToString(CultureInfo.InvariantCulture))
            .Append("&page=").Append(page.ToString(CultureInfo.InvariantCulture));

        var sort = SortParameter(query.Sort);
        if (!string.IsNullOrEmpty(sort))
        {
            uri.Append("&sort=").Append(sort).Append("&order=desc");
        }

        return uri.ToString();
    }
}
