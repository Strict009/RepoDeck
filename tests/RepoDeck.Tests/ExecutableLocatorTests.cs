using RepoDeck.Models;
using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

public class ExecutableLocatorTests
{
    private static CandidateFile Win(string path) => new(path, false, 1024);
    private static CandidateFile Nix(string path, bool executable = true) => new(path, executable, 1024);

    private static ExecutableSelection SelectWindows(
        IReadOnlyList<CandidateFile> files, string repository = "tool", params string[] predicted) =>
        ExecutableLocator.Select(files, predicted, repository, OsPlatform.Windows);

    [Fact]
    public void The_predicted_executable_is_chosen_when_it_is_present()
    {
        var selection = SelectWindows(
            [Win("other.exe"), Win("tool.exe"), Win("bin/thing.exe")],
            "tool", "tool.exe");

        Assert.Equal("tool.exe", selection.Chosen);
        Assert.Contains("expected to find", selection.Reason);
    }

    [Fact]
    public void An_uninstaller_is_never_chosen_over_the_application()
    {
        // Extracted application folders are full of these.
        var selection = SelectWindows([Win("unins000.exe"), Win("tool.exe")], "tool");

        Assert.Equal("tool.exe", selection.Chosen);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("vcredist_x64.exe")]
    [InlineData("updater.exe")]
    [InlineData("crashpad_handler.exe")]
    [InlineData("tool-helper.exe")]
    public void Supporting_executables_lose_to_the_real_program(string distractor)
    {
        var selection = SelectWindows([Win(distractor), Win("tool.exe")], "tool");

        Assert.Equal("tool.exe", selection.Chosen);
    }

    [Fact]
    public void A_name_matching_the_repository_wins_when_nothing_was_predicted()
    {
        var selection = SelectWindows([Win("launcher.exe"), Win("shotcut.exe")], "shotcut");

        Assert.Equal("shotcut.exe", selection.Chosen);
        Assert.Contains("matches the project name", selection.Reason);
    }

    [Fact]
    public void A_shallower_executable_beats_a_deeply_nested_one()
    {
        var selection = SelectWindows([Win("bin/sub/deep/app.exe"), Win("app.exe")], "unrelated");

        Assert.Equal("app.exe", selection.Chosen);
    }

    [Fact]
    public void Executables_in_support_folders_are_deprioritised()
    {
        var selection = SelectWindows(
            [Win("runtime/dotnet.exe"), Win("bin/tool.exe")], "tool");

        Assert.Equal("bin/tool.exe", selection.Chosen);
    }

    [Fact]
    public void Runners_up_are_offered_as_alternatives()
    {
        var selection = SelectWindows(
            [Win("tool.exe"), Win("tool-cli.exe"), Win("bin/extra.exe")], "tool");

        Assert.Equal("tool.exe", selection.Chosen);
        Assert.NotEmpty(selection.Alternatives);
        Assert.DoesNotContain("tool.exe", selection.Alternatives);
    }

    [Fact]
    public void A_folder_with_no_executables_reports_that_plainly()
    {
        var selection = SelectWindows([Win("readme.txt"), Win("data.json")], "tool");

        Assert.False(selection.Found);
        Assert.Null(selection.Chosen);
        Assert.Contains("did not find", selection.Reason);
    }

    [Fact]
    public void Only_a_disqualified_executable_is_still_offered_but_hedged()
    {
        // Better to offer something with a caveat than to claim there is nothing.
        var selection = SelectWindows([Win("unins000.exe")], "tool");

        Assert.Equal("unins000.exe", selection.Chosen);
        Assert.Contains("not confident", selection.Reason);
    }

    [Fact]
    public void On_Linux_the_executable_bit_makes_a_file_a_candidate()
    {
        var selection = ExecutableLocator.Select(
            [Nix("tool", executable: true), new CandidateFile("readme.md", false, 10)],
            [], "tool", OsPlatform.Linux);

        Assert.Equal("tool", selection.Chosen);
    }

    [Fact]
    public void On_Linux_an_extensionless_file_counts_even_without_permissions()
    {
        // Zip archives do not carry Unix permissions, so this must still work.
        var selection = ExecutableLocator.Select(
            [new CandidateFile("tool", false, 4096), new CandidateFile("notes.txt", false, 10)],
            [], "tool", OsPlatform.Linux);

        Assert.Equal("tool", selection.Chosen);
    }

    [Fact]
    public void An_AppImage_is_recognised_on_Linux()
    {
        var selection = ExecutableLocator.Select(
            [new CandidateFile("Tool-x86_64.AppImage", false, 5000)], [], "tool", OsPlatform.Linux);

        Assert.Equal("Tool-x86_64.AppImage", selection.Chosen);
    }

    [Fact]
    public void Windows_ignores_files_that_are_not_executables()
    {
        var selection = SelectWindows([Win("tool"), Win("tool.dll"), Win("tool.exe")], "tool");

        Assert.Equal("tool.exe", selection.Chosen);
    }

    [Fact]
    public void Backslash_paths_from_Windows_built_archives_are_handled()
    {
        var selection = SelectWindows([Win(@"bin\sub\tool.exe"), Win("other.exe")], "tool");

        Assert.Equal(@"bin\sub\tool.exe", selection.Chosen);
    }
}
