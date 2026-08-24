# Sunshine

A small Windows Minecraft launcher focused on fast startup, low idle usage, and a clean
dark/transparent UI.

## Quick Launch Guide

### 1. Install what you need

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Minecraft already installed once through the official launcher
- Java installed or available from Minecraft's bundled runtime

### 2. Open the project folder

```powershell
cd path\to\sunshine2
```

### 3. Build the launcher

```powershell
dotnet build
```

### 4. Run Sunshine

```powershell
dotnet run --project src/Sunshine/Sunshine.csproj
```

You can also run the built executable after building:

```powershell
src\Sunshine\bin\Debug\net8.0-windows\Sunshine.exe
```

### 5. Launch Minecraft

1. Enter a username.
2. Pick one of your installed Minecraft versions.
3. Click launch.

Sunshine uses versions already installed in:

```text
%APPDATA%\.minecraft\versions
```

It does not download Minecraft files. If a version is missing libraries or a client jar,
open that version once in the official Minecraft Launcher, then try Sunshine again.

Launch logs are saved here:

```text
%LOCALAPPDATA%\Sunshine\logs\latest.log
```

## What it does

- Reads already-installed versions from `%APPDATA%\.minecraft\versions` (vanilla, Fabric,
  Quilt, NeoForge - anything with a valid `<id>.json`). It never downloads or modifies game
  files; it only launches what's already on disk.
- Resolves `inheritsFrom` chains (e.g. a Fabric profile merged onto its vanilla parent).
- Builds the java classpath/JVM args/game args from the version JSON and launches it directly.
- Offline accounts only: username -> deterministic offline UUID (same scheme vanilla
  Minecraft uses for `OfflinePlayer:<name>`), no Microsoft auth involved.
- Optional G1GC-tuned JVM flags for smoother frame times.
- Exits itself right after the game starts (toggleable) so it isn't sitting in memory while
  you play.

## Project layout

- `src/Sunshine/Models` - version.json schema + resolved-version/profile types.
- `src/Sunshine/Services`
  - `VersionResolver` - scans installed versions, merges `inheritsFrom` chains.
  - `LibraryResolver` - evaluates OS rules, resolves classpath/native jars.
  - `GameLauncher` - builds the full java command line and starts the process.
  - `JavaLocator` - finds a `javaw.exe` (Mojang-bundled runtime, `JAVA_HOME`, or `PATH`).
  - `OfflineAuth` - offline-mode UUID derivation.
  - `SettingsStore` - persists last-used profile to `%LOCALAPPDATA%\Sunshine\settings.json`.
- `src/Sunshine/Interop/NativeMethods.cs` - Win11 dark title bar + acrylic backdrop via DWM.
- `src/Sunshine/MainWindow.xaml(.cs)` - the whole UI (single window, custom chrome).

## Known limitations (v1)

- Windows 11 only for the acrylic/dark-titlebar chrome (falls back to a plain window on
  older builds, since `DwmSetWindowAttribute`'s backdrop/dark-mode attributes are ignored
  pre-Win11 rather than erroring).
- No download/repair flow - if a version's libraries or client jar aren't fully present,
  launch will fail with a message pointing at the missing file rather than fetching it.
- No Forge support (Forge's modern installer runs Java "processors" as part of install,
  which this launcher doesn't execute). Fabric/Quilt/NeoForge/vanilla work because their
  profiles are plain, already-processed version JSONs.
