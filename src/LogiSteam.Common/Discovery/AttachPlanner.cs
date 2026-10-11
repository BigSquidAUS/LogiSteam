namespace LogiSteam.Common.Discovery;

/// <summary>What the Game PC should do about its local attachments after one beacon.</summary>
/// <param name="Detach">Bus ids to detach: no longer offered, or switched off for this PC.</param>
/// <param name="Forget">Bus ids whose local port has silently died; nothing to detach, just stop tracking.</param>
/// <param name="Attach">Offered devices to attach now.</param>
public sealed record AttachPlan(IReadOnlyList<string> Detach, IReadOnlyList<string> Forget, IReadOnlyList<BeaconDevice> Attach);

/// <summary>
/// Pure decision logic for the Game PC: given what a Wheel PC offers and what is attached here,
/// work out what to detach, what to forget and what to attach. Kept free of any process or
/// network calls so every case can be unit-tested; AttachManager only carries the plan out.
/// </summary>
public static class AttachPlanner
{
    /// <param name="offered">Devices in the beacon.</param>
    /// <param name="attachedPorts">Bus id -> local usbip port, for what this PC believes is attached.</param>
    /// <param name="livePorts">
    /// Ports `usbip port` currently lists, or null if that couldn't be queried -- in which case
    /// nothing is assumed dead (a transient query failure must not cause reattach thrashing).
    /// </param>
    /// <param name="isReleased">Whether the user switched a device ("vvvv:pppp") off for this PC.</param>
    /// <param name="isCoolingDown">Whether a bus id failed to attach too recently to retry yet.</param>
    public static AttachPlan Plan(
        IReadOnlyList<BeaconDevice> offered,
        IReadOnlyDictionary<string, int> attachedPorts,
        IReadOnlyCollection<int>? livePorts,
        Func<string, bool> isReleased,
        Func<string, bool> isCoolingDown)
    {
        var offeredByBusId = offered.GroupBy(d => d.BusId).ToDictionary(g => g.Key, g => g.First());

        var detach = attachedPorts.Keys
            .Where(busId => !offeredByBusId.TryGetValue(busId, out var device) || isReleased(device.HardwareIdText))
            .ToList();

        var forget = livePorts is null
            ? []
            : attachedPorts
                .Where(pair => !detach.Contains(pair.Key) && !livePorts.Contains(pair.Value))
                .Select(pair => pair.Key)
                .ToList();

        var attach = offeredByBusId.Values
            .Where(device => !isReleased(device.HardwareIdText)
                && !isCoolingDown(device.BusId)
                && (!attachedPorts.ContainsKey(device.BusId) || forget.Contains(device.BusId)))
            .ToList();

        return new AttachPlan(detach, forget, attach);
    }
}
