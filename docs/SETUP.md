# LogiSteam Setup

## Phase 0: manual validation spike (do this first)

Before installing LogiSteam at all, confirm the underlying approach actually works for the G29
using the stock CLIs -- no LogiSteam code involved. Neither `usbipd-win` nor `usbip-win2` lists a
Logitech wheel (or any force-feedback device) as a tested device, so this is the step that answers
whether the whole project is worth relying on.

1. Connect the two PCs to the same LAN over **wired Ethernet** (treat Wi-Fi as unsupported --
   USB/IP has documented timing sensitivity, see `docs/ARCHITECTURE.md`).
2. On the **Wheel PC** (where the G29 is plugged in): install
   [usbipd-win](https://github.com/dorssel/usbipd-win/releases). Run `usbipd list` to find the
   G29's bus id, then `usbipd bind --busid=<busid>`.
3. On the **Game PC**: install [usbip-win2](https://github.com/vadimgrn/usbip-win2/releases).
   Run `usbip.exe list -r <wheel-pc-ip>` to confirm the G29 shows up, then
   `usbip.exe attach -r <wheel-pc-ip> -b <busid>`.
4. On the Game PC, confirm in Windows' game controller settings that the G29 enumerates
   correctly, the pedals and shifter register, and -- the highest-risk item -- **force feedback
   actually works** in a real game.
5. If input or FFB doesn't work acceptably, stop here and reassess before installing LogiSteam --
   the automation layer only adds convenience, it can't fix a broken transport.
6. Once done, undo the manual bind/attach (`usbip.exe detach -p <port>` on the Game PC) before
   moving on to the real install below, so LogiSteam starts from a clean state.

## One-time setup (per machine)

There is a single installer, `LogiSteam-Setup.exe` (built from `installer/LogiSteam.Setup/LogiSteam.iss`),
used on both machines. Run it and pick a role on its component-selection page:

- **"Wheel PC (Client)"** -- this PC has the G29 physically plugged in. Silently installs
  `usbipd-win` first (only if not already present), then installs `LogiSteam.WheelSide`.
- **"Game PC (Host)"** -- this PC runs Steam and the game. Silently installs `usbip-win2` first
  (only if not already present), then installs `LogiSteam.GameSide`.

The two roles are mutually exclusive (selecting one deselects the other) since a single machine
only ever plays one role; "Wheel PC" is the default selection. Steps:

1. Run `LogiSteam-Setup.exe` on each machine, choosing the matching role.
2. One UAC prompt is expected either way (the installer runs elevated, since both prerequisite
   drivers need Administrator rights to install regardless of who triggers it).
3. Confirm both PCs are on the same LAN subnet, wired connection recommended.
4. The wizard offers a "start automatically when Windows starts" checkbox, a "launch now"
   checkbox, and a "Create a desktop icon" checkbox (checked by default) for each role, so
   there's nothing left to configure by hand after it finishes.
5. The first time `LogiSteam.WheelSide` *runs*, it first tries to auto-detect a known wheel model
   from the currently-plugged-in devices -- see "Selecting your wheel" below. It only opens the
   manual device picker if nothing recognized is plugged in.
6. Once a device is selected (automatically or manually), `LogiSteam.WheelSide` also registers a
   one-time "AutoBind" policy for that device's hardware id(s) with `usbipd-win` -- if that wasn't
   already done as part of the install, expect one more brief UAC prompt at that point (a single
   prompt even for a multi-persona wheel like the G29, which registers all of its known personas
   in one batched elevation); it is not required on subsequent launches, since the policy persists.

## Selecting your wheel

`LogiSteam.WheelSide` no longer assumes a single fixed VID/PID for "the" G29 -- that assumption
was wrong twice in practice (a stale value from a third-party lookup site, then a real
per-unit/firmware difference), and live debugging showed why: the G29's PS3/PS4 mode switch and
native/generic mode make the *same physical wheel* enumerate as three different USB personas.
So a recognized wheel is now a small built-in catalog of known models, each backed by every
confirmed persona's hardware id (see `KnownWheelProfiles` in the source for the current list).

- **Automatic (default):** on first run, `LogiSteam.WheelSide` checks currently-plugged-in devices
  against that catalog and silently selects a match -- no dialog, no user action. The tray status
  then reads e.g. "Sharing Logitech G29 (bus 3-1)" using the model's stable name, regardless of
  which persona happens to be live underneath at any given moment.
- **Manual override:** if nothing in the catalog matches (an unrecognized wheel model, or any
  other USB device you want to share), right-click the tray icon and choose **"Select USB
  Device..."** to get a list of every USB device Windows currently sees, deduplicated so one
  physical device shows as one entry even if it has multiple internal USB interfaces. Likely-wheel
  candidates (Logitech vendor id, or a description mentioning "wheel"/"racing"/"driving force") are
  sorted to the top to make manual selection easier. Pick the one that's yours; the choice is
  remembered (`%LOCALAPPDATA%\LogiSteam\wheelside-device.json`) so you only do this once. If you
  don't see your device in the list, unplug/replug it and click **Refresh** -- and check Device
  Manager's "Hardware Ids" for that device (Details tab) if you want to confirm what LogiSteam
  should be matching. This override is always available from the tray menu, even after an
  automatic match, if you ever need to change the selection.

**Adding a new confirmed wheel model to the catalog** (maintainers): only add a profile once
you've directly observed its hardware id(s) on a real unit -- e.g. via Device Manager's "Hardware
Ids" (Details tab) or a log line from `UsbDeviceInventoryLogger` -- never from a third-party
lookup site (that's the exact mistake that motivated moving away from hardcoded ids in the first
place). Add a new `KnownWheelProfile` entry to `KnownWheelProfiles.All` in
`src/LogiSteam.Common/DeviceDetection/KnownWheelProfiles.cs`, listing every persona you've
confirmed for that model, then add a `KnownWheelProfilesTests.cs` regression guard the same way
the existing G29 entry is guarded.

## Diagnosing connection flakiness

`LogiSteam.WheelSide` continuously logs every USB device change (arrivals, departures, and moves
between bus locations) to `%LOCALAPPDATA%\LogiSteam\logs\usb-devices.log`, tagging Logitech-vendor
devices and whichever one is currently selected. Right-click the tray icon -> **"Open USB Device
Log"** to jump straight to it in Explorer. If the connection is bouncing, this log is the fastest
way to see whether the device is genuinely disconnecting/moving at the USB level (which points to
a cable/port/driver issue) versus something else -- this is the same kind of data used to track
down the G29's bind-triggered recalibration and `usbipd-win`'s stub-driver swap, now built in
instead of requiring a one-off diagnostic script each time.

Both apps also run an always-on, unauthenticated, read-only TCP diagnostics endpoint (WheelSide:
port 35896, GameSide: port 35897) that returns a live JSON snapshot of everything above plus the
current attach/discovery state -- reachable from anywhere on the LAN, no need to be sitting at
either machine. Query it with:

```powershell
.\tools\query-diagnostics.ps1 -TargetIp <the machine's IP> -Side WheelSide   # or -Side GameSide
```

This is what makes it possible to troubleshoot both machines' live state from a single terminal
session without relaying copy-pasted output back and forth. Note this endpoint has no
authentication by design (a deliberate tradeoff for a private home LAN) -- it only exposes
read-only status/device information, never anything sensitive, and there is no way to send
commands back through it.

Both apps also have a built-in **"Open Diagnostics..."** tray menu item, so most troubleshooting
no longer needs a terminal at all:

- **Connectivity** shows the same live JSON snapshot as the TCP endpoint above, auto-refreshing
  every 2 seconds, with "Refresh Now" and "Copy to Clipboard" buttons. On `LogiSteam.GameSide`,
  there's also a "Query Wheel PC" button (pre-filled with whichever Wheel PC it has last
  discovered) that fetches and displays that machine's live snapshot inline -- the in-app
  replacement for running `query-diagnostics.ps1` by hand.
- **Input Test** lets you confirm raw wheel input is actually flowing: click "Start Test", then
  move the wheel / press pedals and buttons -- each raw HID report received appears as a
  timestamped hex line. On `LogiSteam.WheelSide` it tests your currently-selected wheel; on
  `LogiSteam.GameSide` (which tracks the attached device by bus id/port rather than hardware id)
  pick the device from the dropdown first. If nothing appears while a game controller is
  connected and attached, that's a real problem worth investigating; a mouse or keyboard showing
  nothing is expected, since Windows exclusively locks their pointer/keystroke HID collections.

Building the installer yourself (only needed if you change the source): publish both apps with
`dotnet publish` (see main `README.md`), then compile `installer/LogiSteam.Setup/LogiSteam.iss`
with Inno Setup 6 (`ISCC.exe LogiSteam.iss`). The prerequisite installers are vendored, pinned
copies in `installer/vendor/` (`usbipd-win_5.3.0_x64.msi`, `USBip-0.9.7.8-x64.exe`) -- both get
embedded in the one combined installer regardless of which role is chosen at runtime (~125MB
total), since Inno Setup can't know the user's choice at compile time. Re-download and re-pin
them if you need a newer upstream version, and re-run the Phase 0 spike after doing so.

## Steady-state use (zero-config)

1. Plug in the wheel on the Wheel PC (if not already).
2. Launch both tray apps (optionally set both to run at login).
3. Tray icon colors mean the same thing on both apps: **gray** = idle/waiting, **amber** =
   in-progress or partially working, **green** = fully connected end-to-end, **red** = something's
   actually broken. Hover or check the (disabled) top menu item for the specific status text.
   - `LogiSteam.WheelSide` goes green once a Game PC has actually acked it back -- not just when
     it's broadcasting into the void, but when it's confirmed something is listening.
   - `LogiSteam.GameSide` goes green once it's attached the shared device.
4. No IP addresses or bus ids ever need to be typed in -- discovery is automatic on the LAN.
5. Start your Steam Remote Play/Steam Link session and play.
6. On exit, `LogiSteam.GameSide` detaches cleanly; use its tray menu's "Detach now" as a manual
   escape hatch if needed.

## Caveats

- No force feedback is guaranteed -- see Phase 0.
- A PC that also runs VirtualBox may see driver conflicts, since `usbipd-win` reuses VirtualBox's
  `VBoxUSB`/`VBoxUSBMon` drivers as its sharing mechanism.
- Both `usbipd-win` and `usbip-win2` are independently versioned upstream projects -- if you
  upgrade one, re-run the Phase 0 spike before trusting the pairing again.
