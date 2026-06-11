# CLAUDE.md — Content Distribution Player

Guidance for AI agents (and developers) working in this repository. This file is
**self-contained**: everything needed to understand, build, and run the project is here.

---

## 1. What it is

**Content Distribution Player** (assembly `Player.exe`, namespace `ContentDistributionPlayer`)
is a Windows **digital-signage / remote presentation client** built for Accenture. It runs
full-screen on a PC attached to a display and shows content (**PowerPoint, video, images,
websites, PDF/Word/Excel**) that is **driven in real time** by a "controller/director" through a
**NodeJS** server.

- Real-time control over **MQTT-over-WebSocket** (`MQTTnet`).
- Presentation data and settings via **REST API** (a Laravel server), fetched with `HttpClient`.
- Remote files are **downloaded locally** before playback.
- Each client is identified by a **room (`Room`) + monitor (`Monitor`)** pair.
- **"Live content"** mode: overlays a full-page item on top of the running presentation.
- **"Display mode"**: launches external apps/URLs/files and sends the player to the background.

Tech stack: **C# / .NET Framework 4.8 / Windows Forms**. Windows-only.

---

## 2. Repository layout

```
<repo root>
├── Player.sln                     ← Visual Studio solution (open this)
├── JSON_communication_data.txt    ← protocol reference (MQTT / REST payload examples)
├── .gitignore
├── CLAUDE.md                      ← this file
├── update-server/                 ← sample static auto-update manifest + instructions
└── Player/                        ← the C# project
    ├── Player.csproj              ← OLD-STYLE csproj: every .cs is listed explicitly
    ├── App.config                 ← settings (host, room, monitor, contents folder, …)
    ├── packages.config            ← NuGet dependencies (classic restore)
    ├── Program.cs                 ← entry point
    ├── MainForm.cs / .Designer.cs / .resx
    ├── SettingsForm.cs              ← runtime config editor opened with CTRL+G
    ├── app.manifest                 ← DPI awareness / Windows execution manifest
    ├── Components/                ← domain logic
    ├── Controls/                  ← custom WinForms controls
    ├── Extensions/                ← helper extension methods
    ├── Utilities/                 ← infrastructure helpers
    ├── Properties/                ← AssemblyInfo, Settings, Resources
    └── Resources/                 ← logo.png, preload.gif
```

NuGet packages restore to a `packages/` folder **at the repo root** (sibling of `Player/`).

---

## 3. Architecture

A single WinForms executable. `MainForm` orchestrates three subsystems:

1. **Real-time communication** (`RealtimeCommunication`) — MQTT connection, subscribes to the
   room topics, parses incoming commands into .NET events.
2. **REST API** (`APIService`) — fetches main settings and presentation data.
3. **Presentation management** (`PresentationManager` → `SceneManager` → `ControlObjectElement`) —
   file download, scene preloading, content rendering, transitions.

`MainForm` is the glue: it receives RTC events, calls the API, drives the `PresentationManager`,
and updates the UI (cover, status messages, preloader).

### File map

All code lives under [Player/](Player/).

#### Entry point and form
| File | Role |
|------|------|
| [Player/Program.cs](Player/Program.cs) | `Main()` — runs `Application.Run(new MainForm())`. |
| [Player/MainForm.cs](Player/MainForm.cs) | **God class (~1800 lines)**: config load, ESC hotkey, lifecycle, all RTC event handlers, cover handling, **display mode**, display-mode downloads, Windows scaling. |
| [Player/MainForm.Designer.cs](Player/MainForm.Designer.cs) | Generated UI: `lblMessage`, `imgPreload`, `imgBackgroundLogo`, `imgPresentationBackground` (all `PictureBoxWithOpacity`), `panScenesContentsContainer`, `panLiveContentContainer`. |
| [Player/SettingsForm.cs](Player/SettingsForm.cs) | Modal config editor opened with **CTRL+G**; edits the running `Player.exe.config` and requires restart for connection/window settings to take effect. |
| [Player/app.manifest](Player/app.manifest) | Declares Per-Monitor DPI awareness so Windows display scaling does not bitmap-scale the player. |

