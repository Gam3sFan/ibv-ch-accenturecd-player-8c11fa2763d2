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
└── Player/                        ← the C# project
    ├── Player.csproj              ← OLD-STYLE csproj: every .cs is listed explicitly
    ├── App.config                 ← settings (host, room, monitor, contents folder, …)
    ├── packages.config            ← NuGet dependencies (classic restore)
    ├── Program.cs                 ← entry point
    ├── MainForm.cs / .Designer.cs / .resx
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
| [Player/Utilities/WindowUtility.cs](Player/Utilities/WindowUtility.cs) | Win32 P/Invoke: `SetParent`, `MoveWindow`, `SetWindowLong`, cursor, default browser. |
| [Player/Utilities/WebUtility.cs](Player/Utilities/WebUtility.cs) | Extracts `HttpStatusCode` from a `WebException`. |
| [Player/Utilities/NumberUtility.cs](Player/Utilities/NumberUtility.cs) | `IsInt`/`IsFloat` using **`InvariantCulture`** (numbers come from JSON). |
| [Player/Utilities/LogTracer.cs](Player/Utilities/LogTracer.cs) | **Thread-safe** singleton logger over `TraceSource`, with 30-day rotation. |
| [Player/Utilities/URLSecurityZoneAPI.cs](Player/Utilities/URLSecurityZoneAPI.cs) | **Dead code** (legacy IE WebBrowser feature control, no longer used). |

#### Protocol reference
| File | Role |
|------|------|
| [JSON_communication_data.txt](JSON_communication_data.txt) | Example JSON payloads exchanged with the NodeJS server / REST API. **Source of truth for the protocol.** |

---

## 4. Session flow

1. **Startup** ([MainForm.cs](Player/MainForm.cs)): reads `Properties.Settings`, validates config,
   kills stray Office processes, registers the **ESC** hotkey (quits the app), initializes VLC,
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

### 8.2 Deploy to another PC
- Copy the **entire** `Player\bin\Release\` folder (not just the `.exe`) — it contains the CEF and
  libvlc native files.
- The target PC needs: **.NET Framework 4.8 runtime**, **Microsoft PowerPoint**, and the
  **Visual C++ Redistributable** (for CEF).
- Edit `Player.exe.config` as in step 8.1 before first launch.

### 8.3 Troubleshooting
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
- **Threading/UI**: MQTT callbacks and `WebClient` events run off the UI thread → use `Invoke`/`BeginInvoke` on `MainContainer`/`SceneContainer`. Keep doing so.
- **`APP_VERSION`** (`MainForm.APP_VERSION = "1.0.0"`): the server compares it and may reply `app-need-update`. **Do not change it** without coordinating with the server.
- **Office**: `DocumentsUtility.KillAllOfficeProcesses()` is called before start/teardown to avoid zombie PowerPoint instances. Release COM objects with `Marshal.ReleaseComObject`.
- **Hotkeys**: only **ESC** (quits). The others are commented out.
- **OLD-STYLE `.csproj`**: every `.cs` file is listed explicitly in `<Compile Include>`. **If you add/remove a file you must update [Player/Player.csproj](Player/Player.csproj)**, otherwise the build silently diverges.
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
- **Plain-HTTP downloads** are allowed: downloaded files are opened/executed without integrity checks.

---

## 11. Known gotchas

- **Pervasive `async void`** (RTC handlers, `Connect`, `Reconnect`): exceptions are unobservable. Be careful when editing.
- **`PowerPointObject.SendPowerPointCommand`** uses `Task.Run(...).Wait(5s)` with COM STA: can block the UI; fragile flow.
- **PowerPoint COM leaks**: not every intermediate COM object is released (mitigated by `KillAllOfficeProcesses`).
- **One `LibVLC` per video** (`ControlObjectElement`): heavy; usually a single shared instance is used.
- **`PublishMessage` uses `WithRetainFlag()` on everything**: even transient events (`scene-changed`, `download-*`) are *retained* → a late subscriber can receive stale state. Revisit if you touch the protocol.
- **Download logic is duplicated** (in `PresentationManager`, `MainForm`, `ImageUtility`): a good candidate to extract into one shared downloader.
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
