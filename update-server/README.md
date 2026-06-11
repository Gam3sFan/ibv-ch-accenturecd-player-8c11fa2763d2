<p align="center">
  <img src="../Player/Resources/logo.png" alt="Content Distribution Player" width="160">
</p>

<h1 align="center">Content Distribution Player — Update Server</h1>

<p align="center">
  Static auto-update server for <code>Player.exe</code><br>
  <em>Publish a new version with a single command.</em>
</p>

---

## What this folder is

A minimal **static update server**. The player checks the manifest here and, if a newer
version is available, downloads and installs it. You only need a folder that can be served
over HTTP — no database, no backend.

| File | Role |
|------|------|
| `build-update-package.cmd` | One-click entry point. Auto-increments the version, rebuilds, packages, updates the manifest. |
| `build-update-package.ps1` | The actual logic (version bump → rebuild → ZIP → SHA256 → manifest). |
| `player-update.xml` | The **manifest** the player reads. Points to the latest version + ZIP + checksum. |
| `Player-<version>.zip` | A packaged release (created by the script). |

---

## How versioning works (read this once)

There is **one source of truth** for the player version:

```
Player/MainForm.cs  →  public static string APP_VERSION = "1.0.0";
```

- This value is **compiled into `Player.exe`**. It is *not* the Windows file-version metadata.
- The player compares `APP_VERSION` against `<version>` in `player-update.xml`.
- If the manifest version is **higher**, the player offers to update.

> [!IMPORTANT]
> Because the version lives inside the `.exe`, publishing an update **must rebuild the player**.
> If you only changed the manifest without rebuilding, the new `.exe` would still report the old
> version and clients would update **in an endless loop**.
>
> The script handles this for you: it bumps `APP_VERSION` in the source **and rebuilds** before
> packaging, so the manifest and the shipped `.exe` always match.

---

## Publishing a new version (the easy way)

