using System.Reflection;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Which build of RepoDeck this is.
/// </summary>
/// <remarks>
/// Read from the assembly rather than written down here, so it cannot drift from what the
/// packaging script stamped into the artifacts. There is exactly one place the version is
/// set - Directory.Build.props - and everything else, including this, derives from it.
///
/// It is shown on the Settings page because an alpha tester reporting a problem needs to be
/// able to say which build they had, and asking somebody to right-click an executable and
/// read a properties dialog is not a reasonable thing to ask.
/// </remarks>
public static class AppVersion
{
    /// <summary>The full version including any pre-release suffix: "0.1.0-alpha".</summary>
    public static string Current { get; } = Resolve();

    /// <summary>
    /// True while the version carries a pre-release suffix. RepoDeck says so on screen
    /// rather than letting somebody assume a development build is finished software.
    /// </summary>
    public static bool IsPrerelease => Current.Contains('-');

    /// <summary>What the Settings page shows. Plain, and honest about what this is.</summary>
    public static string Description => IsPrerelease
        ? $"RepoDeck {Current} - a development build, not a finished release"
        : $"RepoDeck {Current}";

    private static string Resolve()
    {
        var assembly = typeof(AppVersion).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (informational is { Length: > 0 })
        {
            // Some build configurations append "+<commit>". That is for a build system,
            // not for somebody reading a settings page.
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
