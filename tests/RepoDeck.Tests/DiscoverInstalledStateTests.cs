using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.Install;
using RepoDeck.Services.Media;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

public sealed class DiscoverInstalledStateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-discover-installed-" + Guid.NewGuid().ToString("N"));
    private readonly InstalledAppStore _store;

    public DiscoverInstalledStateTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new InstalledAppStore(paths, NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A test scratch directory is not product state.
        }
    }

    [Theory]
    [InlineData("SomeOwner", "SomeApp", "someowner", "someapp", true)]
    [InlineData("owner-a", "same-app", "owner-b", "same-app", false)]
    [InlineData("same-owner", "app-a", "same-owner", "app-b", false)]
    public async Task Installed_identity_is_exact_owner_and_name_case_insensitively(
        string installedOwner,
        string installedName,
        string resultOwner,
        string resultName,
        bool expectedInstalled)
    {
        _store.Save(Manifest(installedOwner, installedName));
        var github = new FakeGitHubClient
        {
            DefaultResults = [TestRepositories.Create(resultName, resultOwner)]
        };
        var discover = Create(github);

        discover.SearchText = resultName;
        await discover.SearchCommand.ExecuteAsync(null);

        Assert.Equal(expectedInstalled, Assert.Single(discover.Results).IsInstalled);
        Assert.Equal(1, github.SearchCallCount);
    }

    [Fact]
    public async Task Installed_ready_card_never_offers_install_and_remove_restores_it()
    {
        var github = new FakeGitHubClient
        {
            DefaultResults = [TestRepositories.Create("player", "music-org")]
        };
        var discover = Create(github);
        discover.SearchText = "player";
        await discover.SearchCommand.ExecuteAsync(null);

        var card = Assert.Single(discover.Results);
        card.ApplyAnalysedInstallability(new Installability
        {
            State = InstallabilityState.ReadyToInstall
        });
        Assert.True(card.CanInstallDirectly);
        Assert.Equal("INSTALL", card.PrimaryActionLabel);

        _store.Save(Manifest("MUSIC-ORG", "PLAYER"));

        Assert.True(card.IsInstalled);
        Assert.False(card.CanInstallDirectly);
        Assert.Equal("DETAILS", card.PrimaryActionLabel);

        var installRequests = 0;
        discover.RepositoryInstallRequested += _ => installRequests++;
        card.ActivateCommand.Execute(null);
        Assert.Equal(0, installRequests);
        Assert.Same(card, discover.SelectedResult);

        Assert.True(_store.Remove("music-org", "player"));

        Assert.False(card.IsInstalled);
        Assert.True(card.CanInstallDirectly);
        Assert.Equal("INSTALL", card.PrimaryActionLabel);
        Assert.Equal(1, github.SearchCallCount);
        Assert.Equal(0, github.RepositoryCallCount);
        Assert.Equal(0, github.ReadmeCallCount);
        Assert.Equal(0, github.ReleaseCallCount);
        Assert.Equal(0, github.TreeCallCount);
    }

    private DiscoverViewModel Create(FakeGitHubClient github) =>
        new(
            github,
            new HeuristicRepositoryExplanationService(),
            new RepositoryMediaService(NullAppLog.Instance),
            NullAppLog.Instance,
            installedApps: _store,
            dispatcher: ImmediateUiDispatcher.Instance);

    private ApplicationManifest Manifest(string owner, string name) => new()
    {
        Owner = owner,
        Name = name,
        RepositoryUrl = $"https://github.com/{owner}/{name}",
        InstalledPath = Path.Combine(_root, "Apps", owner, name),
        State = InstallationState.Installed,
        ExecutableRelativePath = name + ".exe"
    };
}
