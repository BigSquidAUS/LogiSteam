using System.Text.Json;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Common.Tests;

public class UsbipdModelsTests
{
    // Shape mirrors Usbipd.Automation.Device/State (dorssel/usbipd-win) as emitted by
    // `usbipd state` -- see UsbipdModels.cs remarks. Reconfirm against a live install during
    // the Phase 0 spike; this is the best-effort shape derived from the upstream source.
    private const string SampleStateJson = """
        {
          "Devices": [
            {
              "BusId": "1-4",
              "ClientIPAddress": null,
              "Description": "Logitech G29 Driving Force Racing Wheel",
              "InstanceId": "USB\\VID_046D&PID_C52B&REV_2411&MI_00",
              "IsForced": false,
              "PersistedGuid": "5b13c8b7-6b8f-4f2e-9c1a-1234567890ab",
              "StubInstanceId": null
            },
            {
              "BusId": "1-2",
              "ClientIPAddress": "192.168.1.50",
              "Description": "Some Other Device",
              "InstanceId": "USB\\VID_1005&PID_B113\\7&2A2B3C4D&0&1",
              "IsForced": false,
              "PersistedGuid": "6c24d9c8-7c9f-5f3f-ad2b-2345678901bc",
              "StubInstanceId": "USB\\VID_1005&PID_B113\\STUB"
            }
          ]
        }
        """;

    [Fact]
    public void Deserialize_ParsesDeviceListAndDerivesHardwareId()
    {
        var state = JsonSerializer.Deserialize<UsbipdState>(SampleStateJson)!;

        Assert.Equal(2, state.Devices.Count);
        var g29 = state.Devices.Single(d => d.BusId == "1-4");
        Assert.Equal(UsbHardwareId.G29, g29.HardwareId);
    }

    [Fact]
    public void IsShared_TrueWhenPersistedGuidPresent()
    {
        var state = JsonSerializer.Deserialize<UsbipdState>(SampleStateJson)!;
        var g29 = state.Devices.Single(d => d.BusId == "1-4");

        Assert.True(g29.IsShared);
    }

    [Fact]
    public void IsAttachedRemotely_TrueOnlyWhenClientIPAddressPresent()
    {
        var state = JsonSerializer.Deserialize<UsbipdState>(SampleStateJson)!;

        var g29 = state.Devices.Single(d => d.BusId == "1-4");
        var other = state.Devices.Single(d => d.BusId == "1-2");

        Assert.False(g29.IsAttachedRemotely);
        Assert.True(other.IsAttachedRemotely);
    }

    [Theory]
    [InlineData("Effect Allow, Operation AutoBind, HardwareId 046d:c52b", true)]
    [InlineData("Effect Allow, Operation AutoBind, BusId 1-4", false)]
    [InlineData("", false)]
    public void PolicyAlreadyExists_MatchesAutoBindLineForHardwareId(string policyListOutput, bool expected)
    {
        Assert.Equal(expected, UsbipdCliWrapper.PolicyAlreadyExists(policyListOutput, UsbHardwareId.G29));
    }
}
