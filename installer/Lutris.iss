; Inno Setup script for Lutris.
; Build it with build-installer.ps1 in the project root, which publishes the app first and
; passes AppVersion / PublishDir / OutputDir (and optionally BundleRuntime) as defines.

#define MyAppName "Lutris"
#define MyAppPublisher "Sandip Shakya"
#define MyAppExeName "Lutris.exe"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
; The Windows App SDK runtime the app needs on the target PC.
#define RuntimeInstaller "WindowsAppRuntimeInstall-x64.exe"
#define RuntimeUrl "https://aka.ms/windowsappsdk/2.5/latest/windowsappruntimeinstall-x64.exe"

[Setup]
AppId={{6b753c19-3993-41ec-af13-98c7ccbc98fa}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Installs per user by default (no admin prompt); the wizard offers an all-users install too.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename={#MyAppName}-Windows-{#AppVersion}-Setup
SetupIconFile=..\Assets\Lutris.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef BundleRuntime
; Embedded runtime installer: only extracted when the target PC lacks the runtime.
Source: "redist\{#RuntimeInstaller}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsRuntime
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Whether bundled or downloaded during setup, the runtime installer sits in {tmp} by now.
Filename: "{tmp}\{#RuntimeInstaller}"; Parameters: "--quiet"; StatusMsg: "Installing the Windows App Runtime..."; Check: NeedsRuntime; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;
  RuntimeChecked: Boolean;
  RuntimeMissing: Boolean;

// True when no Windows App Runtime 2.5 or later is registered for the current user.
function NeedsRuntime: Boolean;
var
  ResultCode: Integer;
begin
  if not RuntimeChecked then
  begin
    RuntimeChecked := True;
    RuntimeMissing := True;
    if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "if (Get-AppxPackage -Name Microsoft.WindowsAppRuntime.2 | Where-Object { [version]$_.Version -ge [version]''2.5.0.0'' }) { exit 0 } else { exit 1 }"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RuntimeMissing := (ResultCode <> 0);
    if RuntimeMissing then
      Log('Windows App Runtime 2.5 not found; it will be installed.')
    else
      Log('Windows App Runtime 2.5 is already installed.');
  end;
  Result := RuntimeMissing;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Downloaded %s (%d bytes)', [FileName, ProgressMax]));
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

#ifndef BundleRuntime
// Interactive installs: fetch the runtime installer with a progress page before copying files.
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and NeedsRuntime then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#RuntimeUrl}', '{#RuntimeInstaller}', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        if DownloadPage.AbortedByUser then
          Log('Runtime download aborted by user.')
        else
          SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

// Silent installs never show the wizard pages, so download here instead.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if NeedsRuntime and not FileExists(ExpandConstant('{tmp}\{#RuntimeInstaller}')) then
  begin
    try
      DownloadTemporaryFile('{#RuntimeUrl}', '{#RuntimeInstaller}', '', @OnDownloadProgress);
    except
      Result := 'The Windows App Runtime could not be downloaded. Install it from ' + '{#RuntimeUrl}' +
        ' and then run this Setup again.' + #13#10 + GetExceptionMessage;
    end;
  end;
end;
#endif
