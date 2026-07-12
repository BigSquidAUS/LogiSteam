using LogiSteam.Common.Tray;

namespace LogiSteam.Common.Tests;

public class TrayIconPaletteTests
{
    [Theory]
    [InlineData(TrayStatusLevel.Idle)]
    [InlineData(TrayStatusLevel.Warning)]
    [InlineData(TrayStatusLevel.Connected)]
    [InlineData(TrayStatusLevel.Error)]
    public void GetIcon_ReturnsNonNullIconForEveryLevel(TrayStatusLevel level)
    {
        Assert.NotNull(TrayIconPalette.GetIcon(level));
    }

    [Fact]
    public void GetIcon_ReturnsCachedInstance_NotANewIconEachCall()
    {
        // NotifyIcon doesn't own the underlying GDI handle, so regenerating an icon on every
        // status refresh would leak a handle each time -- verify icons are actually cached.
        var first = TrayIconPalette.GetIcon(TrayStatusLevel.Warning);
        var second = TrayIconPalette.GetIcon(TrayStatusLevel.Warning);

        Assert.Same(first, second);
    }

    [Fact]
    public void GetIcon_DifferentLevelsProduceDifferentIcons()
    {
        var idle = TrayIconPalette.GetIcon(TrayStatusLevel.Idle);
        var error = TrayIconPalette.GetIcon(TrayStatusLevel.Error);

        Assert.NotSame(idle, error);
    }
}
