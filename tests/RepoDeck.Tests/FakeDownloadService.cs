using System.Security.Cryptography;
using RepoDeck.Models;
using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

/// <summary>
/// Stands in for a real download by copying a file that already exists on disk into the
/// downloads folder, so installation can be tested without touching the network.
/// </summary>
internal sealed class FakeDownloadService : IDownloadService
{
    private readonly string _sourceFile;
    private readonly string _downloadsDirectory;

    public FakeDownloadService(string sourceFile, string downloadsDirectory)
    {
        _sourceFile = sourceFile;
        _downloadsDirectory = downloadsDirectory;
    }

    public Exception? Throws { get; set; }
    public int CallCount { get; private set; }

    public async Task<DownloadedFile> DownloadAsync(
        InstallPlan plan,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        cancellationToken.ThrowIfCancellationRequested();

        if (Throws is not null) throw Throws;

        Directory.CreateDirectory(_downloadsDirectory);

        var destination = Path.Combine(
            _downloadsDirectory, Path.GetFileName(plan.AssetName ?? "download"));

        File.Copy(_sourceFile, destination, overwrite: true);

        var bytes = await File.ReadAllBytesAsync(destination, cancellationToken);

        progress?.Report(new DownloadProgress
        {
            BytesReceived = bytes.LongLength,
            TotalBytes = bytes.LongLength
        });

        return new DownloadedFile
        {
            Path = destination,
            Size = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        };
    }
}
