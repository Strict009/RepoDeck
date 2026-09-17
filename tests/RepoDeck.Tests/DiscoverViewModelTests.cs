using RepoDeck.Infrastructure;
using RepoDeck.Services.Explanation;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

public class DiscoverViewModelTests
{
    private static DiscoverViewModel Create(FakeGitHubClient github) =>
        new(github, new HeuristicRepositoryExplanationService(), NullAppLog.Instance);

    [Fact]
    public async Task A_slow_earlier_search_cannot_overwrite_a_newer_one()
    {
        var github = new FakeGitHubClient();
        github.ResultsByText["old"] = [TestRepositories.Create("old-result")];
        github.ResultsByText["new"] = [TestRepositories.Create("new-result")];

        var vm = Create(github);

        // Start a search that will not complete yet.
        var gate = new TaskCompletionSource();
        github.SearchGate = gate;
        vm.SearchText = "old";
        var firstSearch = vm.SearchCommand.ExecuteAsync(null);

        // While it is in flight, the user searches for something else.
        github.SearchGate = null;
        vm.SearchText = "new";
        await vm.SearchCommand.ExecuteAsync(null);

        // Now let the stale search finish. It must not disturb the newer result.
        gate.TrySetResult();
        await firstSearch;

        Assert.Equal(["new-result"], vm.Results.Select(r => r.Name));
        Assert.False(vm.IsBusy);
        Assert.Null(vm.ErrorMessage);
        Assert.DoesNotContain("cancelled", vm.ResultSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelling_a_search_leaves_no_error_on_screen()
    {
        var github = new FakeGitHubClient { SearchGate = new TaskCompletionSource() };
        var vm = Create(github);
        vm.SearchText = "anything";

        var search = vm.SearchCommand.ExecuteAsync(null);
        vm.SearchCancelCommand.Execute(null);
        await search;

        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_failed_search_reports_the_user_facing_message()
    {
        var github = new FakeGitHubClient
        {
            SearchThrows = new RepoDeck.Services.GitHub.GitHubApiException(
                RepoDeck.Services.GitHub.GitHubErrorKind.RateLimited,
                "You have used up GitHub's request allowance.")
        };

        var vm = Create(github);
        vm.SearchText = "anything";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("You have used up GitHub's request allowance.", vm.ErrorMessage);
        Assert.True(vm.HasError);
        Assert.False(vm.ShowResults);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Exactly_one_display_state_is_active_at_a_time()
    {
        var github = new FakeGitHubClient { DefaultResults = [TestRepositories.Create()] };
        var vm = Create(github);

        Assert.True(vm.ShowWelcome);
        Assert.False(vm.ShowResults);
        Assert.False(vm.ShowEmptyState);

        vm.SearchText = "thing";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.False(vm.ShowWelcome);
        Assert.True(vm.ShowResults);
        Assert.False(vm.ShowEmptyState);
    }

    [Fact]
    public async Task No_matches_shows_the_empty_state_rather_than_an_error()
    {
        var github = new FakeGitHubClient { DefaultResults = [] };
        var vm = Create(github);

        vm.SearchText = "nothing matches this";
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.HasError);
    }
}
