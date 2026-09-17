using RepoDeck.Models;

namespace RepoDeck.Services.Install;

public sealed record ExtractionResult
{
    public required string Directory { get; init; }
    public required int FileCount { get; init; }
    public required long TotalBytes { get; init; }

    /// <summary>
    /// Entries refused during extraction, with the reason. Non-empty means the archive
    /// tried to do something RepoDeck would not allow.
    /// </summary>
    public IReadOnlyList<string> RefusedEntries { get; init; } = [];
}

/// <summary>
/// Unpacks a downloaded archive into RepoDeck's managed folder.
/// </summary>
/// <remarks>
/// Archives come from strangers. Every entry path is resolved and checked to be inside
/// the destination before a single byte is written, so an archive cannot reach the rest
/// of the machine.
/// </remarks>
public interface IExtractionService
{
    Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string destinationDirectory,
        PackageType packageType,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ExtractionException : Exception
{
    public ExtractionException(string userMessage, string? detail = null, Exception? inner = null)
        : base(detail ?? userMessage, inner)
    {
        UserMessage = userMessage;
    }

    public string UserMessage { get; }
}
