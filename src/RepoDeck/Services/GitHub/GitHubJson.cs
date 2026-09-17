using System.Text.Json;
using System.Text.Json.Serialization;

namespace RepoDeck.Services.GitHub;

/// <summary>GitHub's search endpoints wrap results in an envelope.</summary>
public sealed class SearchEnvelope<T>
{
    public int TotalCount { get; init; }
    public bool IncompleteResults { get; init; }
    public IReadOnlyList<T> Items { get; init; } = [];
}

public static class GitHubJson
{
    /// <summary>
    /// Shared serializer options. The snake_case naming policy is what lets the models
    /// stay free of per-property attributes.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}
