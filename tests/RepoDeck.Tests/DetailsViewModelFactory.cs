using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.Install;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

internal static class DetailsViewModelFactory
{
    public static RepositoryDetailsViewModel Create(
        FakeGitHubClient github,
        GitHubRepository? repository = null,
        MachineProfile? machine = null,
        IInstallationService? installer = null,
        IInstalledAppStore? installedApps = null,
        AppPaths? paths = null)
    {
        var profile = machine ?? MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);
        var appPaths = paths ?? new AppPaths(
            Path.Combine(Path.GetTempPath(), "RepoDeckDetailsTests", Guid.NewGuid().ToString("N")));

        var store = installedApps ?? new InstalledAppStore(appPaths, NullAppLog.Instance);

        return new RepositoryDetailsViewModel(
            repository ?? TestRepositories.Create(),
            github,
            new HeuristicRepositoryExplanationService(),
            new RepositoryAnalyzerService(github, NullAppLog.Instance),
            new InstallPlanner(appPaths),
            installer ?? new NoOpInstallationService(),
            store,
            new LaunchService(appPaths, store, NullAppLog.Instance),
            profile,
            NullAppLog.Instance);
    }
}

/// <summary>
/// Refuses to install anything. Used wherever a test opens a details page but is not
/// exercising installation, so no test can install by accident.
/// </summary>
internal sealed class NoOpInstallationService : IInstallationService
{
    public Task<InstallationResult> InstallAsync(
        InstallPlan plan,
        IProgress<InstallationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InstallationResult.Failed("Installation is not available in this test."));

    public Task<bool> UninstallAsync(
        ApplicationManifest manifest, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public ExecutableChoiceResult ChooseExecutable(ApplicationManifest manifest, string relativePath) =>
        ExecutableChoiceResult.Failed("Not available in this test.");
}
