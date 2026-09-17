using RepoDeck.Services.Install;

namespace RepoDeck.Tests;

/// <summary>
/// The zip-slip defence. An archive that escapes its destination folder can overwrite
/// anything the process can reach, so these are the most important tests in the suite.
/// </summary>
public class ArchivePathGuardTests
{
    private static readonly string Destination =
        Path.Combine(Path.GetTempPath(), "RepoDeckExtract", "app");

    [Theory]
    [InlineData("tool.exe")]
    [InlineData("bin/tool.exe")]
    [InlineData("deep/nested/folder/resource.dat")]
    [InlineData("./tool.exe")]
    public void Ordinary_entries_resolve_inside_the_destination(string entry)
    {
        var resolved = ArchivePathGuard.ResolveSafePath(Destination, entry);

        Assert.NotNull(resolved);
        Assert.True(ArchivePathGuard.IsInside(Destination, resolved!));
    }

    [Theory]
    [InlineData("../evil.exe")]
    [InlineData("../../evil.exe")]
    [InlineData("../../../../../../windows/system32/evil.dll")]
    [InlineData("bin/../../evil.exe")]
    [InlineData("good/../../../evil.exe")]
    [InlineData("..")]
    [InlineData("../")]
    public void Traversal_entries_are_refused(string entry)
    {
        Assert.Null(ArchivePathGuard.ResolveSafePath(Destination, entry));
    }

    [Theory]
    [InlineData(@"..\evil.exe")]
    [InlineData(@"bin\..\..\evil.exe")]
    public void Traversal_using_backslashes_is_refused(string entry)
    {
        // An archive built on Windows may use backslashes even when read on Linux.
        Assert.Null(ArchivePathGuard.ResolveSafePath(Destination, entry));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/usr/bin/evil")]
    [InlineData("C:/Windows/System32/evil.dll")]
    [InlineData(@"C:\Windows\System32\evil.dll")]
    [InlineData("//server/share/evil.exe")]
    [InlineData("~/.bashrc")]
    public void Absolute_and_rooted_entries_are_refused(string entry)
    {
        Assert.Null(ArchivePathGuard.ResolveSafePath(Destination, entry));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("...")]
    public void Empty_and_degenerate_entries_are_refused(string entry)
    {
        Assert.Null(ArchivePathGuard.ResolveSafePath(Destination, entry));
    }

    [Fact]
    public void A_sibling_directory_with_a_shared_prefix_is_not_inside()
    {
        // "app-evil" starts with "app" as a string but is a different folder.
        var sibling = Path.Combine(Path.GetTempPath(), "RepoDeckExtract", "app-evil", "x.exe");

        Assert.False(ArchivePathGuard.IsInside(Destination, sibling));
    }

    [Fact]
    public void The_destination_itself_is_not_inside_itself()
    {
        Assert.False(ArchivePathGuard.IsInside(Destination, Destination));
    }

    [Fact]
    public void A_file_directly_in_the_destination_is_inside()
    {
        Assert.True(ArchivePathGuard.IsInside(Destination, Path.Combine(Destination, "tool.exe")));
    }
}
