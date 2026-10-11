# LogiSteam Architecture

## Why USB tunneling instead of a virtual gamepad

Steam Remote Play's controller forwarding only reliably carries standard XInput/gamepad signals.
Two simpler alternatives were considered and rejected before this design:

- **DirectInput -> virtual XInput bridge** (read the G29 as a DirectInput joystick, re-emit it as
  a virtual Xbox 360 controller via ViGEmBus): a few days of user-mode work, but permanently rules
  out force feedback and most of the G29's buttons -- Steam would just be forwarding a gamepad
  signal, not the real wheel.
- **A from-scratch USB-over-network kernel driver stack**: preserves full fidelity, but is a
  multi-month Windows driver development effort (virtual host controller, URB forwarding, wire
  protocol, driver signing) -- essentially reimplementing VirtualHere/usbip-win2 from zero.

LogiSteam instead automates two existing, protocol-compatible open-source USB/IP projects for
Windows, so the wheel's actual USB traffic (including force feedback commands sent to the wheel)
is tunneled rather than translated into a lossy gamepad signal.

## Components

| Project | Runs on | Role | License |
|---|---|---|---|
| [`dorssel/usbipd-win`](https://github.com/dorssel/usbipd-win) | Wheel PC | USB/IP server: shares the physically attached G29 | GPL-3.0 |
| [`vadimgrn/usbip-win2`](https://github.com/vadimgrn/usbip-win2) | Game PC | USB/IP client: attaches the shared G29 via its VHCI virtual bus driver | BSD-2-Clause |
| `LogiSteam.App` (`WheelRole`) | Wheel PC | Identifies the wheel; registers `usbipd-win`'s AutoBind policy once; beacons its bus id on the LAN | (this repo) |
| `LogiSteam.App` (`GameRole`) | Game PC | Discovers the Wheel PC's beacon; auto-attaches/detaches via `usbip-win2`'s CLI; acks back so the Wheel PC knows it's seen | (this repo) |

`LogiSteam.App` is a single executable (`LogiSteam.exe`) installed on both PCs. `AppController`
owns the WPF main window, the tray icon and exactly one running role (`IRole`): `WheelRole` or
`GameRole`, chosen with the switch at the top of the window and persisted in
`%LOCALAPPDATA%\LogiSteam\settings.json`. Switching stops one role (releasing its ports, and
detaching the wheel if it was the Game PC) and starts the other. Where this document says
"WheelSide"/"GameSide" below, read "the app in its Wheel PC role"/"in its Game PC role" -- they
were separate executables in 1.x. A second launch (desktop shortcut) doesn't start a second
copy; `SingleInstance` asks the running one to show its window.

What the window says is not decided in the UI. `LogiSteam.Common/Dashboard` holds two pure
functions, `WheelSideDashboard.Build` and `GameSideDashboard.Build`, that turn a role's raw
state into a `DashboardState` (headline, detail, colour level, checklist steps, warnings). The
window and the tray icon both render that, so they can never disagree, and the wording is
covered by unit tests.

Logitech G HUB has to be running on the Game PC, and optionally on the Wheel PC too (the
`RequireGHubOnWheelPc` setting, off by default, toggled under Advanced -- the fallback for a
setup where the wheel needs it at both ends). `GHubMonitor` checks for its processes locally
(`lghub` or `lghub_agent`; not `lghub_updater`, a service that outlives G HUB), and each side
reports its answer to the other in the beacon/ack (`gHubRunning`), so both windows can warn
about the other PC. `GHubStatus.RequiredHere`/`RequiredOnPeer` carry which ends are required, so
the same warning logic serves both roles and both settings. A peer running 1.x doesn't send the field, which is treated as unknown rather
than as a warning.

`LogiSteam.Common` holds everything shared between the two tray apps:

- `Discovery/` -- two independent UDP protocols, both zero-config (no typed-in IPs):
  - `BeaconMessage`/`BeaconBroadcaster`/`BeaconListener` (port 35893): Wheel PC -> Game PC, "here's my shared device."
  - `AckMessage`/`AckSender`/`AckListener` (port 35894): Game PC -> Wheel PC, "I see you, and here's whether I've attached." This is what drives the Wheel PC's "connected to GAME-PC" tray status -- without it, a Wheel PC broadcasting into the void would look identical to one a Game PC has actually found.
- `UsbipInterop/` -- thin wrappers that shell out to `usbipd.exe` (Wheel PC) and `usbip.exe`
  (Game PC) and parse their output. Both upstream CLIs are treated as external dependencies,
  invoked as separate processes -- never vendored or statically linked, both for licensing
  reasons (`usbipd-win` is GPL-3.0) and because `usbip-win2`'s C++ SDK (`libusbip`) has had
  breaking ABI changes between releases, making CLI text-parsing the lower-maintenance choice.
  `usbipd-win`'s installer adds itself to PATH; `usbip-win2`'s does not (confirmed by inspecting
  its own Inno Setup script -- no PATH-related entries at all), so `UsbipCliWrapper` resolves
  `usbip.exe`'s real location from its Inno Setup uninstall registry entry
  (`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{199505B0-B93D-4521-A8C7-897818E0205A}_is1`,
  checking both the native and WOW6432Node views) rather than assuming a bare `usbip` resolves.
- `DeviceDetection/` -- `UsbDeviceEnumerator` lists currently-plugged-in USB devices (deduplicated
  by VID:PID, since one physical device can show up as several PnP entries -- one per USB
  function/interface); `UsbInventoryDiff` compares two snapshots and reports arrivals/departures/
  moves. `WheelDeviceWatcher` polls Windows PnP presence (via WMI) for the user-picked device, but
  is **not** used to gate sharing/beaconing -- see the "Why usbipd state, not WMI" note below for
  why that used to be a real bug. It only drives an optional "refresh
  sooner" hint.
- `Config/` -- `SelectedDeviceStore` persists the user's chosen device (`%LOCALAPPDATA%\LogiSteam\wheelside-device.json`)
  so identification survives restarts and the wheel being unplugged. A hardcoded
  VID/PID assumption for the G29 was wrong twice in practice (once from a stale third-party
  lookup, once from real per-unit/firmware variance) -- letting the user confirm the device
  directly is more reliable than guessing a single constant ever again.
- `Tray/` -- `TrayStatus`/`TrayStatusLevel` (a small enum-based status shared by both apps, so
  icon selection isn't fragile substring-matching of human-readable text) and `TrayIconPalette`,
  which draws a simple steering-wheel glyph colored per status level (gray = idle, amber =
  warning, green = connected, red = error) rather than borrowing generic Windows stock icons.
- `Diagnostics/` -- `CrashLogger` (both apps) writes unhandled exceptions to
  `%LOCALAPPDATA%\LogiSteam\logs\` with a message box, so a startup failure is never silent.
  `UsbDeviceInventoryLogger` (Wheel PC only) polls the full USB device list, diffs it against the
  previous snapshot via `UsbInventoryDiff`, and appends only the *changes* (arrival/departure/
  moved) to `%LOCALAPPDATA%\LogiSteam\logs\usb-devices.log` (5MB rotation), tagging Logitech-vendor
  devices and whichever one is currently selected. Reachable from Advanced > USB device log on the Wheel PC. This turns the one-off diagnostic scripts used to track down the bind/rebind/
  bus-move flakiness below into a permanent, built-in feature.
  `RemoteDiagnosticsServer` (both apps) is a plain TCP listener (WheelSide: port 35896, GameSide:
  port 35897) that responds to any connection with a live JSON snapshot of that app's current
  state -- selected/attached device, status, ack state, discovered peers, and (WheelSide only) a
  fresh full USB inventory plus a tail of `usb-devices.log`. Query it with
  `tools/query-diagnostics.ps1 -TargetIp <ip> -Side WheelSide|GameSide`. **This is intentionally
  always-on and unauthenticated** (a deliberate, user-confirmed tradeoff): anyone on the LAN who
  knows the port can read this status data. It cannot change any state -- there is no command
  channel, only a snapshot response -- which keeps the exposure to read-only status information.
  `RemoteDiagnosticsClient` is the in-app counterpart on GameSide (the "Query Wheel PC" button --
  see below), so most troubleshooting no longer needs the PowerShell script at all.
  `HidInputTester` wraps HidSharp to read raw HID input reports live from a set of hardware ids,
  productizing the ad-hoc scratch scripts used to confirm the G29's input was flowing end-to-end
  during this project's troubleshooting. Both roles expose an Advanced > Diagnostics window
  (`WheelSideDiagnosticsForm`/`GameSideDiagnosticsForm`, non-modal, single-instance) with a
  Connectivity panel (the same snapshot JSON above, auto-refreshing) and an Input Test panel
  (`HidInputTester` against the current device selection, showing live raw report bytes) --
  WheelSide tests against `SelectedDevice.HardwareIds`; GameSide has no fixed hardware-id list
  (it tracks the attachment by bus id/port, not hardware id), so its Input Test sources from a
  dropdown of currently-present USB devices instead. Note: HidSharp's `TryOpen` will fail on
  Windows' exclusively-locked boot-protocol HID collections (e.g. a mouse's actual pointer
  collection) -- this is expected OS behavior, not a bug, and doesn't affect game controllers like
  the G29, which don't have that exclusivity restriction.

## Why `usbipd state`, not WMI, is the presence signal

An earlier version of `WheelSide` gated its "is the device here" status on `WheelDeviceWatcher`'s
WMI check. That was a real bug: `usbipd-win` replaces a shared device's driver with a stub
("USBIP Shared Device" in Device Manager) while it's bound, which the physical device sees as a
reset -- consistent with the G29 running its ~10 second autocentering calibration on every bind,
not just the first. That stub swap also means the *original* `VID:PID` PnP entry legitimately
disappears from WMI while the device is being successfully shared. Gating status on WMI meant
LogiSteam would see a healthy share as "unplugged," stop beaconing, cause the Game PC to detach
(unbinding the device, restoring the original entry), which the watcher would then see as "back,"
triggering a new attach that binds it again -- a bind/unbind feedback loop LogiSteam was driving
against itself, with a wheel that was never actually disconnected. `usbipd state` is
`usbipd-win`'s own authoritative view and already correctly tracks the device through binding/
stub-driver changes (that's the whole point of the tool), so `WheelSide` now uses it alone as the
presence signal, and only uses the WMI watcher as a non-blocking
"refresh sooner" hint.

Since a wheel is now a multi-persona profile (see above), `usbipd state` can legitimately list
*several* candidate devices at once right after a mode switch -- the previously-live persona's
entry going stale (no `BusId`) while the newly-live persona's entry appears. `UsbipdCliWrapper
.SelectBestMatch` handles this by preferring a candidate with a live (non-null) `BusId` over one
that only matches historically, so a mode switch resolves to the currently-live persona rather
than a stale one.

## Data flow

```
[Wheel PC] WheelAutoDetector identifies the wheel among the plugged-in USB devices (no dialog)
           -> persisted via SelectedDeviceStore -> everything below uses that selection

[wheel/device] --USB--> [Wheel PC]
   WheelSide: ensures usbipd-win AutoBind policy for the selected VID:PID (one-time, one UAC prompt)
   WheelSide: watches for the selected device, beacons {hostname, busid, vid, pid, shared, deviceName, gHubRunning} over UDP :35893
   WheelSide: listens for acks on UDP :35894 (AckListener) -> tray shows "connected to GAME-PC" once one arrives

[Game PC]
   GameSide: listens for the beacon (BeaconListener)
   GameSide: on a "shared" beacon, runs `usbip attach -r <wheel-pc-ip> -b <busid>` (AttachManager)
   GameSide: sends an ack back to the Wheel PC after every beacon it processes (AckSender)
   GameSide: on beacon loss or app exit, runs `usbip detach -p <port>`

Windows on the Game PC now sees the device as locally attached (via usbip-win2's VHCI driver) --
the normal driver stack and Steam/games interact with it exactly as if it were physically plugged
in. Steam Remote Play only has to stream video/audio; LogiSteam and the USB/IP projects handle
100% of the input (and force-feedback, if Phase 0 confirms it works).
```

## How the wheel is identified (and why not one hardcoded VID/PID)

The G29 isn't one fixed VID:PID. It has a physical PS3/PS4 mode switch, and the *same physical
wheel* can enumerate under different product ids (`046D:C24F` and `046D:C260`, both confirmed on
one real unit; with G HUB running, that unit reports `C24F` in either switch position). A single
hardcoded id therefore strands the connection the moment the wheel is in the other mode.

So "the wheel" is a **named profile backed by a set of hardware ids**
(`SelectedDevice { DisplayName, HardwareIds, Source }`), and `WheelAutoDetector` identifies it
with no user involvement, in two steps:

1. **Catalog** (`KnownWheelProfiles`): Logitech wheel models and every id each can report. The
   G29's ids were observed on real hardware; the rest come from the Linux kernel's Logitech
   wheel drivers (`drivers/hid/hid-ids.h`).
2. **Product name**: a Logitech device the catalog doesn't list is accepted if its USB product
   string (read via HidSharp, `HidProductNameProbe`) reads like a racing wheel. Names containing
   "mouse", "keyboard", "receiver" and the like are rejected even if they contain "wheel".

`WheelAutoDetector.ReconcileSelection` re-runs this on every refresh tick, so a mode switch or a
swapped wheel heals itself. It never overrides a manual choice (`Source = Manual`, made under
Advanced > Choose the wheel manually -- the fallback for an unrecognized wheel, and the only
place a device list exists), and it drops a saved selection that isn't a recognizable wheel at
all, which is what a 1.x manual pick of the wrong device looks like.

A cautionary note on `046D:C52B`: 1.x listed it as a third G29 "native mode" id. It is
Logitech's Unifying wireless receiver (`USB_DEVICE_ID_LOGITECH_UNIFYING_RECEIVER`), which shows
up in Device Manager as "Logitech USB Input Device". With it in the catalog, a Wheel PC with a
Logitech mouse/keyboard dongle could identify, and share, the dongle as "the G29". It was
removed, a saved selection containing it is cleaned up automatically, and a test guards against
it coming back.

## Known open risk: force feedback over the network

Neither `usbipd-win` nor `usbip-win2` lists a force-feedback wheel in its tested-device list.
There is one documented upstream report of timing-sensitive USB traffic (serial control lines)
degrading over the network hop ([usbipd-win#469](https://github.com/dorssel/usbipd-win/issues/469)).
FFB commands are periodic, latency-sensitive output reports, so this is a real risk to validate
via the Phase 0 spike in `docs/SETUP.md` before depending on this for sim racing -- the
automation in this repo cannot fix a fundamentally unworkable transport, only make a working one
convenient.

## Updates

Both PCs need to run compatible versions, so each one tells the other its version in the
beacon/ack (`appVersion`) and `UpdateNotices` turns the comparison into dashboard cards: "an
update is available" (with an Update button) on the PC that is behind, and "the other PC is out
of date" on the one that is ahead.

The installer itself is only ever fetched from this project's GitHub releases (`UpdateChecker`):
the app looks up the latest release at startup, every six hours, and promptly when the peer
reports a version newer than any it knows about. A peer's version is treated purely as a hint to
go and look. It is never the source of the file, because running a program handed over by
whatever machine answers on the LAN would give any device on the network a way to execute code
here. The download URL must be under this repository's release downloads, and the file is
checked against the size and SHA-256 GitHub publishes for the asset before it is run.

Installing is always started by the user clicking Update. It needs a Windows elevation prompt
and drops the wheel for a few seconds, neither of which should happen unprompted mid-race. The
app stops its role cleanly (detaching the wheel), runs the installer with `/SILENT`, and the
installer restarts the new version as the signed-in user.

## Additional devices

The Wheel PC can share more than the wheel. The user adds devices by name (`AddDeviceWindow`);
they are remembered by hardware id (`wheelside-extra-devices.json`) and offered whenever they
are plugged in. A beacon now carries a `devices` list (the wheel first, flagged `isWheel`), and
an ack carries `attachedBusIds`. The original single-device fields are still filled in with the
first device, so a 2.1 peer keeps working with just that one.

On the Game PC, `AttachPlanner` is the pure decision step: from what is offered, what is
attached, which local ports are still alive and what the user has switched off, it returns what
to detach, forget and attach. `AttachManager` carries that plan out, one device at a time, so a
device that won't attach never blocks the others, and a beacon arriving mid-reconcile is skipped
rather than queued.

Guard rails, because the list is every USB device on the PC (`UsbDeviceClassifier`, from the
Windows device classes and driver services on the device and the nodes beneath it):

- **Never offered:** storage, network adapters, Bluetooth adapters, hubs.
- **Offered with an explicit confirmation:** keyboards and mice (it stops working on the Wheel
  PC while attached), audio and video (poor over USB/IP).
- **Always reversible from the Game PC:** each offered device can be switched off there
  (`gameside-released-devices.json`), which detaches it and stops re-attaching it. That is the
  way back if the Wheel PC's own keyboard or mouse was added by mistake.

G HUB only applies when a Logitech wheel is involved, so its checklist row and warnings are
dropped for someone sharing only other devices.
