; ZMK Companion — Inno Setup 6 installer script
; Requires: Inno Setup 6.3+ (https://jrsoftware.org/isinfo.php)
; Build: ISCC.exe ZmkCompanion.iss   (or run build.ps1)

#define AppName    "ZMK Companion"
#define AppVersion "1.0.0"
#define AppExe     "ZmkCompanion.exe"
#define PublishDir "..\app\ZmkCompanion\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={{8F3A2B1C-4D5E-6F70-A891-BC23DE456F78}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=oscampo
AppPublisherURL=https://github.com/oscampo/zmk-companion
AppSupportURL=https://github.com/oscampo/zmk-companion/issues
AppUpdatesURL=https://github.com/oscampo/zmk-companion/releases

; Install per-user, no UAC prompt
PrivilegesRequired=lowest
DefaultDirName={localappdata}\ZmkCompanion
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

OutputDir=output
OutputBaseFilename=ZmkCompanion-Setup-{#AppVersion}
UninstallDisplayIcon={app}\{#AppExe}

Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=100

; x64 only — matches the publish RID
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Minimum Windows 10 2004 (build 19041) for WinRT BLE APIs
MinVersion=10.0.19041

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup";    Description: "Start {#AppName} automatically with Windows"; \
                    GroupDescription: "On login:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; \
                    GroupDescription: "Extras:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";          Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}";   Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; HKCU Run key for auto-start — removed cleanly on uninstall
Root: HKCU; \
  Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; \
  ValueName: "{#AppName}"; \
  ValueData: """{app}\{#AppExe}"""; \
  Flags: uninsdeletevalue; \
  Tasks: startup

[Run]
; Kill any running instance before files are replaced (upgrade scenario)
Filename: "taskkill"; Parameters: "/f /im {#AppExe}"; \
  Flags: runhidden skipifdoesntexist; BeforeInstall: True

; Launch after install (user can uncheck the checkbox)
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName} now"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the running app before uninstall removes its files
Filename: "taskkill"; Parameters: "/f /im {#AppExe}"; Flags: runhidden

[Code]
// Remove leftover settings directory on uninstall only if the user confirms.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{%APPDATA}\ZmkCompanion');
    if DirExists(SettingsDir) then
    begin
      if MsgBox('Remove saved settings (city, NFL team, pomodoro preset)?',
                mbConfirmation, MB_YESNO) = IDYES then
        DelTree(SettingsDir, True, True, True);
    end;
  end;
end;