#### Components — domain logic
| File | Role |
|------|------|
| [Player/Components/RealtimeCommunication.cs](Player/Components/RealtimeCommunication.cs) | MQTT client: connect/reconnect, room topics, publishes client messages, dispatches incoming messages onto `delegate`s (`OnInitPresentation`, `OnGotoScene`, …). |
| [Player/Components/APIService.cs](Player/Components/APIService.cs) | REST GET calls with retry, using a **single shared static `HttpClient`**. `API_URI` is set after connection. |
| [Player/Components/PresentationManager.cs](Player/Components/PresentationManager.cs) | **Core (~1580 lines)**: file download, scene preloading (`SceneManager[]`), de-duplication of resources shared across scenes, transitions, `goto` command queue, **live content**. |
| [Player/Components/SceneManager.cs](Player/Components/SceneManager.cs) | A single scene: loads documents, normalizes `bounds`, shows/hides content, stops/starts/destroys documents. |
| [Player/Components/ControlObjectElement.cs](Player/Components/ControlObjectElement.cs) | Wrapper for **one** content item: instantiates the right player (PowerPoint/LibVLC/CefSharp/PictureBox), sizes it, starts/stops/destroys it. |
| [Player/Components/PowerPointObject.cs](Player/Components/PowerPointObject.cs) | Drives PowerPoint via **Office Interop**: opens the file, re-parents the slideshow window into the WinForms container, `GotoSlide`/`Next`/`Pause`/`Resume`, hidden-slide and sub-slide handling. |
| [Player/Components/SceneTransition.cs](Player/Components/SceneTransition.cs) | Scene transition VO (`none` / `slideToLeft`, color, duration). |
| [Player/Components/FileData.cs](Player/Components/FileData.cs) | Download VO (resourceId, fileName, version, type, localFile). |
| [Player/Components/SceneContentFile.cs](Player/Components/SceneContentFile.cs) | Scene-content file VO (filename, indices, JSON). |
| [Player/Components/ZIndexSceneElement.cs](Player/Components/ZIndexSceneElement.cs) | VO used to order content by z-index. |
| [Player/Components/InfoMessage.cs](Player/Components/InfoMessage.cs) | On-screen status messages + preloader, animated with `dot-net-transitions`. |

#### Controls / Extensions / Utilities
| File | Role |
|------|------|
| [Player/Controls/PictureBoxWithOpacity.cs](Player/Controls/PictureBoxWithOpacity.cs) | `PictureBox` with an animatable `Opacity` property (fade via `ColorMatrix`). |
| [Player/Extensions/JObjectExtensions.cs](Player/Extensions/JObjectExtensions.cs) | `JObject.Get<T>(prop, ifNull)` — "safe" JSON access (swallows exceptions). |
| [Player/Extensions/ScreenExtensions.cs](Player/Extensions/ScreenExtensions.cs) | Per-monitor DPI (`GetDpiForMonitor`). |
| [Player/Utilities/DocumentsUtility.cs](Player/Utilities/DocumentsUtility.cs) | File-type detection by extension; `KillAllOfficeProcesses()`. |
| [Player/Utilities/ImageUtility.cs](Player/Utilities/ImageUtility.cs) | Image download; `LoadBitmapUnlocked()` (loads without locking the file on disk). |
| [Player/Utilities/FileUtility.cs](Player/Utilities/FileUtility.cs) | Purges partial `*_downloading` files. |
| [Player/Utilities/RemoteFileDownloader.cs](Player/Utilities/RemoteFileDownloader.cs) | Shared async downloader with partial-file suffix and atomic replace. |
| [Player/Utilities/RuntimeSettingsService.cs](Player/Utilities/RuntimeSettingsService.cs) | Reads/writes the running `Player.exe.config` for the CTRL+G settings panel. |
| [Player/Utilities/RuntimeStatusSnapshot.cs](Player/Utilities/RuntimeStatusSnapshot.cs) | Snapshot DTO for the CTRL+G health/status panel. |
| [Player/Utilities/AutoUpdateService.cs](Player/Utilities/AutoUpdateService.cs) | Static-server auto-update checker/stager; downloads XML + ZIP, optional SHA256 verification, writes installer script. |
| [Player/Utilities/WindowUtility.cs](Player/Utilities/WindowUtility.cs) | Win32 P/Invoke: `SetParent`, `MoveWindow`, `SetWindowLong`, cursor, default browser. |
| [Player/Utilities/WebUtility.cs](Player/Utilities/WebUtility.cs) | Extracts `HttpStatusCode` from a `WebException`. |
| [Player/Utilities/NumberUtility.cs](Player/Utilities/NumberUtility.cs) | `IsInt`/`IsFloat` using **`InvariantCulture`** (numbers come from JSON). |
| [Player/Utilities/LogTracer.cs](Player/Utilities/LogTracer.cs) | **Thread-safe** singleton logger over `TraceSource`, with 30-day rotation. |
| [Player/Utilities/URLSecurityZoneAPI.cs](Player/Utilities/URLSecurityZoneAPI.cs) | **Dead code** (legacy IE WebBrowser feature control, no longer used). |

