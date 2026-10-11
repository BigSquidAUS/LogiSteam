using System.ComponentModel;
using System.Net;
using LogiSteam.Common.Dashboard;
using LogiSteam.Common.Discovery;
using LogiSteam.Common.GHub;
using LogiSteam.Common.Tray;
using LogiSteam.Common.Updates;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.Roles;

/// <summary>One device a Wheel PC offers and what this Game PC has done with it.</summary>
/// <param name="Port">The local usbip port it is attached on, when connected.</param>
public sealed record DeviceLink(BeaconDevice Device, OfferedDeviceState State, int? Port = null, string? Error = null)
{
    public string Name => Device.Name ?? (Device.IsWheel ? "Racing wheel" : $"USB device {Device.HardwareIdText}");
}

/// <summary>
/// Drives the Game PC's side of the zero-config handshake. Every beacon from a Wheel PC lists
/// the devices it offers (the wheel, plus any additional devices the user added there); this
/// reconciles what is attached locally against that list -- attaching what's newly offered,
/// detaching what's gone or what the user switched off here, and re-attaching anything that
/// silently died. This is the piece usbip-win2 itself doesn't provide (it has no
/// auto-attach/policy engine, unlike usbipd-win's AutoBind policies on the Wheel PC side).
///
/// Each device is handled independently, so one that refuses to attach never holds up the rest.
/// Every beacon processed also gets a return ack sent to the Wheel PC (see AckSender), so the
/// Wheel PC can show which of its devices this PC has actually attached.
/// </summary>
/// <param name="isReleased">
/// Whether the user has switched a device (by "vvvv:pppp" hardware id) off for this PC. A
/// released device is never attached, and is detached if it already is -- which is how a
/// device added by mistake on the Wheel PC (its keyboard, say) can always be handed back from here.
/// </param>
public sealed class AttachManager(BeaconListener listener, UsbipCliWrapper usbip, GHubMonitor gHub, Func<string, bool>? isReleased = null)
{
    // The G29 runs a ~10 second autocentering calibration on connect, during which an attach
    // attempt can fail or the device can otherwise be unresponsive. Without a cooldown, a failed
    // attach would get retried on every beacon (~2s), potentially hammering the device mid
    // calibration and prolonging exactly the condition causing the failure in the first place.
    private static readonly TimeSpan AttachRetryCooldown = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<string, bool> _isReleased = isReleased ?? (_ => false);

    // Touched only while holding _gate.
    private readonly Dictionary<string, AttachedDevice> _attached = [];
    private readonly Dictionary<string, (DateTime AtUtc, string Error)> _failures = [];

    // Replaced wholesale at the end of each reconcile, so other threads always read a consistent snapshot.
    private volatile IReadOnlyList<DeviceLink> _links = [];
    private volatile string? _attachedHost;

    public event Action<TrayStatus>? StatusChanged;

    /// <summary>Every device the current Wheel PC offers, with what this PC has done about each.</summary>
    public IReadOnlyList<DeviceLink> Links => _links;

    /// <summary>The Wheel PC's address the current attachments came from, if any.</summary>
    public string? AttachedHost => _attachedHost;

    public void Start()
    {
        listener.WheelPcDiscoveredOrUpdated += OnDiscoveredOrUpdated;
        listener.WheelPcLost += OnLost;
        listener.Start();
        StatusChanged?.Invoke(new TrayStatus("Searching for a Wheel PC...", TrayStatusLevel.Idle));
    }

