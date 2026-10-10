; LogiSteam installer. One app for both PCs: the same LogiSteam.exe runs on the Wheel PC and on
; the Game PC, and the user picks which one a PC is with the switch in the app (it can be changed
; at any time). Because either role can be chosen later, both USB/IP prerequisites are installed.
;
; usbipd-win (GPL-3.0) and usbip-win2 (BSD-2-Clause) are redistributed unmodified and invoked as
; separate processes -- neither is vendored/relinked into LogiSteam's own binary.
;
; Build with:
;   dotnet publish src\LogiSteam.App -c Release -r win-x64 --self-contained true -o publish\LogiSteam
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\LogiSteam.Setup\LogiSteam.iss

#define MyAppName "LogiSteam"
#define MyAppVersion "2.1.0"
#define MyAppPublisher "LogiSteam"
#define AppExeName "LogiSteam.exe"
#define UsbipdWinInstaller "usbipd-win_5.3.0_x64.msi"
#define UsbipWin2Installer "USBip-0.9.7.8-x64.exe"

[Setup]
AppId={{6A1F2E3D-4C5B-4A6E-8D7F-9A0B1C2D3E4F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\LogiSteam
DefaultGroupName=LogiSteam
DisableProgramGroupPage=yes
OutputDir=..\..\publish\installers
OutputBaseFilename=LogiSteam-Setup
SetupIconFile=..\..\src\LogiSteam.App\Assets\LogiSteam.ico
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Files]
Source: "..\..\publish\LogiSteam\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "..\vendor\{#UsbipdWinInstaller}"; DestDir: "{tmp}"; Flags: dontcopy deleteafterinstall
Source: "..\vendor\{#UsbipWin2Installer}"; DestDir: "{tmp}"; Flags: dontcopy deleteafterinstall
Source: "..\..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\licenses\*"; DestDir: "{app}\licenses"; Flags: recursesubdirs ignoreversion

; Leftovers from 1.x, which shipped a separate app per role.
[InstallDelete]
Type: files; Name: "{app}\LogiSteam.WheelSide.*"
Type: files; Name: "{app}\LogiSteam.GameSide.*"
Type: files; Name: "{group}\LogiSteam Wheel Side.lnk"
Type: files; Name: "{group}\LogiSteam Game Side.lnk"
Type: files; Name: "{userstartup}\LogiSteam Wheel Side.lnk"
Type: files; Name: "{userstartup}\LogiSteam Game Side.lnk"
Type: files; Name: "{autodesktop}\LogiSteam Wheel Side.lnk"
Type: files; Name: "{autodesktop}\LogiSteam Game Side.lnk"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Additional icons:"
Name: "startupshortcut"; Description: "Start LogiSteam automatically when Windows starts"; GroupDescription: "Startup:"

[Icons]
Name: "{group}\LogiSteam"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall LogiSteam"; Filename: "{uninstallexe}"
; --minimized: start quietly in the tray at sign-in instead of opening the window every boot.
Name: "{userstartup}\LogiSteam"; Filename: "{app}\{#AppExeName}"; Parameters: "--minimized"; Tasks: startupshortcut
Name: "{autodesktop}\LogiSteam"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\{#UsbipdWinInstaller}"" /quiet /norestart"; \
    StatusMsg: "Installing usbipd-win (shares the wheel from the Wheel PC)..."; \
    Check: NeedsUsbipdWin; Flags: waituntilterminated
Filename: "{tmp}\{#UsbipWin2Installer}"; Parameters: "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"; \
    StatusMsg: "Installing usbip-win2 (connects the wheel on the Game PC)..."; \
    Check: NeedsUsbipWin2; Flags: waituntilterminated
; Firewall: remove any earlier copy of each rule first so reinstalling never stacks duplicates.
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam WheelSide (UDP 35894 in)"""; Flags: runhidden waituntilterminated
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam GameSide (UDP 35893 in)"""; Flags: runhidden waituntilterminated
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam WheelSide Diagnostics (TCP 35896 in)"""; Flags: runhidden waituntilterminated
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam GameSide Diagnostics (TCP 35897 in)"""; Flags: runhidden waituntilterminated
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""LogiSteam WheelSide (UDP 35894 in)"" dir=in action=allow protocol=UDP localport=35894"; \
    Flags: runhidden waituntilterminated; StatusMsg: "Adding firewall rules for LogiSteam..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""LogiSteam GameSide (UDP 35893 in)"" dir=in action=allow protocol=UDP localport=35893"; \
    Flags: runhidden waituntilterminated; StatusMsg: "Adding firewall rules for LogiSteam..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""LogiSteam WheelSide Diagnostics (TCP 35896 in)"" dir=in action=allow protocol=TCP localport=35896"; \
    Flags: runhidden waituntilterminated; StatusMsg: "Adding firewall rules for LogiSteam..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""LogiSteam GameSide Diagnostics (TCP 35897 in)"" dir=in action=allow protocol=TCP localport=35897"; \
    Flags: runhidden waituntilterminated; StatusMsg: "Adding firewall rules for LogiSteam..."
Filename: "{app}\{#AppExeName}"; Description: "Launch LogiSteam"; Flags: nowait postinstall skipifsilent
; An in-app update runs this installer with /SILENT, where the checkbox above never shows:
; start the new version again, as the signed-in user rather than as Administrator.
Filename: "{app}\{#AppExeName}"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#AppExeName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopLogiSteam"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam WheelSide (UDP 35894 in)"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "RemoveWheelSideFirewallRule"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam GameSide (UDP 35893 in)"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "RemoveGameSideFirewallRule"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam WheelSide Diagnostics (TCP 35896 in)"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "RemoveWheelSideDiagFirewallRule"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam GameSide Diagnostics (TCP 35897 in)"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "RemoveGameSideDiagFirewallRule"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // LogiSteam lives in the tray, so a running copy (this version or a 1.x per-role app) would
  // hold its files open. Stop it before copying; the user relaunches from the final page.
  Exec('taskkill.exe', '/F /IM LogiSteam.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM LogiSteam.WheelSide.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM LogiSteam.GameSide.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

function NeedsUsbipdWin: Boolean;
begin
  Result := not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\usbipd');
  if Result then
  begin
    ExtractTemporaryFile('{#UsbipdWinInstaller}');
  end;
end;

function NeedsUsbipWin2: Boolean;
begin
  Result := not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\usbip2_ude');
  if Result then
  begin
    ExtractTemporaryFile('{#UsbipWin2Installer}');
  end;
end;