#### Protocol reference
| File | Role |
|------|------|
| [JSON_communication_data.txt](JSON_communication_data.txt) | Example JSON payloads exchanged with the NodeJS server / REST API. **Source of truth for the protocol.** |
| [update-server/](update-server/) | Example static auto-update endpoint: `player-update.xml`, packaging scripts, and instructions for serving a release ZIP. |

---

## 4. Session flow

1. **Startup** ([MainForm.cs](Player/MainForm.cs)): reads `Properties.Settings`, validates config,
   kills stray Office processes, registers app hotkeys on the current Win32 handle, initializes VLC,
   the `PresentationManager`, and `RealtimeCommunication`.
2. **RTC connection**: shows the logo/messages, then `_rtc.Connect(...)` → MQTT to
   `<protocol>://<host>:<port>`. On connect the client subscribes to its topics and publishes
   `client-info` (app version + screen resolution).
3. **Connection success**: receives `room-init` on the client topic → reads `apiURI` and calls
   `GET /room/{room}/monitor/{monitor}` → **main settings** (background color, **cover**, any
   in-progress display-mode sessions). The cover is downloaded and shown.
4. **Presentation init**: an `init` message with `presentationId` + `sceneIndex` →
   `GET /presentation/{id}/monitor/{monitor}` → `PresentationManager.Initialize(...)` downloads
   **all** local files.
5. **Goto scene**: a `goto` message (`sceneIndex`/`subSceneIndex`) → `PresentationManager.GotoScene(...)`
   preloads scenes (`SceneManager`), shows content, syncs PowerPoint slides, applies z-indexes.
6. **Events to server**: the client publishes `download-start`/`download-ended`/`scene-changed`/`scene-content-error`.
7. **Unload**: an `unload` message → tears everything down and returns to the cover.

---

## 5. Communication protocol

### MQTT topics (prefix `rooms/{room}`)
- `clientId` = `R{room}_M{monitor}` · `clientUid` = `{clientId}_{yyyyMMddHHmmss}|{random45}`

| Topic | Direction | Meaning |
|-------|-----------|---------|
| `rooms/{room}/init` | server→client | Initialize a presentation |
| `rooms/{room}/unload` | server→client | Unload the presentation |
| `rooms/{room}/goto` | server→client | Go to scene/sub-scene |
| `rooms/{room}/client/{clientId}` | both | `room-init` (carries PIN, server→client) + status messages (client→server) |
| `rooms/{room}/client/{clientUid}` | both | `client-info` (client→server) + `app-need-update` (server→client) |
| `rooms/{room}/live-init` · `live-unload` · `live-goto` | server→client | Live content |
| `rooms/{room}/display-mode-start/{monitor}` | server→client | Start display mode |
| `rooms/{room}/display-mode-stop/{monitor}` · `rooms/{room}/display-mode-stop` | server→client | Stop display mode |

