#define MyAppName "Internet Meter"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "Internet Meter"
#define MyAppExeName "InternetMeter.exe"
#define DeskbandClsid "{{40B96627-7938-4C3E-A932-0B0E79E50C92}"

[Setup]
AppId={{D3375BC2-69A9-4D8E-9E2A-082863A9DC8F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Internet Meter
DefaultGroupName=Internet Meter
PrivilegesRequired=admin
OutputDir=..\artifacts\installer
OutputBaseFilename=InternetMeter-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\InternetMeter\Assets\Brand\internet-meter.ico
WizardSmallImageFile=..\src\InternetMeter\Assets\Brand\internet-meter-128.png
VersionInfoVersion={#MyAppVersion}.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Never let Restart Manager close Explorer: the deskband DLL is loaded in the
; shell process, and a failed shell restart removes the desktop and taskbar.
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace

[Registry]
Root: HKLM64; Subkey: "Software\Classes\CLSID\{#DeskbandClsid}"; ValueType: string; ValueName: ""; ValueData: "Internet Meter"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Classes\CLSID\{#DeskbandClsid}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\InternetMeter.Deskband.dll"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Classes\CLSID\{#DeskbandClsid}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Classes\CLSID\{#DeskbandClsid}\Implemented Categories\{{00021492-0000-0000-C000-000000000046}"; ValueType: none; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"; ValueType: string; ValueName: "{#DeskbandClsid}"; ValueData: "Internet Meter"; Flags: uninsdeletevalue

[Icons]
Name: "{group}\Internet Meter"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Internet Meter"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-deskband"; Flags: runasoriginaluser runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Internet Meter"; Flags: runasoriginaluser nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-deskband"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterInternetMeterDeskband"

[Code]
function FindWindow(lpClassName, lpWindowName: String): HWND;
  external 'FindWindowW@user32.dll stdcall';

procedure DeinitializeSetup();
var
  ResultCode: Integer;
begin
  { Defensive recovery: even if another component closes Explorer while setup
    is running, never leave the user without a desktop and taskbar. }
  if FindWindow('Shell_TrayWnd', '') = 0 then
    Exec(ExpandConstant('{win}\explorer.exe'), '', '', SW_SHOWNORMAL,
      ewNoWait, ResultCode);
end;

