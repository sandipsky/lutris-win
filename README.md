# Lutris for Windows (WinUI 3)

A native Windows 11 game library and launcher. Add the executables of games you own, give
them portrait and landscape banners, launch them from a grid or list, and track play time.
This is the native rewrite of the earlier Electron app in `../lutris-win`; both use the same
library format, so archives exported from one import into the other.

## Requirements

- Windows 10 1809 or later (Windows 11 recommended for Mica and rounded corners)
- Visual Studio 2022 17.10+ or Visual Studio 2026 with the **WinUI application development** workload
- Developer Mode enabled in Windows Settings

## Build and run

Open `Lutris-Native-Win.slnx` in Visual Studio, pick the **x64** platform and press F5, or from a terminal:

```
dotnet build -c Debug -p:Platform=x64
dotnet run -p:Platform=x64
```

The app runs unpackaged (`WindowsPackageType=None`), so no certificate or MSIX registration is needed.

## Build the installer

One command publishes the app and produces the installer (and, with `-Portable`, a zip):

```
.\build-installer.ps1
.\build-installer.ps1 -Portable
.\build-installer.ps1 -BundleRuntime
```

Output lands in `dist\`:

- `Lutris-Windows-<version>-Setup.exe`: an Inno Setup installer (about 52 MB). It installs per user by
  default with no admin prompt, offers an all-users install, adds a Start menu entry and an optional
  desktop shortcut, and registers an uninstaller in Apps & Features.
- `Lutris-Windows-<version>-Portable-x64.zip`: the publish folder zipped, for people who prefer no installer.

The installer checks for the Windows App Runtime 2.5 on the target PC. If it is missing, Setup
downloads Microsoft's runtime installer (about 120 MB) and runs it silently; `-BundleRuntime` embeds
that installer instead so Setup also works offline. The version number comes from `<Version>` in the
csproj, so bump it there before building a release.

Requirements: Visual Studio or the .NET SDK, and Inno Setup 6 (https://jrsoftware.org/isdl.php; a
per-user install in `%LOCALAPPDATA%\Programs\Inno Setup 6` is found automatically). The script and
the Inno script live in `build-installer.ps1` and `installer\Lutris.iss`.

Unsigned installers trigger a Windows SmartScreen warning on other PCs. To avoid it, sign
`Lutris.exe` and the Setup exe with a code-signing certificate (for example via Azure Trusted Signing
or a certificate from a commercial CA) using `signtool`, or Inno Setup's `SignTool` setting.

## Publish only

```
dotnet publish -c Release -p:Platform=x64 -p:PublishProfile=win-x64
```

The output in `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\` bundles the .NET runtime, so
target PCs do not need .NET installed. They do need the **Windows App Runtime 2.5** (the shared
Windows App SDK runtime). Download `WindowsAppRuntimeInstall-x64.exe` from
https://aka.ms/windowsappsdk/2.5/latest/windowsappruntimeinstall-x64.exe and run it from your
installer (Inno Setup or NSIS) before launching the app. If the runtime is missing, the app shows a
prompt pointing at that download instead of starting.

Two publish settings are intentionally off because they broke the app with Windows App SDK 2.5.1:

- `WindowsAppSDKSelfContained=true` (bundling the Windows App SDK runtime) produced an exe that
  crashed at startup inside the native XAML runtime.
- `PublishTrimmed=true` produced an exe whose x:Bind bindings and event handlers never connected,
  so the library rendered empty.

The resulting publish folder is about 200 MB uncompressed (roughly 70 MB inside an installer).

## Where data lives

- Library database: `%LOCALAPPDATA%\Lutris\library.db` (SQLite, WAL mode)
- Banner images: `%LOCALAPPDATA%\Lutris\banners\`
- Crash log: `%LOCALAPPDATA%\Lutris\lutris.log`

Use **More options › Export library** to produce a portable `.zip` (the database plus the banners folder),
and **Import library** to replace the current library from one. On first run, if a library from the
Electron version is found under `%APPDATA%\lutris-win`, the app offers to bring it over.

Developer overrides (environment variables):

- `LUTRIS_DATA_DIR=<folder>` relocates the database and banners, handy for testing against a scratch library.
- `LUTRIS_THEME=Light|Dark` forces a theme instead of following the Windows setting.

## Project layout

| Folder | Purpose |
| --- | --- |
| `Data/` | SQLite access, banner file store, archive import/export, data paths |
| `Services/` | Library facade, game launcher with play-time tracking, file pickers, formatting |
| `Models/` | `Game` record and the bindable `GameItem` wrapper |
| `ViewModels/` | `LibraryViewModel`: search, sort, view mode, selection |
| `Controls/` | `GameEditorDialog`, the add/edit form |
| `Interop/` | The little Win32 needed for DPI and minimum window size |
| `Assets/` | App icon and packaging logos generated from the Lutris logo |

## Keyboard shortcuts

- `Ctrl+N` add a game
- `Ctrl+F` focus search
- `F5` refresh the library
- Double-click a game to play it; right-click for more actions
