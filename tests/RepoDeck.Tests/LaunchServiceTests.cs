using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

/// <summary>
/// The launcher's refusals. These tests deliberately never start a process: what matters
/// is that RepoDeck declines to run anything it should not, and every case here is a
/// refusal or a file-system check.
/// </summary>
public sealed class LaunchServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RepoDeckLaunchTests", Guid.NewGuid().ToString("N"));

    private readonly AppPaths _paths;
    private readonly LaunchService _service;

    public LaunchServiceTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _service = new LaunchService(_paths, new InstalledAppStore(_paths, NullAppLog.Instance),
            NullAppLog.Instance);
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

    /// <summary>
    /// Builds a manifest whose executable resolves to <paramref name="executablePath"/>,
    /// splitting it so the stored path stays relative to the install directory.
    /// </summary>
    private ApplicationManifest Manifest(string? executablePath, bool downloadOnly = false) => new()
    {
        Owner = "someone",
        Name = "tool",
        RepositoryUrl = "https://github.com/someone/tool",
        InstalledPath = executablePath is null
            ? Path.Combine(_paths.Apps, "someone__tool")
            : Path.GetDirectoryName(Path.GetFullPath(executablePath))!,
        ExecutableRelativePath = executablePath is null ? null : Path.GetFileName(executablePath),
        State = downloadOnly ? InstallationState.Downloaded : InstallationState.Installed,
        InstalledAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void An_executable_outside_RepoDecks_folder_is_refused()
    {
        // The single most important refusal: a tampered manifest must not turn Run into
        // "start any program on this machine".
        var outside = Path.Combine(_root, "elsewhere", "anything.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "x");

        var result = _service.Launch(Manifest(outside));

        Assert.False(result.Succeeded);
        Assert.Contains("only run programs inside its own folder", result.ErrorMessage);
    }

    [Fact]
    public void A_system_path_is_refused()
    {
        var result = _service.Launch(Manifest(
            OperatingSystem.IsWindows() ? @"C:\Windows\System32\cmd.exe" : "/bin/sh"));

        Assert.False(result.Succeeded);
        Assert.Contains("only run programs inside its own folder", result.ErrorMessage);
    }

    [Fact]
    public void A_missing_executable_reports_that_it_is_missing()
    {
        var registered = Path.Combine(_paths.Apps, "someone__tool", "tool.exe");

        var result = _service.Launch(Manifest(registered));

        Assert.False(result.Succeeded);
        Assert.Contains("missing", result.ErrorMessage);
    }

    [Fact]
    public void A_manifest_with_no_executable_says_so()
    {
        var result = _service.Launch(Manifest(null));

        Assert.False(result.Succeeded);
        Assert.Contains("did not identify a program", result.ErrorMessage);
    }

    [Fact]
    public void A_download_only_installation_is_never_launched()
    {
        // RepoDeck downloaded an installer and stopped; running it is the user's decision.
        var result = _service.Launch(Manifest(null, downloadOnly: true));

        Assert.False(result.Succeeded);
        Assert.Contains("did not install it", result.ErrorMessage);
    }

    [Fact]
    public void An_installation_is_intact_when_its_executable_is_present()
    {
        var directory = Path.Combine(_paths.Apps, "someone__tool");
        Directory.CreateDirectory(directory);

        var executable = Path.Combine(directory, "tool.exe");
        File.WriteAllText(executable, "program");

        Assert.True(_service.IsIntact(Manifest(executable)));
    }

    [Fact]
    public void An_installation_is_not_intact_when_its_executable_has_gone()
    {
        var executable = Path.Combine(_paths.Apps, "someone__tool", "tool.exe");

        Assert.False(_service.IsIntact(Manifest(executable)));
    }
}
