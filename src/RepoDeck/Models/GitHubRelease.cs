using System.Text.Json.Serialization;

namespace RepoDeck.Models;

/// <summary>
/// A GitHub release. Read-only in Milestone 1 (displayed as factual information);
/// Milestone 2 adds compatibility ranking over <see cref="Assets"/>.
/// </summary>
public sealed class GitHubRelease
{
    public long Id { get; init; }
    public string TagName { get; init; } = "";
    public string? Name { get; init; }
    public string? Body { get; init; }
    public string HtmlUrl { get; init; } = "";
    public bool Draft { get; init; }
    public bool Prerelease { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public IReadOnlyList<GitHubReleaseAsset> Assets { get; init; } = [];

    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? TagName : Name!;
    [JsonIgnore] public bool IsStable => !Draft && !Prerelease;
}

public sealed class GitHubReleaseAsset
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string? Label { get; init; }
    public string BrowserDownloadUrl { get; init; } = "";
    public string? ContentType { get; init; }
    public long Size { get; init; }
    public int DownloadCount { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
