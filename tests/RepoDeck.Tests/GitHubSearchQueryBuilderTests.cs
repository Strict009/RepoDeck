using RepoDeck.Models;
using RepoDeck.Services.GitHub;

namespace RepoDeck.Tests;

public class GitHubSearchQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Free_text_is_passed_through_untouched()
    {
        var query = new RepositorySearchQuery { Text = "  video editor  " };
        Assert.Equal("video editor", GitHubSearchQueryBuilder.BuildQualifiedQuery(query, Now));
    }

    [Fact]
    public void Language_becomes_a_qualifier()
    {
        var query = new RepositorySearchQuery { Text = "editor", Language = "C#" };
        Assert.Equal("editor language:C#", GitHubSearchQueryBuilder.BuildQualifiedQuery(query, Now));
    }

    [Fact]
    public void Languages_containing_spaces_are_quoted()
    {
        var query = new RepositorySearchQuery { Text = "notebook", Language = "Jupyter Notebook" };
        Assert.Equal("notebook language:\"Jupyter Notebook\"",
            GitHubSearchQueryBuilder.BuildQualifiedQuery(query, Now));
    }

    [Fact]
    public void Minimum_stars_becomes_a_range_qualifier()
    {
        var query = new RepositorySearchQuery { Text = "editor", MinStars = 500 };
        Assert.Equal("editor stars:>=500", GitHubSearchQueryBuilder.BuildQualifiedQuery(query, Now));
    }

    [Fact]
    public void Zero_minimum_stars_is_not_a_filter()
    {
        var query = new RepositorySearchQuery { Text = "editor", MinStars = 0 };
        Assert.Equal("editor", GitHubSearchQueryBuilder.BuildQualifiedQuery(query, Now));
    }

    [Fact]
    public void Updated_window_becomes_a_pushed_date_qualifier()
    {
        var query = new RepositorySearchQuery { Text = "editor", UpdatedWithin = UpdatedWithin.PastMonth };
        Assert.Equal("editor pushed:>=2026-05-16", GitHubSearchQueryBuilder.BuildQualifiedQuery(query, Now));
    }

    [Fact]
    public void Archived_exclusion_is_expressed_as_a_qualifier()
    {
        var query = new RepositorySearchQuery { Text = "editor", ExcludeArchived = true };
        Assert.Equal("editor archived:false", GitHubSearchQueryBuilder.BuildQualifiedQuery(query, Now));
    }

    [Fact]
    public void An_entirely_empty_query_falls_back_to_something_GitHub_accepts()
    {
        // GitHub rejects an empty q parameter outright.
        var query = new RepositorySearchQuery();
        Assert.Equal("stars:>=100", GitHubSearchQueryBuilder.BuildQualifiedQuery(query, Now));
    }

    [Theory]
    [InlineData(RepositorySort.BestMatch, "")]
    [InlineData(RepositorySort.Stars, "stars")]
    [InlineData(RepositorySort.RecentlyUpdated, "updated")]
    [InlineData(RepositorySort.Forks, "forks")]
    public void Sort_maps_to_the_GitHub_parameter(RepositorySort sort, string expected)
    {
        Assert.Equal(expected, GitHubSearchQueryBuilder.SortParameter(sort));
    }

    [Fact]
    public void Request_uri_encodes_the_query_and_carries_paging()
    {
        var query = new RepositorySearchQuery
        {
            Text = "video editor",
            Sort = RepositorySort.Stars,
            Page = 2,
            PerPage = 30
        };

        var uri = GitHubSearchQueryBuilder.BuildRequestUri(query, Now);

        Assert.StartsWith("search/repositories?q=", uri);
        Assert.Contains("video%20editor", uri);
        Assert.Contains("&per_page=30", uri);
        Assert.Contains("&page=2", uri);
        Assert.Contains("&sort=stars&order=desc", uri);
    }

    [Fact]
    public void Best_match_omits_the_sort_parameter_entirely()
    {
        var query = new RepositorySearchQuery { Text = "editor" };
        var uri = GitHubSearchQueryBuilder.BuildRequestUri(query, Now);

        Assert.DoesNotContain("sort=", uri);
    }

    [Fact]
    public void Page_size_is_clamped_to_the_GitHub_maximum()
    {
        var query = new RepositorySearchQuery { Text = "editor", PerPage = 500, Page = 0 };
        var uri = GitHubSearchQueryBuilder.BuildRequestUri(query, Now);

        Assert.Contains("&per_page=100", uri);
        Assert.Contains("&page=1", uri);
    }
}