Client→server messages (on `client/{clientId}`): `client-info`, `download-start`, `download-ended`,
`scene-changed`, `scene-content-error`. See [JSON_communication_data.txt](JSON_communication_data.txt).

### REST API (base = `apiURI` received on connection)
- `GET /room/{room}/monitor/{monitor}` → main settings (`background_color`, `cover`, `displayModeClients`)
- `GET /presentation/{presentationId}/monitor/{monitor}` → presentation data (scenes, contents, bounds, params)
- `GET /resource/get?id={id}` → resource info (used by display-mode downloads)

### Error codes (`RealtimeCommunication`)
`1000` goto scene · `1001` presentation content · `1100` PowerPoint · `2000` display mode.

### Content types and players
| Type | Extensions | Rendering |
|------|-----------|-----------|
| PowerPoint | `ppt`, `pptx` | **Office Interop** (slideshow re-parented into the form) |
| Video | `mp4`, `mkv`, `ogg`, `flv`, `mov` | **LibVLCSharp** (`from`/`to`/`loop`) |
| Website | `http(s)://`, `file://` | **CefSharp** (Chromium) |
| Image | `jpg`, `jpeg`, `png`, `gif` | `PictureBox` |
| PDF/Word/Excel | `pdf`/`doc(x)`/`xls(x)` | Pages rendered **server-side as PNG**, shown as images |

### Content bounds — `[x, y, w, h]`
- integer `>= 0`: pixels · integer `-1`: fill/align to container (w/h full, x/y right/bottom)
- float (`0.0–1.0`): percentage of the container. **Always invariant format (`.`)**.

---

## 6. Configuration

Settings live in [Player/App.config](Player/App.config) (section
`ContentDistributionPlayer.Properties.Settings`). At build time they are written to
`Player.exe.config` next to the executable — edit that file on the target machine.

| Key | Type | Notes |
|-----|------|-------|
| `NodeJSHost` | string | NodeJS server host |
| `NodeJSPort` | int | Port |
| `NodeJSProtocol` | string | **`ws` or `wss`** (validated) |
| `Room` / `Monitor` | int | Client identity (both must be `> 0`) |
| `ContentsFolder` | string | Local folder (**must exist**); `presentations/` and `log/` are created inside it |
| `UseFullScreen` | bool | Borderless full screen |
| `ScreenResolutionWidth/Height` | int | If `> 0` (and not full screen): borderless window of that size |
| `PurgePresentationData` | bool | If `true`, deletes downloaded documents on unload |
| `LogMinimumLevel` | string | Logger threshold: `All`, `Verbose`, `Information`, `Warning`, `Error`, `Critical`, `Off` |
| `AutoUpdateEnabled` | bool | Enables update controls/configuration; the player does **not** check/install updates at startup |
| `AutoUpdateManifestUrl` | string | URL of the static XML manifest used by the CTRL+G **Check for updates** button, e.g. `http://localhost:8080/player-update.xml` |

> Always set `ContentsFolder`, `NodeJSHost`, `Room`, `Monitor` for the target machine. With bad
> config the app shows an error `MessageBox` and exits.

---

## 7. How to build

> Building is **Windows-only**. It cannot be built on macOS/Linux (Office Interop COM references,
> CefSharp and LibVLC native binaries, .NET Framework).

### 7.1 Prerequisites (build machine)
- **Windows 10/11, 64-bit.**
- **Visual Studio 2022** (Community is fine) with the **".NET desktop development"** workload,
  *or* the **Build Tools for Visual Studio 2022**.
