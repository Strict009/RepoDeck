namespace RepoDeck.Models;

/// <summary>
/// The computer RepoDeck is deciding compatibility for.
/// </summary>
/// <remarks>
/// Passed into the analyzers rather than each of them asking the operating system
/// directly. That keeps the compatibility rules pure and lets the tests evaluate a
/// Linux ARM64 machine from a Windows x64 development box - which is the only way to
/// be sure RepoDeck is not quietly hard-coded to whatever it was written on.
/// </remarks>
public sealed record MachineProfile
{
    public required OsPlatform OperatingSystem { get; init; }

    /// <summary>The architecture of the operating system itself.</summary>
    public required CpuArchitecture Architecture { get; init; }

    /// <summary>
    /// The architecture of the running process, which can differ from the OS -
    /// an x64 process under emulation on an ARM64 machine, for example.
    /// </summary>
    public required CpuArchitecture ProcessArchitecture { get; init; }

    /// <summary>Best-effort .NET runtime description, for future source-build support.</summary>
    public string? DotnetRuntime { get; init; }

    public string Description => OperatingSystem.DisplayName() + " " + Architecture.DisplayName();

    /// <summary>
    /// Architectures this machine can actually run, best first. An ARM64 Windows or
    /// macOS machine runs x64 binaries through emulation; an x64 machine cannot run ARM64.
    /// </summary>
    public IReadOnlyList<CpuArchitecture> RunnableArchitectures => Architecture switch
    {
        CpuArchitecture.X64 => [CpuArchitecture.X64, CpuArchitecture.X86],
        CpuArchitecture.Arm64 when OperatingSystem is OsPlatform.Windows or OsPlatform.MacOS =>
            [CpuArchitecture.Arm64, CpuArchitecture.X64, CpuArchitecture.X86],
        CpuArchitecture.Arm64 => [CpuArchitecture.Arm64],
        CpuArchitecture.X86 => [CpuArchitecture.X86],
        CpuArchitecture.Arm => [CpuArchitecture.Arm],
        _ => []
    };

    /// <summary>True when this machine can run the given architecture, natively or emulated.</summary>
    public bool CanRun(CpuArchitecture architecture) => RunnableArchitectures.Contains(architecture);

    /// <summary>True when running it would mean emulation rather than a native match.</summary>
    public bool IsEmulated(CpuArchitecture architecture) =>
        CanRun(architecture) && architecture != Architecture;

    /// <summary>Convenience for tests and for reasoning about machines other than this one.</summary>
    public static MachineProfile For(OsPlatform os, CpuArchitecture architecture) => new()
    {
        OperatingSystem = os,
        Architecture = architecture,
        ProcessArchitecture = architecture
    };
}