    private async void OnDiscoveredOrUpdated(DiscoveredWheelPc pc)
    {
        // Beacons arrive every couple of seconds and an attach can take longer than that. One
        // that arrives mid-reconcile is simply skipped (the next one picks up from the new
        // state) rather than queued, so slow attaches never pile up into repeated work.
        if (await _gate.WaitAsync(0).ConfigureAwait(false))
        {
            try
            {
                await ReconcileAsync(pc).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        await AckAsync(pc.Address).ConfigureAwait(false);
    }

    private async Task ReconcileAsync(DiscoveredWheelPc pc)
    {
        var host = pc.Address.ToString();

        // Attached to a different Wheel PC: let go of its devices before taking this one's.
        if (_attached.Count > 0 && _attachedHost != host && !await DetachAllInternalAsync().ConfigureAwait(false))
        {
            return;
        }

        // Deliberately NOT gated on pc.Beacon.Shared: usbipd-win's AutoBind policy only actually
        // binds a device when a remote client requests to attach it -- that's the trigger, not
        // a precondition. Waiting for "Shared: true" before ever attempting an attach would be a
        // deadlock (it can only become true as a side effect of the attach we're refusing to try).
        var offered = pc.Beacon.AllDevices();
        var offeredBusIds = offered.Select(d => d.BusId).ToHashSet();

        // `usbip port` tells us whether what we think is attached still is: an attachment can
        // silently die (sleep/resume, a driver hiccup) and the beacon alone can't reveal that,
        // since the Wheel PC still sees its side as fine.
        var livePorts = _attached.Count > 0 ? await TryListPortsAsync().ConfigureAwait(false) : null;
        var plan = AttachPlanner.Plan(
            offered,
            _attached.ToDictionary(pair => pair.Key, pair => pair.Value.Port),
            livePorts,
            _isReleased,
            busId => _failures.TryGetValue(busId, out var failure) && DateTime.UtcNow - failure.AtUtc < AttachRetryCooldown);

        // 1. Detach what is no longer offered (unplugged, removed on the Wheel PC, or its bus id
        //    changed) and what the user switched off for this PC.
        foreach (var busId in plan.Detach)
        {
            await DetachOneInternalAsync(_attached[busId]).ConfigureAwait(false);
        }

        // 2. Stop tracking attachments that died on their own.
        foreach (var busId in plan.Forget)
        {
            _attached.Remove(busId);
        }

        // 3. Attach what is offered, wanted here, and not attached.
        foreach (var device in plan.Attach)
        {
            if (_attached.ContainsKey(device.BusId))
            {
                continue; // its detach in step 1 didn't take; try again next beacon
            }

            var name = device.Name ?? "device";
            Publish(pc, offered, new TrayStatus($"Attaching {name} from {pc.Beacon.HostName}...", TrayStatusLevel.Warning));
            try
            {
                _attached[device.BusId] = await usbip.AttachAsync(host, device.BusId).ConfigureAwait(false);
                _failures.Remove(device.BusId);
            }
            catch (UsbipCommandException ex)
            {
                _failures[device.BusId] = (DateTime.UtcNow, $"{name} couldn't be attached to this PC ({ex.Message.Trim()}). Trying again shortly.");
            }
            catch (Win32Exception)
            {
                _failures[device.BusId] = (DateTime.UtcNow,
                    "usbip-win2, the tool LogiSteam uses to connect devices, isn't installed on this PC. Run the LogiSteam installer again to add it.");
            }
        }

        foreach (var staleBusId in _failures.Keys.Where(busId => !offeredBusIds.Contains(busId)).ToList())
        {
            _failures.Remove(staleBusId);
        }

        _attachedHost = _attached.Count > 0 ? host : null;
        Publish(pc, offered, Summarize(pc, offered));
    }

    /// <summary>One line for the tray tooltip and diagnostics: the wheel's state if a wheel is offered, else the first device's.</summary>
    private TrayStatus Summarize(DiscoveredWheelPc pc, IReadOnlyList<BeaconDevice> offered)
    {
        var primary = offered.FirstOrDefault(d => d.IsWheel) ?? offered.FirstOrDefault();
        if (primary is null)
        {
            return new TrayStatus($"{pc.Beacon.HostName} isn't offering any devices", TrayStatusLevel.Idle);
        }

        var name = primary.Name ?? "device";
        if (_attached.TryGetValue(primary.BusId, out var attachment))
        {
            var others = _attached.Count - 1;
            var suffix = others > 0 ? $", plus {others} more" : string.Empty;
            return new TrayStatus($"Attached - {name} from {pc.Beacon.HostName} (local port {attachment.Port}){suffix}", TrayStatusLevel.Connected);
        }

        if (_isReleased(primary.HardwareIdText))
        {
            return new TrayStatus($"{name} from {pc.Beacon.HostName} is switched off for this PC", TrayStatusLevel.Idle);
        }

        return _failures.TryGetValue(primary.BusId, out var failure)
            ? new TrayStatus(failure.Error, TrayStatusLevel.Error)
            : new TrayStatus($"Attaching {name} from {pc.Beacon.HostName}...", TrayStatusLevel.Warning);
    }

    private void Publish(DiscoveredWheelPc pc, IReadOnlyList<BeaconDevice> offered, TrayStatus status)
    {
        _links = offered
            .Select(device =>
                _attached.TryGetValue(device.BusId, out var attachment) ? new DeviceLink(device, OfferedDeviceState.Connected, attachment.Port)
                : _isReleased(device.HardwareIdText) ? new DeviceLink(device, OfferedDeviceState.Released)
                : _failures.TryGetValue(device.BusId, out var failure) ? new DeviceLink(device, OfferedDeviceState.Failed, Error: failure.Error)
                : new DeviceLink(device, OfferedDeviceState.Connecting))
            .ToList();
        StatusChanged?.Invoke(status);
    }

    private async Task<IReadOnlyList<int>?> TryListPortsAsync()
    {
        try
        {
            return await usbip.ListPortsAsync().ConfigureAwait(false);
        }
        catch (UsbipCommandException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private async void OnLost(IPAddress address)
    {
        if (_attachedHost != address.ToString())
        {
            // Nothing attached from it, but its devices shouldn't linger in the list.
            if (_attachedHost is null)
            {
                _links = [];
                StatusChanged?.Invoke(new TrayStatus("Searching for a Wheel PC...", TrayStatusLevel.Idle));
            }

            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DetachAllInternalAsync().ConfigureAwait(false);
            _links = [];
            StatusChanged?.Invoke(new TrayStatus("Wheel PC lost - detached, searching again...", TrayStatusLevel.Idle));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <returns>
    /// True once every local attachment is confirmed clear (or there were none); false if a real
    /// attachment is still occupying a port, so callers should not attach afresh this cycle.
    /// </returns>
    private async Task<bool> DetachAllInternalAsync()
    {
        var allClear = true;
        foreach (var attachment in _attached.Values.ToList())
        {
            allClear &= await DetachOneInternalAsync(attachment).ConfigureAwait(false);
        }

        if (_attached.Count == 0)
        {
            _attachedHost = null;
        }

        return allClear;
    }

    private async Task<bool> DetachOneInternalAsync(AttachedDevice attachment)
    {
        try
        {
            await usbip.DetachAsync(attachment.Port).ConfigureAwait(false);
        }
        catch (UsbipCommandException)
        {
            // Best-effort: the port may already be gone (e.g. the Wheel PC vanished mid-session).
        }
        catch (Win32Exception)
        {
            // usbip.exe missing; nothing more we can do here.
        }

        // usbip.exe's detach reports failure via its return value, not an exception -- confirm the
        // port is actually gone before forgetting about it. Otherwise a detach that silently fails
        // leaves a real local attachment orphaned from our own tracking, and every subsequent
        // attach attempt then fails with "device busy (already exported)" against a "ghost"
        // attachment we no longer know we have. A failed query here is taken as "it's clear":
        // the worst case is a bounded attach-retry-with-cooldown, not a stuck port.
        if (await TryListPortsAsync().ConfigureAwait(false) is { } ports && ports.Contains(attachment.Port))
        {
            return false;
        }

        _attached.Remove(attachment.BusId);
        return true;
    }

    private Task AckAsync(IPAddress wheelPcAddress)
    {
        var fromThisHost = _attachedHost == wheelPcAddress.ToString();
        var links = _links;
        var attachedBusIds = fromThisHost
            ? links.Where(l => l.State == OfferedDeviceState.Connected).Select(l => l.Device.BusId).ToList()
            : [];

        return AckSender.SendAsync(wheelPcAddress, new AckMessage
        {
            HostName = Environment.MachineName,
            // What a Wheel PC older than 2.2 reads: whether the one device it offers is attached.
            Attached = fromThisHost && links.Count > 0 && attachedBusIds.Contains(links[0].Device.BusId),
            AttachedBusIds = attachedBusIds,
            GHubRunning = gHub.IsRunning,
            AppVersion = AppVersion.Current,
        });
    }

    /// <summary>
    /// Detaches everything now. Used on shutdown (so stale VHCI ports don't linger after LogiSteam
    /// exits), on a manual reconnect, and when the user switches a device off: the next beacon
    /// re-attaches whatever is still wanted.
    /// </summary>
    public async Task DetachForShutdownAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DetachAllInternalAsync().ConfigureAwait(false);
            _links = _links.Select(l => l.State == OfferedDeviceState.Connected ? l with { State = OfferedDeviceState.Connecting, Port = null } : l).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }
}
