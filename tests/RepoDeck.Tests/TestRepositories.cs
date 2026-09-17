using RepoDeck.Models;

namespace RepoDeck.Tests;

/// <summary>Builds repository fixtures without every test repeating the boilerplate.</summary>
internal static class TestRepositories
{
    public static GitHubRepository Create(
        string name = "example",
        string owner = "someone",
        string? description = "A thing that does something useful for people.",
        string? language = "C#",
        int stars = 1_000,
        bool archived = false,
        bool fork = false,
        string? licenseSpdx = "MIT",
        IReadOnlyList<string>? topics = null,
        DateTimeOffset? pushedAt = null,
        DateTimeOffset? createdAt = null)
    {
        return new GitHubRepository
        {
            Id = 1,
            Name = name,
            FullName = $"{owner}/{name}",
            Description = description,
            HtmlUrl = $"https://github.com/{owner}/{name}",
            Language = language,
            StargazersCount = stars,
            Archived = archived,
            Fork = fork,
            Owner = new RepositoryOwner { Login = owner },
            License = licenseSpdx is null
                ? null
                : new RepositoryLicense { SpdxId = licenseSpdx, Name = licenseSpdx + " License" },
            Topics = topics ?? [],
            PushedAt = pushedAt ?? DateTimeOffset.UtcNow.AddDays(-10),
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddYears(-3)
        };
    }

    public static GitHubRelease Release(
        string tag = "v1.0.0",
        bool prerelease = false,
        params string[] assetNames)
    {
        return new GitHubRelease
        {
            TagName = tag,
            Name = tag,
            Prerelease = prerelease,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-30),
            Assets = assetNames.Select(n => new GitHubReleaseAsset
            {
                Name = n,
                Size = 5 * 1024 * 1024,
                BrowserDownloadUrl = "https://example.invalid/" + n
            }).ToList()
        };
    }
}
