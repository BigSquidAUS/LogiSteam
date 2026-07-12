; Combined LogiSteam installer. Asks the user to pick a role for this machine -- "Wheel PC
; (Client)" or "Game PC (Host)" -- and only installs/bundles the prerequisite relevant to that
; role. The two roles are mutually exclusive (Inno Setup's "exclusive" component flag gives
; radio-button behavior), since a single machine only ever plays one role.
;
; usbipd-win (GPL-3.0) and usbip-win2 (BSD-2-Clause) are redistributed unmodified and invoked as
; separate processes -- neither is vendored/relinked into LogiSteam's own binary.
;
; Build with: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" LogiSteam.iss

#define MyAppName "LogiSteam"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "LogiSteam"
#define WheelExeName "LogiSteam.WheelSide.exe"
#define GameExeName "LogiSteam.GameSide.exe"
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
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Components]
Name: "wheelside"; Description: "Wheel PC (Client) - this PC has the G29 physically plugged in"; Flags: exclusive
Name: "gameside"; Description: "Game PC (Host) - this PC runs Steam and the game"; Flags: exclusive

[Files]
Source: "..\..\publish\WheelSide\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Components: wheelside
Source: "..\..\publish\GameSide\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Components: gameside
Source: "..\vendor\{#UsbipdWinInstaller}"; DestDir: "{tmp}"; Flags: dontcopy deleteafterinstall; Components: wheelside
Source: "..\vendor\{#UsbipWin2Installer}"; DestDir: "{tmp}"; Flags: dontcopy deleteafterinstall; Components: gameside

[Tasks]
Name: "startupshortcut"; Description: "Start LogiSteam automatically when Windows starts"; GroupDescription: "Startup:"
Name: "launchafterinstall"; Description: "Launch LogiSteam now"; GroupDescription: "Additional tasks:"; Flags: unchecked

[Icons]
Name: "{group}\LogiSteam Wheel Side"; Filename: "{app}\{#WheelExeName}"; Components: wheelside
Name: "{group}\LogiSteam Game Side"; Filename: "{app}\{#GameExeName}"; Components: gameside
Name: "{group}\Uninstall LogiSteam"; Filename: "{uninstallexe}"
Name: "{userstartup}\LogiSteam Wheel Side"; Filename: "{app}\{#WheelExeName}"; Tasks: startupshortcut; Components: wheelside
Name: "{userstartup}\LogiSteam Game Side"; Filename: "{app}\{#GameExeName}"; Tasks: startupshortcut; Components: gameside

[Run]
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\{#UsbipdWinInstaller}"" /quiet /norestart"; \
    StatusMsg: "Installing usbipd-win prerequisite (this shares the wheel over the LAN)..."; \
    Components: wheelside; Check: NeedsUsbipdWin; Flags: waituntilterminated
Filename: "{tmp}\{#UsbipWin2Installer}"; Parameters: "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"; \
    StatusMsg: "Installing usbip-win2 prerequisite (this attaches the shared wheel)..."; \
    Components: gameside; Check: NeedsUsbipWin2; Flags: waituntilterminated
Filename: "{app}\{#WheelExeName}"; Description: "Launch LogiSteam Wheel Side"; \
    Flags: nowait postinstall skipifsilent unchecked; Tasks: launchafterinstall; Components: wheelside
Filename: "{app}\{#GameExeName}"; Description: "Launch LogiSteam Game Side"; \
    Flags: nowait postinstall skipifsilent unchecked; Tasks: launchafterinstall; Components: gameside
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""LogiSteam WheelSide (UDP 35894 in)"" dir=in action=allow protocol=UDP localport=35894"; \
    Components: wheelside; Flags: runhidden waituntilterminated; StatusMsg: "Adding firewall rule for LogiSteam..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""LogiSteam GameSide (UDP 35893 in)"" dir=in action=allow protocol=UDP localport=35893"; \
    Components: gameside; Flags: runhidden waituntilterminated; StatusMsg: "Adding firewall rule for LogiSteam..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""LogiSteam WheelSide Diagnostics (TCP 35896 in)"" dir=in action=allow protocol=TCP localport=35896"; \
    Components: wheelside; Flags: runhidden waituntilterminated; StatusMsg: "Adding firewall rule for LogiSteam remote diagnostics..."
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""LogiSteam GameSide Diagnostics (TCP 35897 in)"" dir=in action=allow protocol=TCP localport=35897"; \
    Components: gameside; Flags: runhidden waituntilterminated; StatusMsg: "Adding firewall rule for LogiSteam remote diagnostics..."

[UninstallRun]
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam WheelSide (UDP 35894 in)"""; \
    Components: wheelside; Flags: runhidden waituntilterminated; RunOnceId: "RemoveWheelSideFirewallRule"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam GameSide (UDP 35893 in)"""; \
    Components: gameside; Flags: runhidden waituntilterminated; RunOnceId: "RemoveGameSideFirewallRule"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam WheelSide Diagnostics (TCP 35896 in)"""; \
    Components: wheelside; Flags: runhidden waituntilterminated; RunOnceId: "RemoveWheelSideDiagFirewallRule"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""LogiSteam GameSide Diagnostics (TCP 35897 in)"""; \
    Components: gameside; Flags: runhidden waituntilterminated; RunOnceId: "RemoveGameSideDiagFirewallRule"

[Code]
procedure InitializeWizard;
begin
  // Without a [Types] section, Inno Setup checks all [Components] by default, which would
  // start both exclusive siblings checked. Force a single sane default (Wheel PC) instead --
  // but ONLY if no explicit /COMPONENTS= override was passed on the command line. This ran
  // unconditionally before, which silently clobbered scripted/unattended installs passing
  // /COMPONENTS=gameside: InitializeWizard runs after Inno Setup applies that override, so the
  // hardcoded Checked[] assignment was resetting the selection back to "wheelside" every time,
  // regardless of what was actually requested.
  if ExpandConstant('{param:COMPONENTS|}') = '' then
  begin
    WizardForm.ComponentsList.Checked[0] := True;
    WizardForm.ComponentsList.Checked[1] := False;
  end;
end;

function NeedsUsbipdWin: Boolean;
begin
  // Guard on the component selection ourselves (rather than relying only on the [Files]/[Run]
  // Components: filter) so ExtractTemporaryFile is never attempted for a file that wasn't
  // extracted for this session's component selection.
  Result := WizardIsComponentSelected('wheelside')
    and not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\usbipd');
  if Result then
  begin
    ExtractTemporaryFile('{#UsbipdWinInstaller}');
  end;
end;

function NeedsUsbipWin2: Boolean;
begin
  Result := WizardIsComponentSelected('gameside')
    and not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\usbip2_ude');
  if Result then
  begin
    ExtractTemporaryFile('{#UsbipWin2Installer}');
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectComponents then
  begin
    if not (WizardIsComponentSelected('wheelside') or WizardIsComponentSelected('gameside')) then
    begin
      MsgBox('Please choose either "Wheel PC (Client)" or "Game PC (Host)" before continuing.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;
