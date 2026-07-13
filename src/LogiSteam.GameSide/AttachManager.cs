using System.ComponentModel;
using System.Net;
using LogiSteam.Common.Discovery;
using LogiSteam.Common.Tray;
using LogiSteam.Common.UsbipInterop;

namespace LogiSteam.GameSide;

/// <summary>
/// Drives the Game PC's side of the zero-config handshake: as soon as a Wheel PC beacon shows
/// the G29 shared, attach it automatically; detach cleanly if the beacon disappears or on app
/// exit. This is the piece usbip-win2 itself doesn't provide (it has no auto-attach/policy
/// engine, unlike usbipd-win's AutoBind policies on the Wheel PC side).
///
/// Every beacon processed also gets a return ack sent to the Wheel PC (see AckSender), so the
/// Wheel PC can show whether a Game PC currently sees it -- not just that it's broadcasting.
/// </summary>
public sealed class AttachManager(BeaconListener listener, UsbipCliWrapper usbip)
{
    // The G29 runs a ~10 second autocentering calibration on connect, during which an attach
    // attempt can fail or the device can otherwise be unresponsive. Without a cooldown, a failed
    // attach would get retried on every beacon (~2s), potentially hammering the device mid
    // calibration and prolonging exactly the condition causing the failure in the first place.
    private static readonly TimeSpan AttachRetryCooldown = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AttachedDevice? _attached;
    private string? _attachedHost;
    private DateTime? _lastFailedAttachUtc;

    public event Action<TrayStatus>? StatusChanged;

    /// <summary>Currently attached device, if any -- read by RemoteDiagnosticsServer's snapshot builder.</summary>
    public AttachedDevice? CurrentAttachment => _attached;

    /// <summary>The Wheel PC's address the current attachment came from, if any.</summary>
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
        // Deliberately NOT gated on pc.Beacon.Shared: usbipd-win's AutoBind policy only actually
        // binds the device when a remote client requests to attach it -- that's the trigger, not
        // a precondition. Waiting for "Shared: true" before ever attempting an attach would be a
        // deadlock (it can only become true as a side effect of the attach we're refusing to try).
        // The beacon's Shared flag is informational only; attach is attempted for any beacon that
        // carries a bus id.
        var host = pc.Address.ToString();
        if (_attached is not null && _attachedHost == host)
        {
            // Already attached to this Wheel PC; nothing to do until it disappears, its bus id
            // changes, or the local attachment turns out to have silently died (sleep/resume, a
            // driver hiccup) -- the beacon alone can't tell us that, since the Wheel PC still
            // sees its side as fine.
            if (_attached.BusId != pc.Beacon.BusId)
            {
                await ReattachAsync(pc, host).ConfigureAwait(false);
            }
            else if (!await IsAttachmentAliveAsync().ConfigureAwait(false))
            {
                StatusChanged?.Invoke(new TrayStatus("Attachment lost - reattaching...", TrayStatusLevel.Warning));
                await ReattachAsync(pc, host).ConfigureAwait(false);
            }

            await AckAsync(pc.Address).ConfigureAwait(false);
            return;
        }

        if (!await _gate.WaitAsync(0).ConfigureAwait(false))
        {
            return; // an attach/detach is already in progress
        }

        try
        {
            if (_attached is not null)
            {
                await DetachInternalAsync().ConfigureAwait(false);
            }

            await AttachInternalAsync(pc, host).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await AckAsync(pc.Address).ConfigureAwait(false);
    }

    /// <summary>
    /// Confirms the port we think we're attached to is still actually imported locally. A query
    /// failure fails open (treated as "still alive") rather than triggering a reattach itself --
    /// a transient `usbip port` hiccup shouldn't cause detach/reattach thrashing on its own; only
    /// a successful query that genuinely omits our port is trusted as "it died."
    /// </summary>
    private async Task<bool> IsAttachmentAliveAsync()
    {
        if (_attached is null)
        {
            return true;
        }

        try
        {
            var ports = await usbip.ListPortsAsync().ConfigureAwait(false);
            return ports.Contains(_attached.Port);
        }
        catch (UsbipCommandException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }

    private async Task ReattachAsync(DiscoveredWheelPc pc, string host)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DetachInternalAsync().ConfigureAwait(false);
            await AttachInternalAsync(pc, host).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task AttachInternalAsync(DiscoveredWheelPc pc, string host)
    {
        if (_lastFailedAttachUtc is { } lastFailure && DateTime.UtcNow - lastFailure < AttachRetryCooldown)
        {
            return; // still cooling down from a recent failure; let the device settle before retrying
        }

        StatusChanged?.Invoke(new TrayStatus($"Attaching G29 from {pc.Beacon.HostName}...", TrayStatusLevel.Warning));
        try
        {
            var attached = await usbip.AttachAsync(host, pc.Beacon.BusId).ConfigureAwait(false);
            _attached = attached;
            _attachedHost = host;
            _lastFailedAttachUtc = null;
            StatusChanged?.Invoke(new TrayStatus($"Attached - G29 from {pc.Beacon.HostName} (local port {attached.Port})", TrayStatusLevel.Connected));
        }
        catch (UsbipCommandException ex)
        {
            _lastFailedAttachUtc = DateTime.UtcNow;
            StatusChanged?.Invoke(new TrayStatus($"Attach failed: {ex.Message} - retrying shortly", TrayStatusLevel.Error));
        }
        catch (Win32Exception)
        {
            _lastFailedAttachUtc = DateTime.UtcNow;
            StatusChanged?.Invoke(new TrayStatus("usbip-win2 not found - install it, then restart LogiSteam", TrayStatusLevel.Error));
        }
    }

    private async void OnLost(IPAddress address)
    {
        if (_attachedHost != address.ToString())
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DetachInternalAsync().ConfigureAwait(false);
            StatusChanged?.Invoke(new TrayStatus("Wheel PC lost - detached, searching again...", TrayStatusLevel.Idle));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DetachInternalAsync()
    {
        if (_attached is null)
        {
            return;
        }

        try
        {
            await usbip.DetachAsync(_attached.Port).ConfigureAwait(false);
        }
        catch (UsbipCommandException)
        {
            // Best-effort: the port may already be gone (e.g. the Wheel PC vanished mid-session).
        }
        catch (Win32Exception)
        {
            // usbip.exe missing; nothing more we can do here.
        }

        _attached = null;
        _attachedHost = null;
    }

    private Task AckAsync(IPAddress wheelPcAddress) =>
        AckSender.SendAsync(wheelPcAddress, new AckMessage
        {
            HostName = Environment.MachineName,
            Attached = _attachedHost == wheelPcAddress.ToString(),
        });

    /// <summary>Called on app shutdown so a stale VHCI port doesn't linger after LogiSteam exits.</summary>
    public async Task DetachForShutdownAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DetachInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
