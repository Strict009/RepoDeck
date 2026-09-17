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
        MachineProfile? machine = null)
    {
        var profile = machine ?? MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "RepoDeckDetailsTests"));

        return new RepositoryDetailsViewModel(
            repository ?? TestRepositories.Create(),
            github,
            new HeuristicRepositoryExplanationService(),
            new RepositoryAnalyzerService(github, NullAppLog.Instance),
            new InstallPlanner(paths),
            profile,
            NullAppLog.Instance);
    }
}
