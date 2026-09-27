using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
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

    private string MakeTarGZip(string name, params TarFixtureEntry[] entries)
    {
        var path = Path.Combine(_workspace, name);

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new TarWriter(gzip, leaveOpen: false);

        foreach (var fixture in entries)
        {
            var entry = new PaxTarEntry(fixture.Type, fixture.Path);
            if (fixture.Type is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
            {
                entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(fixture.Content ?? ""));
            }
            else if (fixture.Type is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                entry.LinkName = fixture.LinkName ?? "../../outside";
            }

            writer.WriteEntry(entry);
        }

        return path;
    }

    private sealed record TarFixtureEntry(
        string Path,
        string? Content = null,
        TarEntryType Type = TarEntryType.RegularFile,
        string? LinkName = null);

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
    public async Task An_absolute_zip_entry_is_refused()
    {
        var zip = MakeZip("absolute.zip",
            ("tool.exe", "legitimate"),
            ("/absolute.txt", "should never be written"));

        var destination = Destination("absolute");
        var result = await _service.ExtractAsync(zip, destination, PackageType.Zip);

        Assert.Single(result.RefusedEntries);
        Assert.Equal(1, result.FileCount);
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

    [Fact]
    public async Task A_tar_gzip_extracts_nested_files()
    {
        var archive = MakeTarGZip("tool.tar.gz",
            new TarFixtureEntry("tool.exe", "binary"),
            new TarFixtureEntry("bin/helper.dll", "library"));

        var destination = Destination("targz");
        var result = await _service.ExtractAsync(archive, destination, PackageType.TarGz);

        Assert.Equal(2, result.FileCount);
        Assert.True(File.Exists(Path.Combine(destination, "tool.exe")));
        Assert.True(File.Exists(Path.Combine(destination, "bin", "helper.dll")));
    }

    [Fact]
    public async Task An_empty_tar_gzip_is_refused()
    {
        var archive = MakeTarGZip("empty.tar.gz");

        var error = await Assert.ThrowsAsync<ExtractionException>(
            () => _service.ExtractAsync(archive, Destination("empty-tar"), PackageType.TarGz));

        Assert.Contains("nothing", error.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_truncated_tar_gzip_is_reported_as_invalid()
    {
        var archive = MakeTarGZip("truncated.tar.gz",
            new TarFixtureEntry("tool.exe", new string('x', 4096)));
        var bytes = await File.ReadAllBytesAsync(archive);
        await File.WriteAllBytesAsync(archive, bytes[..(bytes.Length / 2)]);

        var error = await Assert.ThrowsAsync<ExtractionException>(
            () => _service.ExtractAsync(archive, Destination("truncated-tar"), PackageType.TarGz));

        Assert.True(
            error.UserMessage.Contains("valid archive", StringComparison.OrdinalIgnoreCase)
            || error.UserMessage.Contains("could not unpack", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Tar_parent_traversal_and_links_are_refused_without_hiding_valid_files()
    {
        var archive = MakeTarGZip("hostile.tar.gz",
            new TarFixtureEntry("tool.exe", "valid"),
            new TarFixtureEntry("../../escaped.txt", "nope"),
            new TarFixtureEntry("shortcut", Type: TarEntryType.SymbolicLink, LinkName: "../../escaped"));

        var destination = Destination("hostile-tar");
        var result = await _service.ExtractAsync(archive, destination, PackageType.TarGz);

        Assert.Equal(1, result.FileCount);
        Assert.Equal(2, result.RefusedEntries.Count);
        Assert.True(File.Exists(Path.Combine(destination, "tool.exe")));
        Assert.False(File.Exists(Path.Combine(_workspace, "escaped.txt")));
    }

    [Fact]
    public async Task A_mismatched_archive_format_is_not_decoded_by_its_filename()
    {
        var zipBytesNamedTar = MakeZip("pretends.tar.gz", ("tool.exe", "x"));

        var error = await Assert.ThrowsAsync<ExtractionException>(
            () => _service.ExtractAsync(
                zipBytesNamedTar, Destination("mismatch"), PackageType.TarGz));

        Assert.Contains("valid archive", error.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PackageType.SevenZip)]
    [InlineData(PackageType.TarXz)]
    [InlineData(PackageType.TarBz2)]
    public async Task Unsupported_archives_are_refused_before_a_destination_is_created(PackageType type)
    {
        var archive = MakeZip("bytes.dat", ("tool.exe", "x"));
        var destination = Path.Combine(_workspace, "never-created", type.ToString());

        var error = await Assert.ThrowsAsync<ExtractionException>(
            () => _service.ExtractAsync(archive, destination, type));

        Assert.Contains("cannot unpack", error.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void Expanded_size_limit_refuses_the_first_byte_beyond_the_boundary()
    {
        ExtractionService.GuardTotalSize(ExtractionService.MaxTotalBytes);

        var error = Assert.Throws<ExtractionException>(
            () => ExtractionService.GuardTotalSize(ExtractionService.MaxTotalBytes + 1));

        Assert.Contains("more than", error.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Entry_limit_refuses_the_first_entry_beyond_the_boundary()
    {
        ExtractionService.GuardEntryCount(ExtractionService.MaxEntries);

        var error = Assert.Throws<ExtractionException>(
            () => ExtractionService.GuardEntryCount(ExtractionService.MaxEntries + 1));

        Assert.Contains("number of files", error.UserMessage, StringComparison.OrdinalIgnoreCase);
    }
}
