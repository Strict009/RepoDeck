using System.Diagnostics;
using RepoDeck.Infrastructure;
using RepoDeck.Services.Explanation;
using RepoDeck.Services.Readme;
using RepoDeck.ViewModels;

namespace RepoDeck.Tests;

/// <summary>
/// Regression tests for problems found during the Milestone 1 self-audit.
/// </summary>
public class HardeningTests
{
    [Fact]
    public void A_hostile_readme_cannot_hang_the_parser()
    {
        // Deeply nested emphasis and unclosed tags are the classic way to make a naive
        // regex pass take exponential time. README content is untrusted, so this must
        // complete promptly or degrade, never hang.
        var hostile = new string('*', 6_000) + " text " + new string('_', 6_000)
                      + "\n<" + new string('a', 5_000) + "\n"
                      + string.Concat(Enumerable.Repeat("[x](", 2_000));

        var stopwatch = Stopwatch.StartNew();
        var blocks = ReadmeParser.Parse(hostile);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Parsing took {stopwatch.Elapsed.TotalSeconds:0.0}s.");
        Assert.NotNull(blocks);
    }

    [Fact]
    public void An_enormous_readme_is_capped_rather_than_parsed_whole()
    {
        var huge = string.Concat(Enumerable.Repeat("This is a normal sentence of prose. ", 60_000));

        var stopwatch = Stopwatch.StartNew();
        var blocks = ReadmeParser.Parse(huge);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Parsing took {stopwatch.Elapsed.TotalSeconds:0.0}s.");
        Assert.NotEmpty(blocks);
    }

    [Fact]
    public void Readme_links_are_reduced_to_text_so_nothing_is_clickable()
    {
        // RepoDeck must never surface a URL from repository content as something the
        // user can activate, and must never carry a non-web scheme through.
        var blocks = ReadmeParser.Parse("Run [this installer](file:///C:/Windows/System32/cmd.exe) now.");

        var paragraph = Assert.Single(blocks);
        Assert.DoesNotContain("file://", paragraph.Text);
        Assert.DoesNotContain("cmd.exe", paragraph.Text);
        Assert.Equal("Run this installer now.", paragraph.Text);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:windowsupdate")]
    [InlineData("not a url at all")]
    [InlineData("")]
    public void Only_web_addresses_are_ever_opened(string candidate)
    {
        var log = new RecordingLog();

        SystemBrowser.OpenUrl(candidate, log);

        // Nothing was launched; a refusal is either logged or the input was ignored.
        Assert.DoesNotContain(log.Entries, e => e.Contains("Opened "));
    }

    [Fact]
    public async Task Abandoning_a_details_page_stops_its_work()
    {
        var github = new FakeGitHubClient();
        var details = new RepositoryDetailsViewModel(
            TestRepositories.Create(), github, new HeuristicRepositoryExplanationService(),
            NullAppLog.Instance);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await details.LoadAsync(cts.Token);

        // A cancelled load reports no error to the user and leaves the page idle.
        Assert.Null(details.ErrorMessage);
        Assert.False(details.IsLoading);
    }

    private sealed class RecordingLog : IAppLog
    {
        public List<string> Entries { get; } = [];

        public void Write(LogLevel level, string category, string message, Exception? exception = null) =>
            Entries.Add($"{category}: {message}");
    }
}
