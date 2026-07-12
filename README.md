# LogiSteam

Makes a Logitech G29 racing wheel work over Steam Remote Play / Steam Link, by tunneling the
wheel's real USB traffic across the LAN instead of relying on Steam's own controller-forwarding
(which only reliably carries simple XInput/gamepad signals, not a wheel's bidirectional HID
traffic including force feedback).

## How it works

- **Wheel PC** -- the machine the G29 is physically plugged into (the Steam Remote Play/Steam
  Link streaming client, i.e. where you sit).
- **Game PC** -- the machine that runs Steam and the actual game (the streaming host).

LogiSteam is two small, zero-config tray apps built on top of two existing open-source USB/IP
projects for Windows:

| Machine | LogiSteam app | Upstream dependency | Role |
|---|---|---|---|
| Wheel PC | `LogiSteam.WheelSide` | [usbipd-win](https://github.com/dorssel/usbipd-win) | Shares the G29 over the LAN |
| Game PC | `LogiSteam.GameSide` | [usbip-win2](https://github.com/vadimgrn/usbip-win2) | Attaches the shared G29 as if it were local |

Once both are running, the Game PC's Windows install believes a real G29 is plugged in locally
-- full steering resolution, all buttons, and (pending validation, see below) real force
feedback -- because the wheel's actual USB traffic is tunneled over the network rather than
translated into a simplified gamepad signal.

`LogiSteam.WheelSide` doesn't assume a fixed VID/PID for "the" G29 -- that assumption has been
wrong twice already (a stale third-party lookup value, then real per-unit/firmware variance).
Instead it lists currently-plugged-in USB devices and lets you pick yours (right-click the tray
icon -> "Select USB Device..."), remembering the choice from then on. Both tray icons also show a
live connection state (idle/warning/connected/error, both by icon color and status text) so you
can tell at a glance whether the Wheel PC and Game PC actually see each other, not just that each
one is running.

**Read [`docs/SETUP.md`](docs/SETUP.md) first**, and in particular run the **Phase 0 manual
validation spike** described there *before* relying on this for real use -- neither upstream
project lists a force-feedback wheel as a tested device, so whether FFB actually works over this
transport is an open question this project has not yet answered.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for how the two tray apps fit together.

## Project layout

```
src/LogiSteam.Common/       Shared library: beacon/ack discovery, usbipd.exe/usbip.exe wrappers,
                            USB device enumeration, selected-device persistence, tray icon/status
src/LogiSteam.WheelSide/    Tray app for the Wheel PC (includes the device picker dialog)
src/LogiSteam.GameSide/     Tray app for the Game PC
tests/LogiSteam.Common.Tests/  Unit tests for the parsers and discovery protocol
```

## Building

```
dotnet build LogiSteam.sln
```

## Installing

One installer, `installer/LogiSteam.Setup/LogiSteam.iss`, works for both machines -- it asks
which role this PC plays ("Wheel PC (Client)" or "Game PC (Host)", mutually exclusive) and only
bundles/runs the prerequisite (`usbipd-win` or `usbip-win2`) relevant to that choice.

To build it: publish both apps (`dotnet publish src\LogiSteam.WheelSide -c Release -r win-x64
--self-contained true -p:PublishSingleFile=true -o publish\WheelSide`, same for `GameSide`), then
compile the script with Inno Setup 6 (`ISCC.exe installer\LogiSteam.Setup\LogiSteam.iss`). Output
(`LogiSteam-Setup.exe`) lands in `publish\installers\`. See [`docs/SETUP.md`](docs/SETUP.md) for
the full install flow and the Phase 0 validation spike.

## Known limitations (v1)

- Force feedback over the network is unverified for this device class -- see the Phase 0 spike.
- Wired LAN between the two PCs is required; Wi-Fi latency/jitter is not supported.
- Discovery relies on UDP broadcast, so both PCs must be on the same LAN subnet.
- `usbipd-win` is GPL-3.0 licensed; LogiSteam invokes its installed CLI as an external process
  rather than bundling/relinking it (see `docs/ARCHITECTURE.md`).
- Both apps run an always-on, unauthenticated, read-only TCP diagnostics endpoint (see
  "Diagnosing connection flakiness" in `docs/SETUP.md`) -- a deliberate, user-confirmed tradeoff
  for easy LAN troubleshooting, not something to enable on an untrusted network.
