using System.Text.Json.Serialization;

namespace RepoDeck.Models;

/// <summary>
/// A GitHub repository as RepoDeck understands it.
/// Property names deliberately mirror GitHub's REST shape (snake_case is applied by the
/// serializer naming policy) so there is exactly one model layer for the MVP. Friendly
/// aliases below keep that naming out of the ViewModels.
/// </summary>
public sealed class GitHubRepository
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string FullName { get; init; } = "";
    public string? Description { get; init; }
    public string HtmlUrl { get; init; } = "";
    public string? Homepage { get; init; }
    public string? Language { get; init; }

    public int StargazersCount { get; init; }
    public int ForksCount { get; init; }
    public int OpenIssuesCount { get; init; }
    public int Size { get; init; }

    public bool Archived { get; init; }
    public bool Fork { get; init; }
    public bool Disabled { get; init; }

    public string? DefaultBranch { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? PushedAt { get; init; }

    public RepositoryOwner? Owner { get; init; }
    public RepositoryLicense? License { get; init; }

    public IReadOnlyList<string> Topics { get; init; } = [];

    // ---- Friendly aliases -------------------------------------------------

    [JsonIgnore] public string OwnerLogin => Owner?.Login ?? SplitOwnerFromFullName();
    [JsonIgnore] public string? OwnerAvatarUrl => Owner?.AvatarUrl;
    [JsonIgnore] public int Stars => StargazersCount;
    [JsonIgnore] public int Forks => ForksCount;
    [JsonIgnore] public bool IsArchived => Archived;
    [JsonIgnore] public bool IsFork => Fork;

    /// <summary>Most recent signal of real activity: a push beats a metadata touch.</summary>
    [JsonIgnore] public DateTimeOffset? LastActivity => PushedAt ?? UpdatedAt;

    [JsonIgnore] public string? LicenseName => License?.Name;
    [JsonIgnore] public string? LicenseSpdxId =>
        string.Equals(License?.SpdxId, "NOASSERTION", StringComparison.OrdinalIgnoreCase)
            ? null
            : License?.SpdxId;

    [JsonIgnore] public bool HasLicense => License is not null && LicenseSpdxId is not null;

    private string SplitOwnerFromFullName()
    {
        var slash = FullName.IndexOf('/');
        return slash > 0 ? FullName[..slash] : "";
    }
}

public sealed class RepositoryOwner
{
    public string Login { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public string? HtmlUrl { get; init; }
    public string? Type { get; init; }
}

public sealed class RepositoryLicense
{
    public string? Key { get; init; }
    public string? Name { get; init; }
    public string? SpdxId { get; init; }
    public string? Url { get; init; }
}
