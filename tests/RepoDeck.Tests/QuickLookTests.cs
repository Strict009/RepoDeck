using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.Analysis;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.GitHub;
using RepoDeck.Services.Install;
using RepoDeck.Services.Media;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// The side panel. Its two hard requirements: it must be cancellable, and a slower
/// earlier look must never write its answers over a newer selection.
/// </summary>
public class QuickLookTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-quicklook-" + Guid.NewGuid().ToString("N"));

    private readonly FakeGitHubClient _github = new();
    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;

    public QuickLookTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new InstalledAppStore(_paths, NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the test is not worth failing over.
        }
    }

    private QuickLookViewModel Create() =>
        new(_github,
            new HeuristicRepositoryExplanationService(),
            new RepositoryAnalyzerService(_github, NullAppLog.Instance),
            new InstallPlanner(_paths),
            new RepositoryMediaService(NullAppLog.Instance),
            _store,
            new LaunchService(_paths, _store, NullAppLog.Instance),
            MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64),
            NullAppLog.Instance);

    private static RepositoryCardViewModel Card(GitHubRepository repository)
    {
        var explanations = new HeuristicRepositoryExplanationService();
        var explanation = explanations.ExplainFromMetadata(repository);
        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);
        var setup = SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood);

        return new RepositoryCardViewModel(
            repository, explanation, likelihood, setup, imageUrl: null,
            openDetails: _ => { }, log: NullAppLog.Instance);
    }

    // ---- Opening ----------------------------------------------------------

    [Fact]
    public async Task The_panel_is_filled_from_the_card_before_anything_is_fetched()
    {
        // Selecting a result has to feel instant even when the analysis takes seconds.
        var card = Card(TestRepositories.Create("timber", "naman14", language: "Java"));
        var panel = Create();

        var loading = panel.ShowAsync(card);

        Assert.True(panel.IsOpen);
        Assert.Equal("Timber", panel.Title);
        Assert.Contains("naman14", panel.Subtitle);
        Assert.False(string.IsNullOrWhiteSpace(panel.WhatItIs));

        await loading;
    }

    [Fact]
    public async Task Closing_the_panel_forgets_the_card()
    {
        var panel = Create();
        await panel.ShowAsync(Card(TestRepositories.Create("tool")));

        panel.CloseCommand.Execute(null);

        Assert.False(panel.IsOpen);
        Assert.False(panel.IsLoading);
        Assert.Null(panel.Card);
    }

    // ---- Staleness --------------------------------------------------------

    [Fact]
    public async Task A_superseded_look_never_writes_into_a_panel_that_has_moved_on()
    {
        // A cancellation token only helps where the work stops to look at it. Analysis
        // that has already been handed off finishes regardless, and its continuation then
        // runs against a panel describing something else entirely. That is what the
        // generation counter is for, and this is the case that proves it is needed.
        var handedOff = new TaskCompletionSource();
        _github.TreeGate = handedOff;

        var panel = Create();
        var abandoned = panel.ShowAsync(
            Card(TestRepositories.Create("first-project", description: "The older one.")));

        _github.TreeGate = null;
        await panel.ShowAsync(
            Card(TestRepositories.Create("second-project", description: "The newer one.")));

        Assert.Equal("Second Project", panel.Title);
        Assert.False(panel.IsLoading);

        // The abandoned analysis now completes and tries to report its findings.
        handedOff.SetResult();
        await abandoned;

        Assert.Equal("Second Project", panel.Title);
        Assert.Contains("newer", panel.WhatItIs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("older", panel.WhatItIs, StringComparison.OrdinalIgnoreCase);
        Assert.False(panel.IsLoading);
    }

    [Fact]
    public async Task A_slow_earlier_look_cannot_overwrite_a_newer_selection()
    {
        var gate = new TaskCompletionSource();
        _github.ReadmeGate = gate;

        var first = Card(TestRepositories.Create("first-project", description: "The older one."));
        var second = Card(TestRepositories.Create("second-project", description: "The newer one."));

        var panel = Create();
        var slow = panel.ShowAsync(first);

        _github.ReadmeGate = null;
        await panel.ShowAsync(second);

        Assert.Equal("Second Project", panel.Title);
        Assert.Contains("newer", panel.WhatItIs, StringComparison.OrdinalIgnoreCase);

        gate.SetResult();
        await slow;

        // WhatItIs is written by the load rather than seeded from the card, so it is the
        // property a stale continuation would actually corrupt.
        Assert.Equal("Second Project", panel.Title);
        Assert.Contains("newer", panel.WhatItIs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("older", panel.WhatItIs, StringComparison.OrdinalIgnoreCase);
        Assert.Same(second, panel.Card);
    }

    [Fact]
    public async Task Closing_during_a_look_does_not_reopen_the_panel_when_it_finishes()
    {
        var gate = new TaskCompletionSource();
        _github.ReadmeGate = gate;

        var panel = Create();
        var slow = panel.ShowAsync(Card(TestRepositories.Create("tool")));

        panel.CloseCommand.Execute(null);

        gate.SetResult();
        await slow;

        Assert.False(panel.IsOpen);
    }

    // ---- What it says -----------------------------------------------------

    [Fact]
    public async Task A_project_with_no_releases_is_not_offered_for_installation()
    {
        _github.Releases = [];

        var card = Card(TestRepositories.Create("tool"));
        var panel = Create();
        await panel.ShowAsync(card);

        Assert.Equal(QuickLookAction.Details, panel.Action);
        Assert.False(panel.ShowInstallAction);
        Assert.False(panel.IsReadyToInstall);
    }

    [Fact]
    public async Task The_verdict_always_comes_with_evidence_and_the_disclaimer()
    {
        var card = Card(TestRepositories.Create("tool"));
        var panel = Create();
        await panel.ShowAsync(card);

        Assert.False(string.IsNullOrWhiteSpace(panel.InstallabilityLabel));
        Assert.False(string.IsNullOrWhiteSpace(panel.InstallabilitySummary));
        Assert.Contains("not whether the software is safe", panel.SafetyDisclaimer);
    }

    [Fact]
    public async Task The_panel_hands_its_verdict_back_to_the_card_in_the_grid()
    {
        // Otherwise the grid goes on saying "Unknown" about something the user has just
        // had explained to them.
        var card = Card(TestRepositories.Create("tool"));
        Assert.Equal(InstallabilityState.Unknown, card.Installability.State);

        var panel = Create();
        await panel.ShowAsync(card);

        Assert.NotEmpty(card.Installability.Reasons);
    }

    [Fact]
    public async Task GitHub_vocabulary_is_confined_to_the_technical_section()
    {
        var card = Card(TestRepositories.Create("tool", "someone"));
        var panel = Create();
        await panel.ShowAsync(card);

        var plainEnglish = string.Join(" ",
            panel.Title, panel.WhatItIs, panel.SetupLabel, panel.SetupSummary,
            panel.CompatibilityText, panel.InstallabilityLabel, panel.InstallabilitySummary);

        Assert.DoesNotContain("repository", plainEnglish, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("release asset", plainEnglish, StringComparison.OrdinalIgnoreCase);

        // And it is genuinely available, not merely hidden.
        Assert.Contains(panel.TechnicalDetails, f => f.Label == "Repository");
    }

    [Fact]
    public async Task A_failed_look_reports_it_rather_than_showing_a_blank_panel()
    {
        _github.ReadmeThrows = new GitHubApiException(
            GitHubErrorKind.ServerError, "GitHub is not responding.");
        _github.ReleasesThrows = _github.ReadmeThrows;
        _github.TreeThrows = _github.ReadmeThrows;

        var panel = Create();
        await panel.ShowAsync(Card(TestRepositories.Create("tool")));

        // Optional requests are tolerated, so the panel must still be usable.
        Assert.False(panel.IsLoading);
        Assert.True(panel.IsOpen);
    }

    [Fact]
    public async Task Details_is_requested_rather_than_navigated_to_directly()
    {
        GitHubRepository? requested = null;

        var panel = Create();
        panel.DetailsRequested += r => requested = r;

        var card = Card(TestRepositories.Create("tool", "someone"));
        await panel.ShowAsync(card);

        panel.OpenDetailsCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal("someone/tool", requested!.FullName);
    }

    [Fact]
    public async Task Installing_is_a_request_to_the_shell_and_never_an_installation()
    {
        // Quick Look owns no install state machine. It asks the details page, which shows
        // the plan and requires confirmation, and that remains the only way in.
        var asked = 0;

        var panel = Create();
        panel.InstallRequested += _ => asked++;

        await panel.ShowAsync(Card(TestRepositories.Create("tool")));
        panel.InstallCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.Empty(_store.GetAll());
    }
}

/// <summary>
/// The cancellation half of the contract. Selecting along a row of results has to abandon
/// work rather than accumulate it, and abandoned work must leave nothing behind.
/// </summary>
public class QuickLookCancellationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "repodeck-ql-cancel-" + Guid.NewGuid().ToString("N"));

    private readonly FakeGitHubClient _github = new();
    private readonly AppPaths _paths;
    private readonly InstalledAppStore _store;

    public QuickLookCancellationTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new InstalledAppStore(_paths, NullAppLog.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the test is not worth failing over.
        }
    }

    private QuickLookViewModel Create() =>
        new(_github,
            new HeuristicRepositoryExplanationService(),
            new RepositoryAnalyzerService(_github, NullAppLog.Instance),
            new InstallPlanner(_paths),
            new RepositoryMediaService(NullAppLog.Instance),
            _store,
            new LaunchService(_paths, _store, NullAppLog.Instance),
            MachineProfile.For(OsPlatform.Windows, CpuArchitecture.X64),
            NullAppLog.Instance);

    private static RepositoryCardViewModel Card(string name = "tool")
    {
        var repository = TestRepositories.Create(name);
        var explanations = new HeuristicRepositoryExplanationService();
        var likelihood = ApplicationLikelihoodEvaluator.Evaluate(repository);

        return new RepositoryCardViewModel(
            repository,
            explanations.ExplainFromMetadata(repository),
            likelihood,
            SetupDifficultyEvaluator.EvaluateFromMetadata(repository, likelihood),
            imageUrl: null,
            openDetails: _ => { },
            log: NullAppLog.Instance);
    }

    [Fact]
    public async Task Selecting_a_new_result_abandons_the_request_the_old_one_was_waiting_on()
    {
        var gate = new TaskCompletionSource();
        _github.ReadmeGate = gate;

        var panel = Create();
        var abandoned = panel.ShowAsync(Card("first"));

        // The second selection cancels the first, which unwinds without being released.
        _github.ReadmeGate = null;
        await panel.ShowAsync(Card("second"));

        await abandoned.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Second", panel.Title);
        gate.TrySetResult();
    }

    [Fact]
    public async Task Closing_the_panel_abandons_the_look_in_flight()
    {
        var gate = new TaskCompletionSource();
        _github.ReadmeGate = gate;

        var panel = Create();
        var inFlight = panel.ShowAsync(Card());

        panel.CloseCommand.Execute(null);

        // Nothing releases the gate: the only way this completes is cancellation.
        await inFlight.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(panel.IsOpen);
        Assert.False(panel.IsLoading);
        gate.TrySetResult();
    }

    [Fact]
    public async Task A_cancelled_look_is_not_reported_to_the_user_as_a_failure()
    {
        // Moving on is not an error, and a panel that flashed "that did not work" every
        // time the user changed their mind would be worse than useless.
        var gate = new TaskCompletionSource();
        _github.ReadmeGate = gate;

        var panel = Create();
        var abandoned = panel.ShowAsync(Card("first"));

        _github.ReadmeGate = null;
        await panel.ShowAsync(Card("second"));
        await abandoned.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(panel.HasError);
        Assert.Null(panel.ErrorMessage);
        gate.TrySetResult();
    }

    [Fact]
    public async Task Selecting_several_results_quickly_leaves_the_last_one_showing()
    {
        // Arrowing down a list of results is the ordinary case, not an edge case.
        var panel = Create();

        for (var i = 0; i < 8; i++)
        {
            _ = panel.ShowAsync(Card("project-" + i));
        }

        await panel.ShowAsync(Card("project-last"));

        Assert.Equal("Project Last", panel.Title);
        Assert.False(panel.IsLoading);
        Assert.False(panel.HasError);
    }

    [Fact]
    public async Task An_abandoned_look_does_not_leave_its_pictures_in_the_panel()
    {
        // The screenshot strip belongs to whatever is selected now, not to whatever was
        // selected when a slow analysis started.
        var handedOff = new TaskCompletionSource();
        _github.TreeGate = handedOff;

        var panel = Create();
        var abandoned = panel.ShowAsync(Card("first"));

        _github.TreeGate = null;
        await panel.ShowAsync(Card("second"));

        var countAfterSecond = panel.Gallery.Count;

        handedOff.SetResult();
        await abandoned.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(countAfterSecond, panel.Gallery.Count);
    }

    [Fact]
    public async Task An_abandoned_look_does_not_write_its_verdict_into_the_wrong_card()
    {
        var handedOff = new TaskCompletionSource();
        _github.TreeGate = handedOff;

        var first = Card("first");
        var second = Card("second");

        var panel = Create();
        var abandoned = panel.ShowAsync(first);

        _github.TreeGate = null;
        await panel.ShowAsync(second);

        // The card the user moved away from keeps the answer it had, not a late one.
        var firstVerdict = first.Installability;

        handedOff.SetResult();
        await abandoned.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(firstVerdict.State, first.Installability.State);
        Assert.Same(second, panel.Card);
    }

    [Fact]
    public async Task Reopening_the_same_card_starts_a_fresh_look_rather_than_reusing_a_stale_one()
    {
        var panel = Create();
        var card = Card();

        await panel.ShowAsync(card);
        var firstCallCount = _github.ReadmeCallCount;

        await panel.ShowAsync(card);

        Assert.True(_github.ReadmeCallCount > firstCallCount);
        Assert.True(panel.IsOpen);
    }
}
