using LogiSteam.Common.DeviceDetection;

namespace LogiSteam.Common.Tests;

public class PresenceDebouncerTests
{
    [Fact]
    public void StartsAsNotPresent()
    {
        var debouncer = new PresenceDebouncer(requiredConsecutiveDisagreements: 3);
        Assert.False(debouncer.CurrentState);
    }

    [Fact]
    public void SingleBriefDisappearance_DoesNotFlipState()
    {
        // Regression scenario: a USB power blip (e.g. from an underpowered hub) or momentary PnP
        // re-enumeration during usbipd-win binding causes one or two transient "not present"
        // readings even though the device never really left. Without debouncing, this used to
        // immediately tear down sharing and cascade into a full detach/reattach cycle.
        var debouncer = new PresenceDebouncer(requiredConsecutiveDisagreements: 3);

        // Bring it to a stable "present" state first (3 consecutive agreeing readings).
        debouncer.Observe(true);
        debouncer.Observe(true);
        debouncer.Observe(true);
        Assert.True(debouncer.CurrentState);

        // Now a single transient blip: one "not present" reading, then back to "present".
        Assert.Null(debouncer.Observe(false));
        Assert.True(debouncer.CurrentState, "a single disagreeing reading should not flip state");

        Assert.Null(debouncer.Observe(true)); // agrees with current state again, resets the counter
        Assert.True(debouncer.CurrentState);
    }

    [Fact]
    public void SustainedDisappearance_FlipsStateAfterThreshold()
    {
        var debouncer = new PresenceDebouncer(requiredConsecutiveDisagreements: 3);
        debouncer.Observe(true);
        debouncer.Observe(true);
        debouncer.Observe(true);
        Assert.True(debouncer.CurrentState);

        Assert.Null(debouncer.Observe(false));
        Assert.Null(debouncer.Observe(false));
        var result = debouncer.Observe(false);

        Assert.False(result); // flips on the 3rd consecutive disagreement
        Assert.False(debouncer.CurrentState);
    }

    [Fact]
    public void IntermittentFlapping_NeverAccumulatesAcrossNonConsecutiveDisagreements()
    {
        var debouncer = new PresenceDebouncer(requiredConsecutiveDisagreements: 3);
        debouncer.Observe(true);
        debouncer.Observe(true);
        debouncer.Observe(true);
        Assert.True(debouncer.CurrentState);

        // Alternating readings never reach 3 *consecutive* disagreements.
        for (var i = 0; i < 10; i++)
        {
            Assert.Null(debouncer.Observe(false));
            Assert.Null(debouncer.Observe(true));
        }

        Assert.True(debouncer.CurrentState);
    }

    [Fact]
    public void Observe_ReturnsNull_WhenReadingAgreesWithCurrentState()
    {
        var debouncer = new PresenceDebouncer(requiredConsecutiveDisagreements: 1);
        Assert.False(debouncer.CurrentState);

        Assert.Null(debouncer.Observe(false));
    }

    [Fact]
    public void ThresholdOfOne_FlipsImmediately()
    {
        var debouncer = new PresenceDebouncer(requiredConsecutiveDisagreements: 1);

        var result = debouncer.Observe(true);

        Assert.True(result);
        Assert.True(debouncer.CurrentState);
    }
}
