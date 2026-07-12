using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class UsbHardwareIdTests
{
    [Fact]
    public void G29Constant_HasExpectedVendorAndProduct()
    {
        Assert.Equal(0x046D, UsbHardwareId.G29.Vendor);
        Assert.Equal(0xC52B, UsbHardwareId.G29.Product);
    }

    [Fact]
    public void ToString_FormatsAsLowercaseColonPair()
    {
        Assert.Equal("046d:c52b", UsbHardwareId.G29.ToString());
    }

    [Theory]
    [InlineData(@"USB\VID_046D&PID_C52B&REV_2411&MI_00")] // real Hardware Id, "Logitech USB Input Device" (as seen in Device Manager)
    [InlineData(@"USB\VID_046D&PID_C52B&MI_00")] // real Hardware Id, same device's second entry
    [InlineData(@"USB\VID_046d&PID_c52b\SomeSerial")]
    public void TryParseFromInstanceId_ParsesLogitechG29(string instanceId)
    {
        Assert.True(UsbHardwareId.TryParseFromInstanceId(instanceId, out var id));
        Assert.Equal(UsbHardwareId.G29, id);
    }

    [Fact]
    public void TryParseFromInstanceId_ReturnsFalse_ForNonMatchingText()
    {
        Assert.False(UsbHardwareId.TryParseFromInstanceId(@"USB\ROOT_HUB30\4&1234", out _));
    }

    [Theory]
    [InlineData("046d:c52b")]
    [InlineData("046D:C52B")]
    [InlineData("  046d:c52b  ")]
    public void TryParse_ParsesColonPairForm(string text)
    {
        Assert.True(UsbHardwareId.TryParse(text, out var id));
        Assert.Equal(UsbHardwareId.G29, id);
    }

    [Fact]
    public void TryParse_ReturnsFalse_ForMalformedText()
    {
        Assert.False(UsbHardwareId.TryParse("not-a-hardware-id", out _));
    }
}