### Prerequisites (build machine, Windows only)
- Visual Studio 2022 **or** Build Tools for VS 2022 (the script finds MSBuild automatically).
- Microsoft Office / PowerPoint installed (required to compile the COM references).
- `nuget.exe` on `PATH` (optional but recommended — the script restores packages if it's there).

### Step 1 — Run the script

From this folder, just run:

```bat
build-update-package.cmd
```

That's it. With no arguments, the script will:

1. Read the current `APP_VERSION` (e.g. `1.0.0`).
2. **Auto-increment the patch** → `1.0.1`.
3. Write the new version into `MainForm.cs` (and the assembly metadata).
4. **Rebuild** the player in Release.
5. Create `Player-1.0.1.zip` from `Player/bin/Release/`.
6. Compute its SHA256.
7. Update `player-update.xml`.

If the build fails for any reason, the version change in the source is **automatically reverted**,
so you never end up half-bumped.

### Step 2 — Serve this folder

Any static HTTP server works. For a quick local test:

```bat
python -m http.server 8080
```

### Step 3 — Point the player at the manifest

On each client, set in `Player.exe.config`:

```xml
<setting name="AutoUpdateEnabled"   serializeAs="String"><value>True</value></setting>
<setting name="AutoUpdateManifestUrl" serializeAs="String">
  <value>http://localhost:8080/player-update.xml</value>
</setting>
```

### Step 4 — Update from the client

On the client, open the control panel with **CTRL+G** and press **Check for updates**.
If the manifest version is newer, the player:

1. Downloads the ZIP under `ContentsFolder/updates/<version>/`,
2. Verifies the SHA256 (when present in the manifest),
3. Extracts it and writes `install-update.cmd`,
4. Asks for confirmation. If confirmed, it launches the script and exits.

The installer script waits for the old `Player.exe` to close, copies the new files over the app
folder, and restarts the player on the new version.

---

## Command reference

`build-update-package.cmd` forwards its arguments to the PowerShell script.

```bat
build-update-package.cmd [version] [outputDir] [baseUrl]
```

| Position | Argument | Default | Meaning |
|----------|----------|---------|---------|
| `%1` | `version` | *auto-increment* | Force a specific version, e.g. `1.5.0`. Leave empty (`""`) to auto-increment. |
| `%2` | `outputDir` | this folder | Where to write the ZIP + manifest (e.g. your real server root). |
| `%3` | `baseUrl` | `http://localhost:8080` | Base URL written into `<zipUrl>` in the manifest. |

### Examples

```bat
rem Auto-increment patch (1.0.0 -> 1.0.1), build, package here
build-update-package.cmd

rem Force a specific version
build-update-package.cmd 2.0.0

rem Auto-increment, but write the release to the real server root
build-update-package.cmd "" D:\ContentDistribution-update-server

rem Auto-increment, real server root, real public URL
build-update-package.cmd "" D:\ContentDistribution-update-server http://updates.mycompany.local
```

### Advanced (PowerShell directly)

The `.ps1` exposes a few extra knobs the `.cmd` does not:

```powershell
# Bump the minor instead of the patch (1.0.0 -> 1.1.0)
powershell -ExecutionPolicy Bypass -File .\build-update-package.ps1 -Increment minor

# Re-package the current build WITHOUT rebuilding (advanced; APP_VERSION must already match)
powershell -ExecutionPolicy Bypass -File .\build-update-package.ps1 -Version 1.0.1 -SkipBuild
```

| Parameter | Default | Meaning |
|-----------|---------|---------|
| `-Version` | auto | Explicit version; empty = auto-increment. |
| `-Increment` | `patch` | `patch` or `minor` (used only when `-Version` is empty). |
| `-BaseUrl` | `http://localhost:8080` | Base URL for `<zipUrl>`. |
| `-OutputDir` | script folder | ZIP + manifest destination. |
| `-ReleaseDir` | `..\Player\bin\Release` | Build output to package. |
| `-ManifestPath` | `<OutputDir>\player-update.xml` | Manifest file to update. |
| `-SkipBuild` | off | Package the existing build as-is (no rebuild, no source bump). |

---

## The manifest format

```xml
<?xml version="1.0" encoding="utf-8"?>
<update>
  <version>1.0.1</version>
  <zipUrl>http://localhost:8080/Player-1.0.1.zip</zipUrl>
  <sha256>dcbedafd0c58804f60d4b6c558d790ee3e443bc8dde42dfa938efed363537e3a</sha256>
</update>
```

- `<version>` — compared against `MainForm.APP_VERSION` using `System.Version`.
- `<zipUrl>` — absolute URL where the player downloads the release ZIP.
- `<sha256>` — optional integrity check. **Always keep it** for real deployments (the script fills it in automatically).

> The ZIP must contain the **contents** of `Player/bin/Release/` (the `.exe`, CEF and libvlc
> native files, managed DLLs), **not** an extra parent folder.

---

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `MSBuild not found` | Install VS 2022 / Build Tools, or run from a *Developer Command Prompt for VS 2022*. |
| Build fails: *missing NuGet packages* | Put `nuget.exe` on `PATH`, or run `nuget restore Player.sln` from the repo root first. |
| Build fails on `COMReference` (Office/stdole/VBIDE) | Microsoft Office/PowerPoint is not installed/registered on the build machine. |
| Client never offers the update | Manifest `<version>` must be **higher** than the client's `APP_VERSION`; check `AutoUpdateManifestUrl` is reachable. |
| Client updates over and over | The shipped `.exe` reports an old version. Don't use `-SkipBuild`; let the script rebuild. |
| `low disk space` while zipping | Pass an `outputDir` on another drive (see examples). |

---

## Security note

Plain-HTTP downloads are allowed. For real deployments, serve the manifest and ZIP over **HTTPS**
and always include the `<sha256>` (the script does this for you). The MQTT control channel and this
update channel are powerful by design — keep them on a trusted network.
