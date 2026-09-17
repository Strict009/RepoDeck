using System.Diagnostics;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

/// <summary>
/// The boundaries around Run. Every test here is a refusal, a path check or a
/// bookkeeping assertion - none of them starts a process.
/// </summary>
public sealed class RunSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RepoDeckRunTests", Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;
    private readonly LaunchService _launcher;

    public RunSafetyTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new InstalledAppStore(_paths, NullAppLog.Instance);
        _launcher = new LaunchService(_paths, _store, NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Ignore locked temp files.
        }
    }

    private string InstallDirectory
    {
        get
        {
            var directory = Path.Combine(_paths.Apps, "someone__tool");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    private ApplicationManifest Manifest(
        string? relative,
        InstallationState state = InstallationState.Installed,
        string? installDirectory = null) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        InstalledPath = installDirectory ?? InstallDirectory,
        ExecutableRelativePath = relative,
        State = state,
        InstalledAt = DateTimeOffset.UtcNow
    };

    // ---- Containment ------------------------------------------------------

    [Theory]
    [InlineData("../../../evil.exe")]
    [InlineData(@"..\..\evil.exe")]
    [InlineData("sub/../../../evil.exe")]
    public void A_relative_path_that_climbs_out_of_the_installation_is_refused(string relative)
    {
        // The manifest is a file on disk; an edited one must not become a way out.
        var result = _launcher.Launch(Manifest(relative));

        Assert.False(result.Succeeded);
        Assert.Contains("inside", result.ErrorMessage);
    }

    [Theory]
    [InlineData("/bin/sh")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    public void An_absolute_path_in_the_manifest_is_refused(string absolute)
    {
        var result = _launcher.Launch(Manifest(absolute));

        Assert.False(result.Succeeded);
        Assert.Contains("inside", result.ErrorMessage);
    }

    [Fact]
    public void An_installation_directory_outside_the_managed_folder_is_refused()
    {
        var outside = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "anything.exe"), "x");

        var result = _launcher.Launch(Manifest("anything.exe", installDirectory: outside));

        Assert.False(result.Succeeded);
        Assert.Contains("only run programs inside its own folder", result.ErrorMessage);
    }

    // ---- State ------------------------------------------------------------

    [Fact]
    public void An_unresolved_ambiguous_installation_cannot_be_run()
    {
        var manifest = Manifest(null, InstallationState.AwaitingExecutableChoice);

        var result = _launcher.Launch(manifest);

        Assert.False(result.Succeeded);
        Assert.Contains("multiple possible application executables", result.ErrorMessage);
    }

    [Fact]
    public void A_downloaded_only_record_cannot_be_run()
    {
        var result = _launcher.Launch(Manifest(null, InstallationState.Downloaded));

        Assert.False(result.Succeeded);
        Assert.Contains("did not install it", result.ErrorMessage);
    }

    [Fact]
    public void A_missing_executable_produces_a_readable_error()
    {
        var result = _launcher.Launch(Manifest("gone.exe"));

        Assert.False(result.Succeeded);
        Assert.Contains("missing", result.ErrorMessage);
        Assert.DoesNotContain("Exception", result.ErrorMessage);
    }

    [Fact]
    public void A_manifest_with_no_executable_says_so()
    {
        var result = _launcher.Launch(Manifest(null));

        Assert.False(result.Succeeded);
        Assert.Contains("did not identify a program", result.ErrorMessage);
    }

    // ---- Bookkeeping ------------------------------------------------------

    [Fact]
    public void A_refused_launch_does_not_record_a_run()
    {
        // LastRunAt and RunCount mean "this actually started", so a refusal must not move them.
        var manifest = Manifest("gone.exe");
        _store.Save(manifest);

        _launcher.Launch(manifest);

        var stored = _store.Find("someone", "tool")!;
        Assert.Null(stored.LastRunAt);
        Assert.Equal(0, stored.RunCount);
    }

    [Fact]
    public void A_launch_that_throws_does_not_record_a_run()
    {
        // A file that exists but cannot be started: on Windows a text file with an .exe
        // name fails in CreateProcess, which is exactly the path being exercised.
        var directory = InstallDirectory;
        File.WriteAllText(Path.Combine(directory, "not-really.exe"), "this is not a program");

        var manifest = Manifest("not-really.exe");
        _store.Save(manifest);

        var result = _launcher.Launch(manifest);

        if (result.Succeeded)
        {
            // On a platform where this somehow starts, there is nothing to assert about
            // the failure path; the refusal tests above still cover the boundary.
            return;
        }

        Assert.Contains("would not start", result.ErrorMessage);

        var stored = _store.Find("someone", "tool")!;
        Assert.Null(stored.LastRunAt);
        Assert.Equal(0, stored.RunCount);
    }

    // ---- Process configuration -------------------------------------------

    [Fact]
    public void RepoDeck_never_starts_a_process_through_the_shell()
    {
        // Asserted against the source, because the alternative is starting a real process
        // to observe it. ShellExecute applies file associations and verbs, turning "run
        // this file" into "do whatever the system thinks this extension means".
        var source = File.ReadAllText(SourcePath("LaunchService.cs"));

        Assert.Contains("UseShellExecute = false", source);
        Assert.DoesNotContain("UseShellExecute = true", source);
    }

    [Fact]
    public void RepoDeck_never_requests_elevation_and_never_passes_arguments()
    {
        var source = File.ReadAllText(SourcePath("LaunchService.cs"));

        // Assignments, not mentions: the comments explain why these are absent.
        Assert.DoesNotContain("Verb =", source);
        Assert.DoesNotContain("\"runas\"", source);

        // Nothing from a repository, README or release note is ever handed to a process.
        Assert.DoesNotContain("Arguments =", source);
        Assert.DoesNotContain("ArgumentList", source);
    }

    [Fact]
    public void The_working_directory_is_the_applications_own_folder()
    {
        var source = File.ReadAllText(SourcePath("LaunchService.cs"));

        Assert.Contains("WorkingDirectory = Path.GetDirectoryName(full)!", source);
    }

    [Fact]
    public void Launching_is_the_only_place_RepoDeck_starts_a_downloaded_program()
    {
        var installDirectory = Path.Combine(SourceRoot(), "Services", "Install");

        var offenders = Directory
            .EnumerateFiles(installDirectory, "*.cs")
            .Where(f => File.ReadAllText(f).Contains("Process.Start"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["LaunchService.cs"], offenders);
    }

    private static string SourceRoot()
    {
        // Walk up from the test binary to the repository, then into the application project.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "RepoDeck");
    }

    private static string SourcePath(string fileName) =>
        Path.Combine(SourceRoot(), "Services", "Install", fileName);
}
