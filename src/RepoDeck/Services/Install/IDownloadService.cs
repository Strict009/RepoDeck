using RepoDeck.Models;

namespace RepoDeck.Services.Install;

/// <summary>The outcome of a download that completed successfully.</summary>
public sealed record DownloadedFile
{
    public required string Path { get; init; }
    public required long Size { get; init; }

    /// <summary>SHA-256 of the bytes as received, recorded in the manifest.</summary>
    public required string Sha256 { get; init; }
}

/// <summary>
/// Fetches a release asset to disk.
/// </summary>
/// <remarks>
/// An interface because Milestone 4's update flow and any future resume support will
/// replace the strategy without the installer caring. Implementations must never leave
/// a partial file where a complete one is expected.
/// </remarks>
public interface IDownloadService
{
    /// <summary>
    /// Downloads the plan's asset into RepoDeck's Downloads folder.
    /// Throws <see cref="DownloadException"/> for anything worth showing the user.
    /// </summary>
    Task<DownloadedFile> DownloadAsync(
        InstallPlan plan,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class DownloadException : Exception
{
    public DownloadException(string userMessage, string? detail = null, Exception? inner = null)
        : base(detail ?? userMessage, inner)
    {
        UserMessage = userMessage;
    }

    /// <summary>Plain English, safe to put straight on screen.</summary>
    public string UserMessage { get; }
}
