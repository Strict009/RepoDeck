using RepoDeck.Views;

namespace RepoDeck.Tests;

public class ResponsiveShellTests
{
    [Theory]
    [InlineData(959.99, true)]
    [InlineData(960, false)]
    [InlineData(960.01, false)]
    public void Shell_changes_state_at_the_declared_client_width(double width, bool compact)
    {
        Assert.Equal(compact, MainWindow.UsesCompactShell(width));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Unmeasured_or_invalid_width_does_not_force_compact_state(double width)
    {
        Assert.False(MainWindow.UsesCompactShell(width));
    }
}
