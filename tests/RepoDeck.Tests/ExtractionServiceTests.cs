using System.IO.Compression;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

/// <summary>
/// Extraction against archives built here, including a deliberately malicious one.
/// </summary>
public sealed class ExtractionServiceTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(), "RepoDeckExtractTests", Guid.NewGuid().ToString("N"));

    private readonly ExtractionService _service = new(NullAppLog.Instance);

    public ExtractionServiceTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
        }
        catch
        {
            // A locked temp file must not fail the test run.
        }
    }

    private string MakeZip(string name, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(_workspace, name);

        using var stream = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (entryPath, content) in entries)
        {
            var entry = archive.CreateEntry(entryPath);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    private string Destination(string name)
    {
        var path = Path.Combine(_workspace, "install", name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task An_ordinary_archive_extracts_its_files()
    {
        var zip = MakeZip("tool.zip",
            ("tool.exe", "binary"),
            ("bin/helper.dll", "library"),
            ("docs/readme.txt", "words"));

        var destination = Destination("ordinary");
        var result = await _service.ExtractAsync(zip, destination, PackageType.Zip);

        Assert.Equal(3, result.FileCount);
        Assert.Empty(result.RefusedEntries);
        Assert.True(File.Exists(Path.Combine(destination, "tool.exe")));
        Assert.True(File.Exists(Path.Combine(destination, "bin", "helper.dll")));
    }

    [Fact]
    public async Task A_zip_slip_entry_is_refused_and_nothing_escapes()
    {
        // The attack: an entry whose path climbs out of the destination folder.
        var zip = MakeZip("evil.zip",
            ("tool.exe", "legitimate"),
            ("../../escaped.txt", "should never be written"));

        var destination = Destination("slip");
        var result = await _service.ExtractAsync(zip, destination, PackageType.Zip);

        Assert.Single(result.RefusedEntries);
        Assert.Equal(1, result.FileCount);

        // The file must not exist anywhere above the destination.
        var escapedInWorkspace = Path.Combine(_workspace, "escaped.txt");
        var escapedInInstall = Path.Combine(_workspace, "install", "escaped.txt");

        Assert.False(File.Exists(escapedInWorkspace));
        Assert.False(File.Exists(escapedInInstall));
        Assert.True(File.Exists(Path.Combine(destination, "tool.exe")));
    }

    [Fact]
    public async Task An_archive_of_nothing_but_escapes_fails_rather_than_installing_nothing_quietly()
    {
        var zip = MakeZip("all-evil.zip", ("../../escaped.txt", "nope"));
        var destination = Destination("allevil");

        var error = await Assert.ThrowsAsync<ExtractionException>(
            () => _service.ExtractAsync(zip, destination, PackageType.Zip));

        Assert.Contains("safely unpack", error.UserMessage);
    }

    [Fact]
    public async Task A_corrupt_archive_reports_a_readable_error()
    {
        var path = Path.Combine(_workspace, "corrupt.zip");
        await File.WriteAllTextAsync(path, "this is definitely not a zip file");

        var error = await Assert.ThrowsAsync<ExtractionException>(
            () => _service.ExtractAsync(path, Destination("corrupt"), PackageType.Zip));

        Assert.Contains("not a valid archive", error.UserMessage);
    }

    [Fact]
    public async Task A_missing_download_reports_a_readable_error()
    {
        var error = await Assert.ThrowsAsync<ExtractionException>(
            () => _service.ExtractAsync(
                Path.Combine(_workspace, "absent.zip"), Destination("absent"), PackageType.Zip));

        Assert.Contains("missing", error.UserMessage);
    }

    [Fact]
    public async Task An_unsupported_package_type_is_refused_rather_than_attempted()
    {
        var zip = MakeZip("tool.zip", ("tool.exe", "x"));

        var error = await Assert.ThrowsAsync<ExtractionException>(
            () => _service.ExtractAsync(zip, Destination("unsupported"), PackageType.WindowsInstaller));

        Assert.Contains("cannot unpack", error.UserMessage);
    }

    [Fact]
    public async Task Extraction_can_be_cancelled()
    {
        var entries = Enumerable.Range(0, 200)
            .Select(i => ($"file{i}.dat", new string('x', 4096)))
            .ToArray();

        var zip = MakeZip("many.zip", entries);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ExtractAsync(zip, Destination("cancel"), PackageType.Zip, null, cts.Token));
    }

    [Fact]
    public async Task Nested_directories_are_created_as_needed()
    {
        var zip = MakeZip("nested.zip", ("a/b/c/d/deep.txt", "content"));
        var destination = Destination("nested");

        await _service.ExtractAsync(zip, destination, PackageType.Zip);

        Assert.True(File.Exists(Path.Combine(destination, "a", "b", "c", "d", "deep.txt")));
    }
}
