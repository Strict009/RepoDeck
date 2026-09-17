using System.Runtime.InteropServices;
using RepoDeck.Infrastructure;
using RepoDeck.Models;
using RepoDeck.Services.GitHub;

namespace RepoDeck.Tests;

public class ResponseCacheTests
{
    [Fact]
    public void A_stored_value_is_returned_within_its_lifetime()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = new ResponseCache(time);

        cache.Set("key", "value", TimeSpan.FromMinutes(5));

        Assert.True(cache.TryGet<string>("key", out var value));
        Assert.Equal("value", value);
    }

    [Fact]
    public void An_expired_value_is_not_returned()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = new ResponseCache(time);

        cache.Set("key", "value", TimeSpan.FromMinutes(5));
        time.Advance(TimeSpan.FromMinutes(6));

        Assert.False(cache.TryGet<string>("key", out _));
    }

    [Fact]
    public void A_missing_key_reports_a_miss()
    {
        var cache = new ResponseCache();
        Assert.False(cache.TryGet<string>("nothing", out _));
    }

    [Fact]
    public void A_value_of_the_wrong_type_is_treated_as_a_miss()
    {
        var cache = new ResponseCache();
        cache.Set("key", "a string", TimeSpan.FromMinutes(1));

        Assert.False(cache.TryGet<List<int>>("key", out _));
    }

    [Fact]
    public void The_cache_does_not_grow_without_limit()
    {
        var cache = new ResponseCache(maxEntries: 8);

        for (var i = 0; i < 50; i++)
        {
            cache.Set($"key{i}", $"value{i}", TimeSpan.FromMinutes(5));
        }

        Assert.True(cache.Count <= 8, $"Cache held {cache.Count} entries.");
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}

public class PlatformInfoTests
{
    [Theory]
    [InlineData(Architecture.X64, CpuArchitecture.X64)]
    [InlineData(Architecture.X86, CpuArchitecture.X86)]
    [InlineData(Architecture.Arm64, CpuArchitecture.Arm64)]
    [InlineData(Architecture.Arm, CpuArchitecture.Arm)]
    public void Runtime_architectures_map_onto_RepoDeck_values(Architecture input, CpuArchitecture expected)
    {
        Assert.Equal(expected, PlatformInfo.FromRuntimeArchitecture(input));
    }

    [Fact]
    public void Unrecognised_architectures_become_unknown_rather_than_a_guess()
    {
        Assert.Equal(CpuArchitecture.Unknown, PlatformInfo.FromRuntimeArchitecture((Architecture)999));
    }

    [Fact]
    public void The_current_machine_is_described_in_readable_words()
    {
        var description = PlatformInfo.CurrentDescription;

        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.DoesNotContain("Unknown system", description);
    }

    [Theory]
    [InlineData(OsPlatform.Windows, "Windows")]
    [InlineData(OsPlatform.Linux, "Linux")]
    [InlineData(OsPlatform.MacOS, "macOS")]
    public void Platform_names_are_human_readable(OsPlatform platform, string expected)
    {
        Assert.Equal(expected, PlatformInfo.DisplayName(platform));
    }
}

public class AppPathsTests
{
    [Fact]
    public void All_working_folders_live_under_one_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "RepoDeckTests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);

        Assert.Equal(root, paths.Root);
        foreach (var folder in new[] { paths.Apps, paths.Cache, paths.Downloads, paths.Logs, paths.Data })
        {
            Assert.StartsWith(root, folder);
        }
    }

    [Fact]
    public void Creating_the_folders_is_repeatable()
    {
        var root = Path.Combine(Path.GetTempPath(), "RepoDeckTests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);

        try
        {
            paths.EnsureCreated();
            paths.EnsureCreated();

            Assert.True(Directory.Exists(paths.Apps));
            Assert.True(Directory.Exists(paths.Downloads));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

public class GitHubTokenProviderTests
{
    [Fact]
    public void A_blank_token_counts_as_no_token()
    {
        var provider = new GitHubTokenProvider(() => "   ");

        Assert.Null(provider.GetToken());
        Assert.False(provider.HasToken);
    }

    [Fact]
    public void A_token_is_trimmed_before_use()
    {
        var provider = new GitHubTokenProvider(() => "  ghp_example  ");

        Assert.Equal("ghp_example", provider.GetToken());
        Assert.True(provider.HasToken);
    }
}
