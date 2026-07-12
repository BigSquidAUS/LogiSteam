namespace LogiSteam.Common.DeviceDetection;

/// <summary>
/// Requires a presence reading to disagree with the current known state for several consecutive
/// polls before actually flipping state. Without this, a single transient WMI/PnP hiccup (e.g.
/// during a USB power blip from an underpowered hub, or momentary re-enumeration while
/// usbipd-win binds the device) immediately tears down sharing, which cascades into a full
/// detach on the Game PC side and a slow reconnect -- turning a 1-2 second glitch into a much
/// longer, user-visible connect/disconnect cycle. Extracted as its own small state machine so the
/// debounce behavior itself is unit-testable without WMI.
/// </summary>
public sealed class PresenceDebouncer(int requiredConsecutiveDisagreements = 3)
{
    private bool _lastKnownPresent;
    private int _consecutiveDisagreements;

    public bool CurrentState => _lastKnownPresent;

    /// <summary>
    /// Feeds one new presence reading. Returns the new state if this reading caused a debounced
    /// state change, or null if nothing changed (either the reading agreed with the current state,
    /// or it disagreed but hasn't done so for enough consecutive calls yet).
    /// </summary>
    public bool? Observe(bool present)
    {
        if (present == _lastKnownPresent)
        {
            _consecutiveDisagreements = 0;
            return null;
        }

        _consecutiveDisagreements++;
        if (_consecutiveDisagreements < requiredConsecutiveDisagreements)
        {
            return null;
        }

        _lastKnownPresent = present;
        _consecutiveDisagreements = 0;
        return present;
    }
}
