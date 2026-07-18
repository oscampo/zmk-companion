; ZMK Companion — Inno Setup 6 installer script
; Requires: Inno Setup 6.3+ (https://jrsoftware.org/isinfo.php)
; Build: ISCC.exe ZmkCompanion.iss   (or run build.ps1)

#define AppName      "ZMK Companion"
; AppVersion is normally injected by CI via "ISCC /DAppVersion=X.Y.Z ..." for
; tagged releases (see .github/workflows/build.yml), driven by the git tag,
; not this file. The #ifndef keeps a sane default for local/manual builds
; (plain "ISCC ZmkCompanion.iss" with no /D) without erroring on redefinition.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe       "ZmkCompanion.exe"
#define CliExe       "zkc.exe"
#define PublishDir   "..\app\ZmkCompanion\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#define CliPublishDir "..\app\ZmkCompanionCli\bin\Release\net8.0-windows\win-x64\publish"

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
Source: "{#PublishDir}\{#AppExe}";    DestDir: "{app}"; Flags: ignoreversion
Source: "{#CliPublishDir}\{#CliExe}"; DestDir: "{app}"; Flags: ignoreversion

; Demo content for the "presentacion en sociedad" build: a ready-to-cycle
; Canvas (clock/weather/API ticker/etc.) so a fresh install already shows
; something instead of a single blank clock page.
;
; api_ticker.ps1 ships in {app}\scripts, the recommended home for every
; Auto-start script (not just this one): AutoStartManager launches commands
; via cmd.exe with no explicit WorkingDirectory, so it inherits
; ZmkCompanion.exe's own cwd, {app}, and settings.json's AutoStartEntries
; command ("powershell -File scripts\api_ticker.ps1 | zkc --watch") resolves
; that relative path from there. Keeping user scripts in their own
; subdirectory (not loose in {app} next to the two exes) is also what makes
; "export my settings" tractable later: it can just zip this one folder
; instead of trying to guess which arbitrary path in a free-form Command
; string is "a file to bundle".
Source: "demo\api_ticker.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

; settings.json/library\demo.json go to {userappdata}\ZmkCompanion, matching
; AppSettings' own storage path (Environment.SpecialFolder.ApplicationData)
; and CellGridEditorForm's LibraryDir. onlyifdoesntexist on both: only a
; genuinely fresh install (no prior settings.json at all) gets seeded, an
; upgrade or reinstall never overwrites a real user's own configuration or
; library presets.
Source: "demo\settings.json"; DestDir: "{userappdata}\ZmkCompanion"; \
  Flags: onlyifdoesntexist
Source: "demo\demo.json"; DestDir: "{userappdata}\ZmkCompanion\library"; \
  Flags: onlyifdoesntexist

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

; Add install dir to user PATH so "zkc" works from any terminal
Root: HKCU; \
  Subkey: "Environment"; \
  ValueType: expandsz; \
  ValueName: "Path"; \
  ValueData: "{olddata};{app}"; \
  Flags: uninsdeletevalue

[Run]
; Launch after install (user can uncheck the checkbox)
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName} now"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the running app before uninstall removes its files
Filename: "taskkill"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "KillTray"

[Code]
// Returns true if any .NET 8 Desktop Runtime x64 is installed.
// Primary: dotnet CLI (most reliable). Fallback: registry.
function IsDotNet8DesktopInstalled: Boolean;
var
  TempFile: String;
  Lines: TArrayOfString;
  Names: TArrayOfString;
  ResultCode, I: Integer;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\zmk-dotnet-runtimes.txt');
  if Exec(ExpandConstant('{sys}\cmd.exe'),
          '/C dotnet --list-runtimes > "' + TempFile + '" 2>&1',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    if LoadStringsFromFile(TempFile, Lines) then
      for I := 0 to High(Lines) do
        if Pos('Microsoft.WindowsDesktop.App 8.', Lines[I]) = 1 then
        begin
          Result := True;
          Exit;
        end;
  if RegGetValueNames(HKLM,
      'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
      Names) then
    for I := 0 to High(Names) do
      if (Length(Names[I]) >= 2) and (Copy(Names[I], 1, 2) = '8.') then
      begin
        Result := True;
        Exit;
      end;
end;

function InitializeSetup: Boolean;
var
  TempFile, PSCmd: String;
  ResultCode: Integer;
begin
  Result := True;
  if IsDotNet8DesktopInstalled() then
    Exit;
  if MsgBox(
    '.NET 8 Desktop Runtime (x64) is required but not installed.' + #13#10 +
    'Click Yes to download and install it now (~55 MB).',
    mbConfirmation, MB_YESNO) = IDNO then
  begin
    Result := False;
    Exit;
  end;
  TempFile := ExpandConstant('{tmp}\dotnet8-desktop-runtime.exe');
  PSCmd := '-NoProfile -NonInteractive -Command "Invoke-WebRequest' +
           ' -Uri ''https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe''' +
           ' -OutFile ''' + TempFile + ''' -UseBasicParsing"';
  if not Exec('powershell.exe', PSCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or
     (ResultCode <> 0) then
  begin
    MsgBox('Download failed. Please install .NET 8 Desktop Runtime manually from https://dot.net',
           mbError, MB_OK);
    Result := False;
    Exit;
  end;
  Exec(TempFile, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then
  begin
    MsgBox('.NET 8 Runtime installation failed (code ' + IntToStr(ResultCode) + ').',
           mbError, MB_OK);
    Result := False;
  end;
end;

// Kill any running instance before files are replaced (upgrade scenario).
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AppExe}',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // zkc.exe (CLI) is a short-lived process; no need to kill it separately.
end;

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
