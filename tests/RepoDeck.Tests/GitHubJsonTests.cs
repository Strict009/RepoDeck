using System.Text.Json;
using RepoDeck.Models;
using RepoDeck.Services.GitHub;

namespace RepoDeck.Tests;

/// <summary>
/// Deserialisation against representative GitHub payloads. These are fixed samples
/// rather than live requests, so the suite never depends on the network or a rate limit.
/// </summary>
public class GitHubJsonTests
{
    private const string SearchResponse = """
    {
      "total_count": 2543,
      "incomplete_results": false,
      "items": [
        {
          "id": 1296269,
          "name": "shotcut",
          "full_name": "mltframework/shotcut",
          "private": false,
          "owner": { "login": "mltframework", "avatar_url": "https://example.invalid/a.png", "type": "Organization" },
          "html_url": "https://github.com/mltframework/shotcut",
          "description": "cross-platform (Qt), open-source (GPLv3) video editor",
          "fork": false,
          "created_at": "2011-01-26T19:01:12Z",
          "updated_at": "2026-06-10T08:30:00Z",
          "pushed_at": "2026-06-14T21:15:03Z",
          "homepage": "https://www.shotcut.org",
          "size": 184532,
          "stargazers_count": 11842,
          "forks_count": 921,
          "open_issues_count": 143,
          "language": "C++",
          "archived": false,
          "disabled": false,
          "default_branch": "master",
          "license": { "key": "gpl-3.0", "name": "GNU General Public License v3.0", "spdx_id": "GPL-3.0" },
          "topics": ["video-editor", "qt", "cross-platform"]
        }
      ]
    }
    """;

    [Fact]
    public void Search_envelope_maps_snake_case_fields_onto_the_model()
    {
        var envelope = JsonSerializer.Deserialize<SearchEnvelope<GitHubRepository>>(
            SearchResponse, GitHubJson.Options);

        Assert.NotNull(envelope);
        Assert.Equal(2543, envelope!.TotalCount);
        Assert.False(envelope.IncompleteResults);

        var repository = Assert.Single(envelope.Items);
        Assert.Equal("shotcut", repository.Name);
        Assert.Equal("mltframework/shotcut", repository.FullName);
        Assert.Equal("mltframework", repository.OwnerLogin);
        Assert.Equal(11842, repository.Stars);
        Assert.Equal(921, repository.Forks);
        Assert.Equal("C++", repository.Language);
        Assert.Equal("GPL-3.0", repository.LicenseSpdxId);
        Assert.True(repository.HasLicense);
        Assert.False(repository.IsArchived);
        Assert.Equal(["video-editor", "qt", "cross-platform"], repository.Topics);
    }

    [Fact]
    public void Last_activity_prefers_the_push_date_over_the_metadata_date()
    {
        var envelope = JsonSerializer.Deserialize<SearchEnvelope<GitHubRepository>>(
            SearchResponse, GitHubJson.Options);

        var repository = envelope!.Items[0];

        // pushed_at is later than updated_at in the sample, and is the better activity signal.
        Assert.Equal(repository.PushedAt, repository.LastActivity);
    }

    [Fact]
    public void A_placeholder_licence_is_treated_as_no_licence()
    {
        const string json = """
        { "name": "x", "full_name": "o/x", "license": { "spdx_id": "NOASSERTION", "name": "Other" } }
        """;

        var repository = JsonSerializer.Deserialize<GitHubRepository>(json, GitHubJson.Options);

        Assert.NotNull(repository);
        Assert.Null(repository!.LicenseSpdxId);
        Assert.False(repository.HasLicense);
    }

    [Fact]
    public void Missing_optional_fields_do_not_break_deserialisation()
    {
        const string json = """{ "id": 5, "name": "bare", "full_name": "someone/bare" }""";

        var repository = JsonSerializer.Deserialize<GitHubRepository>(json, GitHubJson.Options);

        Assert.NotNull(repository);
        Assert.Equal("bare", repository!.Name);
        Assert.Null(repository.Description);
        Assert.Null(repository.Language);
        Assert.Empty(repository.Topics);
        Assert.Equal(0, repository.Stars);
        // Owner is absent, so the login is recovered from the full name.
        Assert.Equal("someone", repository.OwnerLogin);
    }

    [Fact]
    public void Releases_and_their_assets_deserialise()
    {
        const string json = """
        [
          {
            "id": 99,
            "tag_name": "v24.3.1",
            "name": "Version 24.3.1",
            "draft": false,
            "prerelease": false,
            "published_at": "2026-03-04T10:00:00Z",
            "html_url": "https://example.invalid/releases/v24.3.1",
            "assets": [
              { "id": 1, "name": "app-win-x64.zip", "size": 78123456, "browser_download_url": "https://example.invalid/app-win-x64.zip", "download_count": 4210 },
              { "id": 2, "name": "app-linux-x64.tar.gz", "size": 71234567, "browser_download_url": "https://example.invalid/app-linux-x64.tar.gz", "download_count": 903 }
            ]
          }
        ]
        """;

        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, GitHubJson.Options);

        var release = Assert.Single(releases!);
        Assert.Equal("v24.3.1", release.TagName);
        Assert.Equal("Version 24.3.1", release.DisplayName);
        Assert.True(release.IsStable);
        Assert.Equal(2, release.Assets.Count);
        Assert.Equal("app-win-x64.zip", release.Assets[0].Name);
        Assert.Equal(78123456, release.Assets[0].Size);
    }

    [Fact]
    public void A_release_without_a_name_displays_its_tag()
    {
        const string json = """[{ "tag_name": "v2", "name": null, "assets": [] }]""";

        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, GitHubJson.Options);

        Assert.Equal("v2", releases![0].DisplayName);
    }

    [Fact]
    public void Language_byte_counts_deserialise_as_a_dictionary()
    {
        const string json = """{ "C++": 1840233, "QML": 231004, "C": 9932 }""";

        var languages = JsonSerializer.Deserialize<Dictionary<string, long>>(json, GitHubJson.Options);

        Assert.Equal(3, languages!.Count);
        Assert.Equal(1840233, languages["C++"]);
    }
}
