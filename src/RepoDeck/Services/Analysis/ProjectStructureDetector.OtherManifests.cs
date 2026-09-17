using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

public static partial class ProjectStructureDetector
{
    private static void InspectPackageJson(
        string content, List<string> frameworks, List<Evidence> evidence, List<ApplicationTypeHint> hints)
    {
        if (Mentions(content, "\"electron\""))
        {
            frameworks.Add("Electron");
            var reason = new Evidence(
                "Depends on Electron, which packages a web application as a desktop program.",
                EvidenceSource.FileContents);
            evidence.Add(reason);
            hints.Add(new ApplicationTypeHint(ApplicationType.DesktopApplication, 70, reason));
        }

        if (Mentions(content, "\"tauri\""))
        {
            frameworks.Add("Tauri");
            var reason = new Evidence(
                "Depends on Tauri, a toolkit for building desktop applications.",
                EvidenceSource.FileContents);
            evidence.Add(reason);
            hints.Add(new ApplicationTypeHint(ApplicationType.DesktopApplication, 70, reason));
        }

        if (Mentions(content, "\"bin\":"))
        {
            hints.Add(new ApplicationTypeHint(ApplicationType.CliTool, 50,
                new Evidence("The package declares command-line executables in its bin section.",
                    EvidenceSource.FileContents)));
        }

        if (Mentions(content, "\"types\":") || Mentions(content, "\"typings\":"))
        {
            hints.Add(new ApplicationTypeHint(ApplicationType.Library, 40,
                new Evidence("The package ships type definitions for other developers to build against.",
                    EvidenceSource.FileContents)));
        }
    }

    private static void InspectCargoToml(string content, List<ApplicationTypeHint> hints)
    {
        if (Mentions(content, "[[bin]]"))
        {
            hints.Add(new ApplicationTypeHint(ApplicationType.CliTool, 50,
                new Evidence("The Rust project declares a binary target, so it builds a runnable program.",
                    EvidenceSource.FileContents)));
        }

        if (Mentions(content, "[lib]"))
        {
            hints.Add(new ApplicationTypeHint(ApplicationType.Library, 40,
                new Evidence("The Rust project declares a library target.", EvidenceSource.FileContents)));
        }

        if (Mentions(content, "tauri"))
        {
            hints.Add(new ApplicationTypeHint(ApplicationType.DesktopApplication, 60,
                new Evidence("The Rust project depends on Tauri, a desktop application toolkit.",
                    EvidenceSource.FileContents)));
        }
    }

    private static void InspectPythonManifest(string content, List<ApplicationTypeHint> hints)
    {
        if (Mentions(content, "console_scripts") || Mentions(content, "[project.scripts]"))
        {
            hints.Add(new ApplicationTypeHint(ApplicationType.CliTool, 55,
                new Evidence("The project installs command-line entry points.", EvidenceSource.FileContents)));
        }

        (string Marker, string Toolkit)[] guiToolkits =
        [
            ("PyQt", "Qt"), ("PySide", "Qt"), ("tkinter", "Tk"),
            ("wxPython", "wxWidgets"), ("kivy", "Kivy")
        ];

        foreach (var (marker, toolkit) in guiToolkits)
        {
            if (!Mentions(content, marker)) continue;

            hints.Add(new ApplicationTypeHint(ApplicationType.DesktopApplication, 55,
                new Evidence($"Depends on {toolkit}, a graphical user interface toolkit.",
                    EvidenceSource.FileContents)));
            break;
        }
    }
}
