using System.Runtime.InteropServices;

namespace RepoDeck.Infrastructure;

public enum OsPlatform { Unknown, Windows, Linux, MacOS }

public enum CpuArchitecture { Unknown, X64, X86, Arm64, Arm }

/// <summary>
/// What machine RepoDeck is running on. Milestone 2 ranks release assets against this.
/// Written as pure functions over explicit inputs so it can be tested for platforms
/// other than the one the test suite happens to run on.
/// </summary>
public static class PlatformInfo
{
    public static OsPlatform CurrentOs { get; } =
        OperatingSystem.IsWindows() ? OsPlatform.Windows
        : OperatingSystem.IsLinux() ? OsPlatform.Linux
        : OperatingSystem.IsMacOS() ? OsPlatform.MacOS
        : OsPlatform.Unknown;

    public static CpuArchitecture CurrentArchitecture { get; } =
        FromRuntimeArchitecture(RuntimeInformation.OSArchitecture);

    public static CpuArchitecture FromRuntimeArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => CpuArchitecture.X64,
        Architecture.X86 => CpuArchitecture.X86,
        Architecture.Arm64 => CpuArchitecture.Arm64,
        Architecture.Arm => CpuArchitecture.Arm,
        _ => CpuArchitecture.Unknown
    };

    public static string DisplayName(OsPlatform os) => os switch
    {
        OsPlatform.Windows => "Windows",
        OsPlatform.Linux => "Linux",
        OsPlatform.MacOS => "macOS",
        _ => "Unknown system"
    };

    public static string DisplayName(CpuArchitecture architecture) => architecture switch
    {
        CpuArchitecture.X64 => "x64",
        CpuArchitecture.X86 => "x86",
        CpuArchitecture.Arm64 => "ARM64",
        CpuArchitecture.Arm => "ARM",
        _ => "unknown architecture"
    };

    /// <summary>e.g. "Windows x64" - shown to the user so compatibility claims have context.</summary>
    public static string CurrentDescription =>
        $"{DisplayName(CurrentOs)} {DisplayName(CurrentArchitecture)}";
}
