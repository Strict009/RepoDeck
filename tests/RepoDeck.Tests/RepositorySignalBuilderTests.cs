using RepoDeck.Models;
using RepoDeck.Services.Analysis;

namespace RepoDeck.Tests;

public class RepositorySignalBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_archived_repository_is_flagged_as_a_caution()
    {
        var details = new RepositoryDetails { Repository = TestRepositories.Create(archived: true) };

        var signals = RepositorySignalBuilder.Build(details, Now);

        Assert.Contains(signals, s => s.IsCaution && s.Text.Contains("archived"));
    }

    [Fact]
    public void A_stale_project_is_a_caution_and_a_fresh_one_is_favourable()
    {
        var stale = RepositorySignalBuilder.Build(new RepositoryDetails
        {
            Repository = TestRepositories.Create(pushedAt: Now.AddYears(-4))
        }, Now);

        var fresh = RepositorySignalBuilder.Build(new RepositoryDetails
        {
            Repository = TestRepositories.Create(pushedAt: Now.AddDays(-5))
        }, Now);

        Assert.Contains(stale, s => s.IsCaution && s.Text.Contains("Last updated"));
        Assert.Contains(fresh, s => s.IsFavourable && s.Text.Contains("Actively maintained"));
    }

    [Fact]
    public void A_missing_licence_is_a_caution()
    {
        var details = new RepositoryDetails { Repository = TestRepositories.Create(licenseSpdx: null) };

        var signals = RepositorySignalBuilder.Build(details, Now);

        Assert.Contains(signals, s => s.IsCaution && s.Text.Contains("No licence detected"));
    }

    [Fact]
    public void Prebuilt_downloads_are_reported_as_favourable()
    {
        var details = new RepositoryDetails
        {
            Repository = TestRepositories.Create(),
            Releases = [TestRepositories.Release("v1.2", false, "tool-win-x64.zip")]
        };

        var signals = RepositorySignalBuilder.Build(details, Now);

        Assert.Contains(signals, s => s.IsFavourable && s.Text.Contains("ready-made downloads"));
    }

    [Fact]
    public void No_releases_at_all_means_building_from_source()
    {
        var details = new RepositoryDetails { Repository = TestRepositories.Create() };

        var signals = RepositorySignalBuilder.Build(details, Now);

        Assert.Contains(signals, s => s.IsCaution && s.Text.Contains("No published releases"));
    }

    [Fact]
    public void A_prerelease_is_called_out()
    {
        var details = new RepositoryDetails
        {
            Repository = TestRepositories.Create(),
            Releases = [TestRepositories.Release("v3.0-beta", true, "tool.zip")]
        };

        var signals = RepositorySignalBuilder.Build(details, Now);

        Assert.Contains(signals, s => s.IsCaution && s.Text.Contains("pre-release"));
    }

    [Fact]
    public void A_very_new_repository_is_flagged_as_having_little_track_record()
    {
        var details = new RepositoryDetails
        {
            Repository = TestRepositories.Create(createdAt: Now.AddDays(-14))
        };

        var signals = RepositorySignalBuilder.Build(details, Now);

        Assert.Contains(signals, s => s.IsCaution && s.Text.Contains("little track record"));
    }

    [Fact]
    public void Popularity_is_never_presented_as_safety()
    {
        var details = new RepositoryDetails { Repository = TestRepositories.Create(stars: 50_000) };

        var signals = RepositorySignalBuilder.Build(details, Now);

        var starSignal = Assert.Single(signals, s => s.Text.Contains("GitHub stars"));
        Assert.Equal(SignalTone.Neutral, starSignal.Tone);
        Assert.Contains("not a guarantee", starSignal.Text);

        // There must be no aggregate verdict anywhere in the panel.
        Assert.DoesNotContain(signals, s => s.Text.Equals("Safe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_fork_is_identified_as_not_the_original_project()
    {
        var details = new RepositoryDetails { Repository = TestRepositories.Create(fork: true) };

        var signals = RepositorySignalBuilder.Build(details, Now);

        Assert.Contains(signals, s => s.Text.Contains("fork"));
    }
}
