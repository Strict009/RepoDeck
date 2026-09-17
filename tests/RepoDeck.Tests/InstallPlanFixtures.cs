using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

internal static class InstallPlanFixtures
{
    public static readonly MachineProfile WindowsX64 =
        MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64);

    public static readonly MachineProfile LinuxX64 =
        MachineProfile.For(OsPlatform.Linux, CpuArchitecture.X64);

    public static AppPaths Paths() => new(Path.Combine(Path.GetTempPath(), "RepoDeckPlanTests"));

    public static RepositoryAnalysis Analysis(
        ApplicationType type = ApplicationType.DesktopApplication, bool complete = true) => new()
    {
        Owner = "someone",
        Name = "example",
        RepositoryUrl = "https://github.com/someone/example",
        ApplicationType = type,
        ApplicationTypeConfidence = Confidence.Likely,
        IsComplete = complete,
        IncompleteReason = complete ? null : "The rate limit was reached."
    };

    public static InstallPlan Plan(MachineProfile machine, params string[] assets)
    {
        var releases = ReleaseAnalyzer.Analyze(
            [TestRepositories.Release("v1.4.2", false, assets)], machine);

        return new InstallPlanner(Paths()).Create(
            TestRepositories.Create(), Analysis(), releases, machine);
    }

    public static InstallPlan PlanFor(
        RepositoryAnalysis analysis, MachineProfile machine, GitHubRelease[] releases)
    {
        var analysed = ReleaseAnalyzer.Analyze(releases, machine);
        return new InstallPlanner(Paths()).Create(
            TestRepositories.Create(), analysis, analysed, machine);
    }
}
