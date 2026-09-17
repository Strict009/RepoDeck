namespace RepoDeck.Models;

public enum OsPlatform { Unknown, Windows, Linux, MacOS }

public enum CpuArchitecture { Unknown, X64, X86, Arm64, Arm }

public static class PlatformText
{
    public static string DisplayName(this OsPlatform os) => os switch
    {
        OsPlatform.Windows => "Windows",
        OsPlatform.Linux => "Linux",
        OsPlatform.MacOS => "macOS",
        _ => "Unknown system"
    };

    public static string DisplayName(this CpuArchitecture architecture) => architecture switch
    {
        CpuArchitecture.X64 => "x64",
        CpuArchitecture.X86 => "x86",
        CpuArchitecture.Arm64 => "ARM64",
        CpuArchitecture.Arm => "ARM",
        _ => "unknown architecture"
    };
}
