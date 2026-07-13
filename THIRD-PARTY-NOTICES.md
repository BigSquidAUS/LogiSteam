# Third-Party Notices

LogiSteam's own source code is proprietary (see `LICENSE`). This installer bundles two
independent, unmodified third-party tools that LogiSteam.WheelSide and LogiSteam.GameSide invoke
as external processes (never linked or relinked into LogiSteam's own binaries). Both are
redistributed here under their original license terms, reproduced below.

## usbipd-win (GPL-3.0-only)

- Project: https://github.com/dorssel/usbipd-win
- Copyright (C) 2020 Frans van Dorsselaer
- Vendored version: 5.3.0 (`installer/vendor/usbipd-win_5.3.0_x64.msi`), unmodified upstream binary
- Full license text: [`licenses/GPL-3.0-usbipd-win.txt`](licenses/GPL-3.0-usbipd-win.txt)
- Source code: the exact source corresponding to this vendored build is publicly available at the
  project URL above, tag `v5.3.0`. LogiSteam makes no modifications to it.

LogiSteam.WheelSide only invokes the installed `usbipd.exe` as a separate command-line process
(see `src/LogiSteam.Common/UsbipInterop/UsbipdCliWrapper.cs`) -- it is not statically or
dynamically linked into any LogiSteam binary. Redistributing this GPL-3.0 component alongside
LogiSteam (rather than requiring a separate download) still constitutes distribution under the
GPL, which is why its full license text and source pointer are included here as required by
GPL-3.0 section 6.

## usbip-win2 (BSD-2-Clause)

- Project: https://github.com/vadimgrn/usbip-win2
- Copyright (c) 2021-2026, Vadym Hrynchyshyn <vadimgrn@gmail.com>
- Vendored version: 0.9.7.8 (`installer/vendor/USBip-0.9.7.8-x64.exe`), unmodified upstream binary
- Full license text: [`licenses/BSD-2-Clause-usbip-win2.txt`](licenses/BSD-2-Clause-usbip-win2.txt)

LogiSteam.GameSide only invokes the installed `usbip.exe` as a separate command-line process (see
`src/LogiSteam.Common/UsbipInterop/UsbipCliWrapper.cs`) -- it is not statically or dynamically
linked into any LogiSteam binary.

## NuGet package dependencies

LogiSteam.Common also references the following NuGet packages at build time (not modified;
compiled into LogiSteam's own binaries as ordinary library dependencies, not separately vendored
as installers the way usbipd-win/usbip-win2 are):

- `HidSharp` 2.6.4 (Apache License 2.0) -- Copyright 2010-2025 James F. Bellinger,
  https://www.nuget.org/packages/HidSharp -- full text:
  [`licenses/Apache-2.0-HidSharp.txt`](licenses/Apache-2.0-HidSharp.txt)
- `System.Management` (MIT License) -- Copyright (c) .NET Foundation and Contributors, part of
  the .NET runtime libraries, https://www.nuget.org/packages/System.Management
