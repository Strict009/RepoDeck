using System.Runtime.InteropServices;
using RepoDeck.Models;

namespace RepoDeck.Infrastructure;

/// <summary>
/// Detects what machine RepoDeck is running on.
/// </summary>
/// <remarks>
/// Detection lives here; the <em>types</em> live in Models, so the analysis code can
/// reason about a Linux ARM64 machine without depending on the infrastructure layer.
/// </remarks>
public static class PlatformInfo
{
    public static OsPlatform CurrentOs { get; } =
        OperatingSystem.IsWindows() ? OsPlatform.Windows
        : OperatingSystem.IsLinux() ? OsPlatform.Linux
        : OperatingSystem.IsMacOS() ? OsPlatform.MacOS
        : OsPlatform.Unknown;

    public static CpuArchitecture CurrentArchitecture { get; } =
        FromRuntimeArchitecture(RuntimeInformation.OSArchitecture);

    public static CpuArchitecture CurrentProcessArchitecture { get; } =
        FromRuntimeArchitecture(RuntimeInformation.ProcessArchitecture);

    public static string DotnetRuntimeDescription => RuntimeInformation.FrameworkDescription;

    public static CpuArchitecture FromRuntimeArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => CpuArchitecture.X64,
        Architecture.X86 => CpuArchitecture.X86,
        Architecture.Arm64 => CpuArchitecture.Arm64,
        Architecture.Arm => CpuArchitecture.Arm,
        _ => CpuArchitecture.Unknown
    };

    public static string DisplayName(OsPlatform os) => os.DisplayName();

    public static string DisplayName(CpuArchitecture architecture) => architecture.DisplayName();

    /// <summary>e.g. "Windows x64" - shown to the user so compatibility claims have context.</summary>
    public static string CurrentDescription => $"{CurrentOs.DisplayName()} {CurrentArchitecture.DisplayName()}";

    /// <summary>A profile describing this machine, for the compatibility analyzers.</summary>
    public static MachineProfile CurrentMachine() => new()
    {
        OperatingSystem = CurrentOs,
        Architecture = CurrentArchitecture,
        ProcessArchitecture = CurrentProcessArchitecture,
        DotnetRuntime = DotnetRuntimeDescription
    };
}
