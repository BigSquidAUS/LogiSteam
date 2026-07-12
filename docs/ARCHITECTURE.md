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
| `LogiSteam.WheelSide` | Wheel PC | Registers `usbipd-win`'s AutoBind policy once; watches for the user-selected device; beacons its bus id on the LAN | (this repo) |
| `LogiSteam.GameSide` | Game PC | Discovers the Wheel PC's beacon; auto-attaches/detaches via `usbip-win2`'s CLI; acks back so the Wheel PC knows it's seen | (this repo) |

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
  why that used to be a real bug. It only drives the picker's live list and an optional "refresh
  sooner" hint.
- `Config/` -- `SelectedDeviceStore` persists the user's chosen device (`%LOCALAPPDATA%\LogiSteam\wheelside-device.json`)
  so the picker (`LogiSteam.WheelSide/DevicePickerForm.cs`) only needs to run once. A hardcoded
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
  devices and whichever one is currently selected. Reachable from the Wheel PC tray menu ("Open
  USB Device Log"). This turns the one-off diagnostic scripts used to track down the bind/rebind/
  bus-move flakiness below into a permanent, built-in feature.
  `RemoteDiagnosticsServer` (both apps) is a plain TCP listener (WheelSide: port 35896, GameSide:
  port 35897) that responds to any connection with a live JSON snapshot of that app's current
  state -- selected/attached device, status, ack state, discovered peers, and (WheelSide only) a
  fresh full USB inventory plus a tail of `usb-devices.log`. Query it with
  `tools/query-diagnostics.ps1 -TargetIp <ip> -Side WheelSide|GameSide`. **This is intentionally
  always-on and unauthenticated** (a deliberate, user-confirmed tradeoff): anyone on the LAN who
  knows the port can read this status data. It cannot change any state -- there is no command
  channel, only a snapshot response -- which keeps the exposure to read-only status information.

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
presence signal, and only uses the WMI watcher for the device picker and as a non-blocking
"refresh sooner" hint.

## Data flow

```
[Wheel PC] first run: DevicePickerForm lists USB devices (UsbDeviceEnumerator) -> user picks one
                      -> persisted via SelectedDeviceStore -> everything below uses that choice

[wheel/device] --USB--> [Wheel PC]
   WheelSide: ensures usbipd-win AutoBind policy for the selected VID:PID (one-time, one UAC prompt)
   WheelSide: watches for the selected device, beacons {hostname, busid, vid, pid, shared} over UDP :35893
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

## Why a device picker instead of a hardcoded VID/PID

Two real incidents motivated this: the G29's Product ID was first sourced from a third-party
VID/PID lookup site (wrong -- it didn't match any real device), then corrected to a value read
directly from one real unit's Device Manager entry (`0xC52B`) -- which then turned out to still
not universally match every setup. Rather than continue hardcoding a "the" G29 identifier that
keeps needing correction, `LogiSteam.WheelSide` now asks the user to confirm the device directly
via `DevicePickerForm`, once, and remembers the choice. This also means LogiSteam isn't
G29-specific at all anymore -- any USB device can be shared this way.

## Known open risk: force feedback over the network

Neither `usbipd-win` nor `usbip-win2` lists a force-feedback wheel in its tested-device list.
There is one documented upstream report of timing-sensitive USB traffic (serial control lines)
degrading over the network hop ([usbipd-win#469](https://github.com/dorssel/usbipd-win/issues/469)).
FFB commands are periodic, latency-sensitive output reports, so this is a real risk to validate
via the Phase 0 spike in `docs/SETUP.md` before depending on this for sim racing -- the
automation in this repo cannot fix a fundamentally unworkable transport, only make a working one
convenient.
