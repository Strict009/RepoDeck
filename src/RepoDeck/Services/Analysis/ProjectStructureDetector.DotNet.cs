using RepoDeck.Models;

namespace RepoDeck.Services.Analysis;

public static partial class ProjectStructureDetector
{
    private static void InspectDotNetProject(
        string content, List<string> frameworks, List<Evidence> evidence, List<ApplicationTypeHint> hints)
    {
        if (Mentions(content, "Avalonia"))
        {
            frameworks.Add("Avalonia");
            var reason = new Evidence(
                "The project references Avalonia, a cross-platform desktop user interface toolkit.",
                EvidenceSource.FileContents);
            evidence.Add(reason);
            hints.Add(new ApplicationTypeHint(ApplicationType.DesktopApplication, 70, reason));
        }

        if (Mentions(content, "<UseWPF>true") || Mentions(content, "Microsoft.NET.Sdk.WindowsDesktop"))
        {
            frameworks.Add("WPF");
            var reason = new Evidence(
                "The project uses WPF, a Windows-only desktop user interface toolkit.",
                EvidenceSource.FileContents);
            evidence.Add(reason);
            hints.Add(new ApplicationTypeHint(ApplicationType.DesktopApplication, 70, reason));
        }

        if (Mentions(content, "<UseWindowsForms>true"))
        {
            frameworks.Add("Windows Forms");
            var reason = new Evidence(
                "The project uses Windows Forms, a Windows-only desktop user interface toolkit.",
                EvidenceSource.FileContents);
            evidence.Add(reason);
            hints.Add(new ApplicationTypeHint(ApplicationType.DesktopApplication, 70, reason));
        }

        if (Mentions(content, "Microsoft.Maui"))
        {
            frameworks.Add(".NET MAUI");
            hints.Add(new ApplicationTypeHint(ApplicationType.MobileApplication, 40,
                new Evidence("The project uses .NET MAUI, which targets mobile and desktop.",
                    EvidenceSource.FileContents)));
        }

        if (Mentions(content, "Microsoft.AspNetCore"))
        {
            frameworks.Add("ASP.NET Core");
            hints.Add(new ApplicationTypeHint(ApplicationType.Server, 50,
                new Evidence("The project references ASP.NET Core, a web server framework.",
                    EvidenceSource.FileContents)));
        }

        // OutputType is the clearest statement a .NET project makes about itself.
        if (Mentions(content, "<OutputType>WinExe"))
        {
            hints.Add(new ApplicationTypeHint(ApplicationType.DesktopApplication, 55,
                new Evidence("The project builds a windowed application.", EvidenceSource.FileContents)));
        }
        else if (Mentions(content, "<OutputType>Exe"))
        {
            hints.Add(new ApplicationTypeHint(ApplicationType.CliTool, 45,
                new Evidence("The project builds a command-line executable.", EvidenceSource.FileContents)));
        }
        else
        {
            // A .NET project with no OutputType builds a library by default.
            hints.Add(new ApplicationTypeHint(ApplicationType.Library, 35,
                new Evidence("The project builds a library rather than a program that can be run.",
                    EvidenceSource.FileContents)));
        }

        if (Mentions(content, "<IsPackable>true") || Mentions(content, "<PackageId>"))
        {
            hints.Add(new ApplicationTypeHint(ApplicationType.Library, 30,
                new Evidence("The project is packaged for other developers to consume via NuGet.",
                    EvidenceSource.FileContents)));
        }
    }
}
