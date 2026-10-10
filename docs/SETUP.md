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
and a single app, `LogiSteam.exe`, used on both machines.

1. Run `LogiSteam-Setup.exe` on each PC. One UAC prompt is expected. It installs LogiSteam and,
   if they aren't already present, both prerequisites (`usbipd-win` and `usbip-win2`) -- both,
   because either role can be chosen later in the app.
2. Confirm both PCs are on the same LAN subnet, wired connection recommended.
3. The wizard offers "Start LogiSteam automatically when Windows starts" (it then starts
   quietly in the tray) and "Create a desktop icon".
4. The first time LogiSteam opens it asks **"Which PC is this?"** -- choose **Wheel PC** on the
   machine the wheel is plugged into and **Game PC** on the machine that runs Steam and the
   game. If a racing wheel is already plugged in, the Wheel PC choice says so. The choice can be
   changed at any time with the switch at the top of the window.
5. On the Wheel PC, the first time the wheel is found LogiSteam registers a one-time "AutoBind"
   policy for it with `usbipd-win`, which needs Administrator rights: expect one UAC prompt. If
   it is declined, the window shows "One-time permission needed" with an **Allow** button to
   try again -- it is never re-prompted on a timer.
6. Open **Logitech G HUB** on the Game PC. LogiSteam warns in its window and from the tray
   icon, on both PCs, whenever G HUB isn't running there. G HUB is not required on the Wheel PC
   by default; if the wheel misbehaves without it, turn on **Advanced > G HUB on the Wheel PC:
   required** on both PCs and they will check and warn about the Wheel PC's G HUB as well.

## How the wheel is identified

Nobody is asked to pick a device. On the Wheel PC, LogiSteam looks at the USB devices plugged in
and identifies the racing wheel itself:

1. **By USB id**, against a built-in catalog of Logitech wheels (`KnownWheelProfiles` in the
   source): G29, G920, G923, G27, G25, Driving Force GT / Pro / EX, MOMO. A model that changes
   USB id with its mode switch lists every id it can report (the G29's PS3 and PS4 modes).
2. **By USB product name**, for a Logitech wheel the catalog doesn't list: a Logitech device
   whose product name reads like a wheel ("... Racing Wheel", "Driving Force ...") is accepted.
   Mice, keyboards, joysticks and wireless receivers are not.

The window then shows the wheel by its model name ("Logitech G29") regardless of which mode it
happens to be in.

**If your wheel isn't found:** open **Advanced > Choose the wheel manually**. That lists the USB
devices on the PC (likely wheels first) so you can pick it by hand. A manual choice is never
overridden by automatic detection; **Advanced > Go back to automatic detection** undoes it.

**Adding a wheel model to the catalog** (maintainers): only add an id that is confirmed --
observed on a real unit (Device Manager's "Hardware Ids", or a `usb-devices.log` line) or
listed in the Linux kernel's Logitech wheel drivers (`drivers/hid/hid-ids.h`). Never use a
VID/PID lookup site, and note that `046D:C52B` is Logitech's Unifying receiver, not a wheel: an
early version listed it as a G29 mode, which could make LogiSteam share a mouse dongle instead
of the wheel. Add the profile to `KnownWheelProfiles.All` and a case to
`KnownWheelProfilesTests.cs`.

## Diagnosing connection flakiness

On the Wheel PC, LogiSteam continuously logs every USB device change (arrivals, departures, and moves
between bus locations) to `%LOCALAPPDATA%\LogiSteam\logs\usb-devices.log`, tagging Logitech-vendor
devices and whichever one is currently selected. **Advanced > USB device log** jumps straight
to it in Explorer. If the connection is bouncing, this log is the fastest
way to see whether the device is genuinely disconnecting/moving at the USB level (which points to
a cable/port/driver issue) versus something else -- this is the same kind of data used to track
down the G29's bind-triggered recalibration and `usbipd-win`'s stub-driver swap, now built in
instead of requiring a one-off diagnostic script each time.

LogiSteam also runs an always-on, unauthenticated, read-only TCP diagnostics endpoint (port 35896
while it is the Wheel PC, 35897 while it is the Game PC) that returns a live JSON snapshot of everything above plus the
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

There is also a built-in **Advanced > Diagnostics** window in both roles, so most troubleshooting
no longer needs a terminal at all:

- **Connectivity** shows the same live JSON snapshot as the TCP endpoint above, auto-refreshing
  every 2 seconds, with "Refresh Now" and "Copy to Clipboard" buttons. On the Game PC,
  there's also a "Query Wheel PC" button (pre-filled with whichever Wheel PC it has last
  discovered) that fetches and displays that machine's live snapshot inline -- the in-app
  replacement for running `query-diagnostics.ps1` by hand.
- **Input Test** lets you confirm raw wheel input is actually flowing: click "Start Test", then
  move the wheel / press pedals and buttons -- each raw HID report received appears as a
  timestamped hex line. On the Wheel PC it tests the identified wheel; on
  the Game PC (which tracks the attached device by bus id/port rather than hardware id)
  pick the device from the dropdown first. If nothing appears while a game controller is
  connected and attached, that's a real problem worth investigating; a mouse or keyboard showing
  nothing is expected, since Windows exclusively locks their pointer/keystroke HID collections.

Building the installer yourself (only needed if you change the source): publish the app with
`dotnet publish` (see main `README.md`), then compile `installer/LogiSteam.Setup/LogiSteam.iss`
with Inno Setup 6 (`ISCC.exe LogiSteam.iss`). The prerequisite installers are vendored, pinned
copies in `installer/vendor/` (`usbipd-win_5.3.0_x64.msi`, `USBip-0.9.7.8-x64.exe`) -- both are
embedded in the installer (about 77MB in total), since the role is chosen in the app, after
installation. Re-download and re-pin
them if you need a newer upstream version, and re-run the Phase 0 spike after doing so.

## Steady-state use (zero-config)

1. Plug in the wheel on the Wheel PC (if not already) and open G HUB on the Game PC.
2. Start LogiSteam on both PCs (or let it start with Windows).
3. The window shows one status line and a checklist. The colours mean the same thing in the
   window and on the tray icon: **blue** = working on it (searching, connecting), **green** =
   connected end to end, **amber** = needs your attention (G HUB not running, permission
   needed), **red** = something is broken.
   - On the Wheel PC it goes green once a Game PC has attached the wheel.
   - On the Game PC it goes green once the wheel is attached locally.
4. No IP addresses or bus ids ever need to be typed in -- discovery is automatic on the LAN.
5. Start your Steam Remote Play/Steam Link session and play.
6. Closing the window leaves LogiSteam running in the tray so the wheel stays connected; click
   the tray icon to reopen it, right-click > Exit to quit. On exit (or when switched to Wheel
   PC) the Game PC detaches the wheel cleanly. **Advanced > Reconnect the wheel** detaches and
   re-attaches if it ever stops responding.

## Caveats

- No force feedback is guaranteed -- see Phase 0.
- A PC that also runs VirtualBox may see driver conflicts, since `usbipd-win` reuses VirtualBox's
  `VBoxUSB`/`VBoxUSBMon` drivers as its sharing mechanism.
- Both `usbipd-win` and `usbip-win2` are independently versioned upstream projects -- if you
  upgrade one, re-run the Phase 0 spike before trusting the pairing again.