- **.NET Framework 4.8 SDK / Targeting Pack** (selectable in the VS Installer under *Individual components*).
- **Microsoft Office with PowerPoint installed.** Required at build time because the project has
  `COMReference` entries (Office Core, PowerPoint, stdole, VBIDE) resolved from the registry — and
  required at runtime to play PowerPoint content.
- **`nuget.exe`** on `PATH` (the project uses classic `packages.config` restore).
  Download from <https://www.nuget.org/downloads> if needed.
- Internet access for the first NuGet restore (CefSharp, LibVLC, MQTTnet, etc.).

### 7.2 Restore NuGet packages
From the repo root (the folder containing `Player.sln`):
```bat
nuget restore Player.sln
```
This downloads everything into `packages/` at the repo root. (In Visual Studio: right-click the
solution → **Restore NuGet Packages**.) The build will hard-fail with a clear "missing package"
error if this step is skipped.

### 7.3 Build

**Option A — Visual Studio (recommended):**
1. Open `Player.sln`.
2. Pick configuration **Release** / platform **Any CPU**.
3. **Build → Rebuild Solution**.

**Option B — command line** (from the *Developer Command Prompt for VS 2022*, repo root):
```bat
msbuild Player.sln /p:Configuration=Release /t:Rebuild
```

CefSharp runs in AnyCPU mode (`CefSharpAnyCpuSupport=true`): the build copies the native CEF
binaries (x86 + x64) and libvlc into the output folder automatically.

### 7.4 Build output
```
Player\bin\Release\Player.exe   ← the executable
Player\bin\Release\             ← + all native deps (CEF, libvlc) and managed DLLs
```
For a **Debug** build, output is in `Player\bin\Debug\`.

---

## 8. How to run

### 8.1 Run on the build machine
1. Build (section 7).
2. Edit `Player\bin\Release\Player.exe.config` and set at least `ContentsFolder` (must exist),
   `NodeJSHost`, `NodeJSPort`, `NodeJSProtocol`, `Room`, `Monitor`.
3. Double-click `Player.exe` (or run it from the output folder).
4. The app shows the logo, connects to MQTT, fetches settings, and displays the cover. It then
   waits for the controller to init a presentation.
5. Press **ESC** to quit.

Runtime shortcuts:
- **ESC** quits the player.
- **CTRL+H** toggles always-on-top.
- **CTRL+G** opens the settings + health/status modal for the running `Player.exe.config`.

### 8.2 Static auto-update
The player can update from a plain static server, but update checks are **manual only** from the
CTRL+G control panel. Point `AutoUpdateManifestUrl` to an XML file like
[update-server/player-update.xml](update-server/player-update.xml), then press **Check for updates**:

```xml
<update>
  <version>1.0.1</version>
  <zipUrl>http://10.107.188.6/content-update/ContentNuovo_Player_101.zip</zipUrl>
  <sha256></sha256>
</update>
```

The manifest `<version>` is compared with `MainForm.APP_VERSION`, not with the Windows file-version
metadata of `Player.exe`. The current `MainForm.APP_VERSION` is shown in the CTRL+G panel.

To prepare a static-server release, run the packaging script with **no arguments**:

```bat
update-server\build-update-package.cmd
```

With no version argument it **auto-increments the patch** of `MainForm.APP_VERSION` (e.g. `1.0.0` →
`1.0.1`), writes it back into the source, **rebuilds** the player in Release so the shipped `.exe`
carries the new version, zips `Player/bin/Release/` as `ContentNuovo_Player_<version>.zip` (version
without dots, e.g. `ContentNuovo_Player_101.zip`), computes SHA256, and
updates `player-update.xml`. If the build fails, the source version bump is reverted automatically.
You can still force a version (`build-update-package.cmd 1.5.0`) or redirect the output to the real
server root (third arg = base URL):

```bat
update-server\build-update-package.cmd "" D:\ContentDistribution-player-update-server http://updates.local
```

See [update-server/README.md](update-server/README.md) for the full guide and the PowerShell knobs
(`-Increment minor`, `-SkipBuild`, …). The ZIP must contain the **contents** of `Player/bin/Release/`,
not an extra parent folder. If the
manifest version is newer than `MainForm.APP_VERSION`, the control panel downloads the ZIP under
`ContentsFolder/updates/<version>/`, verifies `sha256` when present, extracts it, writes
`install-update.cmd`, and asks whether to install. If confirmed, the player starts that script and
exits. The script waits for the current `Player.exe` PID to end, copies the staged package over the
app folder, and restarts `Player.exe`.

### 8.3 Deploy to another PC
- Copy the **entire** `Player\bin\Release\` folder (not just the `.exe`) — it contains the CEF and
  libvlc native files.
- The target PC needs: **.NET Framework 4.8 runtime**, **Microsoft PowerPoint**, and the
  **Visual C++ Redistributable** (for CEF).
- Edit `Player.exe.config` as in step 8.1 before first launch.

### 8.4 Troubleshooting
| Symptom | Cause / fix |
|---------|-------------|
| Build error: *"references NuGet package(s) that are missing"* | Run `nuget restore Player.sln` (section 7.2). |
| Build error on `COMReference` (Office/stdole/VBIDE) | Microsoft Office/PowerPoint is not installed/registered on the build machine. |
| Startup `MessageBox`: *"Contents folder … does not exist"* | Fix `ContentsFolder` in `Player.exe.config`. |
| App starts but never connects | Check `NodeJSHost/Port/Protocol`, the broker is reachable, firewall, and `ws`/`wss` matches the server. |
| Video/website not rendering after copy | The `bin` folder was not copied whole (missing libvlc/CEF natives), or VC++ Redistributable is missing. |
| PowerPoint slides don't show | PowerPoint not installed on the runtime machine. |

---

## 9. Conventions and constraints

- **All network I/O** goes through `RealtimeCommunication` (MQTT) and `APIService` (REST). Do not add ad-hoc sockets/HTTP.
- **`HttpClient` is a single shared static instance** in `APIService`: do not create one per call (socket exhaustion).
- **JSON access** always via `JObject.Get<T>(...)` (null-safe).
- **Numeric parsing** always with `InvariantCulture` (numbers come from JSON): never parse `bounds` with the current culture.
- **Image loading** always via `ImageUtility.LoadBitmapUnlocked(...)` (never `Image.FromFile`, which locks the file on disk).
- **Downloads** use `RemoteFileDownloader` for presentation files, display-mode resources, images, and auto-update packages. It writes `*_downloading` partials and replaces the final file only after a complete download.
- **Threading/UI**: MQTT callbacks and async download continuations run off the UI thread → use `Invoke`/`BeginInvoke` on `MainContainer`/`SceneContainer`. Keep doing so.
- **`APP_VERSION`** (`MainForm.APP_VERSION = "1.0.0"`): the server compares it and may reply `app-need-update`. **Do not change it** without coordinating with the server.
- **Office**: PowerPoint instances created by this app are tracked by process id. `DocumentsUtility.KillAllOfficeProcesses()` keeps its legacy name but only terminates those tracked PowerPoint processes, avoiding unrelated user presentations. Release COM objects with `Marshal.ReleaseComObject`.
- **Hotkeys**: **ESC** quits, **CTRL+H** toggles always-on-top, **CTRL+G** opens the runtime settings modal. Hotkeys are registered in `OnHandleCreated` and unregistered in `OnHandleDestroyed` because changing border/fullscreen style can recreate the form handle.
- **OLD-STYLE `.csproj`**: every `.cs` file is listed explicitly in `<Compile Include>`. **If you add/remove a file you must update [Player/Player.csproj](Player/Player.csproj)**, otherwise the build silently diverges.
- **DPI / Windows Scale**: the app uses `app.manifest` + early process DPI awareness in `Program.Main()` to avoid Windows bitmap-scaling the player when System → Display scale is changed. Keep this if presentation quality matters.
- **Auto-update**: checks are manual from the CTRL+G panel; never install updates automatically at startup. Keep the static manifest format in sync with `AutoUpdateService`. Bump `MainForm.APP_VERSION` only together with a release ZIP and manifest update.
- **WinForms resources** (logo, preloader) live in [Player/Properties/Resources.resx](Player/Properties/Resources.resx) and are referenced via `Properties.Resources.<key>`. To swap the logo: drop the new PNG in `Player/Resources/`, point the `logo` entry in `Resources.resx` at it, and keep `Resources.Designer.cs` + `MainForm.Designer.cs` (`Properties.Resources.logo`) in sync.
- UI language is English; comments/logs are mixed Italian/English.

---

## 10. Security (be aware)

- **Display mode = remote command execution**: an MQTT message can make the client run
  `Process.Start(commandString)` / open a URL / execute downloaded files ([MainForm.cs](Player/MainForm.cs)).
  Powerful by design → the MQTT channel **must** be secured.
- **MQTT currently has no authentication**: `MqttClientOptionsBuilder` uses only `WithWebSocketServer`
  + `WithCleanSession` — **no** credentials/certificate validation. Anyone who knows the room can
  publish commands. Adding auth/TLS requires coordination with the NodeJS server.
- **Secrets in `App.config`**: the real host and a local path are committed. They should be
  externalized and scrubbed from history.
- **Plain-HTTP downloads** are allowed for content and update packages. Auto-update supports optional SHA256 verification in the XML manifest; use it for real deployments.

---

## 11. Known gotchas

- **Pervasive `async void`** (RTC handlers, `Connect`, `Reconnect`): exceptions are unobservable. Be careful when editing.
- **`PowerPointObject.SendPowerPointCommand`** uses `Task.Run(...).Wait(5s)` with COM STA: can block the UI; fragile flow.
- **PowerPoint COM leaks**: not every intermediate COM object is released (mitigated by tracked PowerPoint process cleanup).
- **One `LibVLC` per video** (`ControlObjectElement`): heavy; usually a single shared instance is used.
- **`PublishMessage` uses `WithRetainFlag()` on everything**: even transient events (`scene-changed`, `download-*`) are *retained* → a late subscriber can receive stale state. Revisit if you touch the protocol.
- **Auto-update install is script-based**: the app cannot overwrite its own running executable, so update installation is delegated to `install-update.cmd` after the player exits.
- **`URLSecurityZoneAPI.cs`** is dead code (legacy IE WebBrowser); kept in the build to avoid touching the `.csproj`.
- **`WindowsScaleFactor`** is computed but never used.
- An orphaned `Player/Resources/accenture_logo.png` may remain on disk after the logo was switched to `logo.png`; it is no longer referenced and can be deleted.

---

## 12. Recent improvements (June 2026)

Already-applied security/robustness fixes to preserve: culture-invariant `bounds` parsing; shared
static `HttpClient`; images loaded without file locks + disposed; thread-safe `LogTracer` with robust
rotation; `RandomString` off-by-one fix and bounded retry in `PublishMessage`; operator-precedence fix
in `DocumentsUtility.GetDocumentTypeByFileName`; backwards control removal in `SceneManager`; HDC/Graphics
release in `GetWindowsScalingFactor`. The startup logo was switched from `accenture_logo.png` to `logo.png`.
The logo is fitted with preserved aspect ratio, app hotkeys now survive handle recreation, **CTRL+H**
toggles topmost, **CTRL+G** opens a runtime `Player.exe.config` editor, MQTT payload handling catches malformed JSON,
and the app declares Per-Monitor DPI awareness to preserve presentation sharpness under Windows display scaling.
Downloads are centralized through `RemoteFileDownloader`, PowerPoint cleanup now targets only processes created
by this player, logging has a configurable threshold, the CTRL+G modal includes a runtime health/status panel,
and a static-server auto-update flow can download an XML manifest + release ZIP, verify SHA256, stage the package,
run an installer script, and restart the player.
