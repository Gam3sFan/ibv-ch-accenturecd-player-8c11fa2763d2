<p align="center">
  <img src="Player/Resources/logo.png" alt="Content Distribution Player" width="170">
</p>

<h1 align="center">Content Distribution Player</h1>

<p align="center">
  Windows digital-signage / remote presentation client, driven in real time.<br>
  <em>PowerPoint · Video · Images · Websites · PDF / Word / Excel</em>
</p>

---

## What it is

**Content Distribution Player** (`Player.exe`) is a Windows **digital-signage / remote presentation
client** built for Accenture. It runs full-screen on a PC attached to a display and shows content
that is **driven in real time** by a controller/director through a NodeJS server.

- Real-time control over **MQTT-over-WebSocket** (`MQTTnet`).
- Presentation data and settings via a **REST API** (Laravel), fetched with `HttpClient`.
- Remote files are **downloaded locally** before playback.
- Each client is identified by a **room + monitor** pair.
- **Live content** overlays a full-page item on top of the running presentation.
- **Display mode** launches external apps / URLs / files and sends the player to the background.

**Tech stack:** C# / .NET Framework 4.8 / Windows Forms. **Windows-only.**

### Supported content

| Type | Extensions | Rendering |
|------|-----------|-----------|
| PowerPoint | `ppt`, `pptx` | Office Interop (slideshow re-parented into the form) |
| Video | `mp4`, `mkv`, `ogg`, `flv`, `mov` | LibVLCSharp |
| Website | `http(s)://`, `file://` | CefSharp (Chromium) |
| Image | `jpg`, `jpeg`, `png`, `gif` | `PictureBox` |
| PDF / Word / Excel | `pdf` / `doc(x)` / `xls(x)` | Pages rendered server-side as PNG, shown as images |

---

## Architecture (at a glance)

A single WinForms executable. `MainForm` orchestrates three subsystems:

1. **`RealtimeCommunication`** — MQTT connection; subscribes to the room topics, parses incoming
   commands into .NET events.
2. **`APIService`** — REST calls for main settings and presentation data (single shared static `HttpClient`).
3. **`PresentationManager → SceneManager → ControlObjectElement`** — file download, scene preloading,
   content rendering, transitions.

```
<repo root>
├── Player.sln                  ← open this in Visual Studio
├── README.md                   ← this file
├── CLAUDE.md                   ← in-depth guide (architecture, conventions, gotchas)
├── JSON_communication_data.txt ← protocol reference (MQTT / REST payloads)
├── update-server/              ← static auto-update server + packaging scripts
└── Player/                     ← the C# project
    ├── Components/  Controls/  Extensions/  Utilities/
    ├── MainForm.cs  SettingsForm.cs  Program.cs
    └── App.config  Player.csproj  packages.config
```

> For the full file-by-file map, conventions, and known gotchas, see [CLAUDE.md](CLAUDE.md).

---

## Build

> Building is **Windows-only** (Office Interop COM references, CefSharp / LibVLC native binaries,
> .NET Framework). It cannot be built on macOS/Linux.

### Prerequisites
- Windows 10/11, 64-bit.
- **Visual Studio 2022** (Community is fine) with the **".NET desktop development"** workload, *or*
  the **Build Tools for VS 2022**.
- **.NET Framework 4.8 SDK / Targeting Pack**.
- **Microsoft Office with PowerPoint** (the project resolves Office COM references at build time).
- **`nuget.exe`** on `PATH` (classic `packages.config` restore).

### Steps
```bat
nuget restore Player.sln
msbuild Player.sln /p:Configuration=Release /t:Rebuild
```
…or in Visual Studio: open `Player.sln`, select **Release / Any CPU**, then **Build → Rebuild Solution**.

Output lands in `Player\bin\Release\` — the `.exe` plus all native deps (CEF, libvlc) and managed DLLs.

---

## Run

1. Build (above).
2. Edit `Player\bin\Release\Player.exe.config` and set at least `ContentsFolder` (must exist),
   `NodeJSHost`, `NodeJSPort`, `NodeJSProtocol`, `Room`, `Monitor`.
3. Run `Player.exe`. It shows the logo, connects to MQTT, fetches settings, shows the cover, and
   waits for the controller to start a presentation.

### Runtime shortcuts
| Key | Action |
|-----|--------|
| **ESC** | Quit the player |
| **CTRL+H** | Toggle always-on-top |
| **CTRL+G** | Open the settings + health/status panel (edits the running `Player.exe.config`) |

### Configuration keys (`Player.exe.config`)
| Key | Notes |
|-----|-------|
| `NodeJSHost` / `NodeJSPort` | NodeJS server host / port |
| `NodeJSProtocol` | `ws` or `wss` |
| `Room` / `Monitor` | Client identity (both `> 0`) |
| `ContentsFolder` | Local folder (**must exist**); `presentations/` and `log/` are created inside |
| `UseFullScreen` | Borderless full screen |
| `ScreenResolutionWidth/Height` | Borderless window of that size when full screen is off |
| `PurgePresentationData` | Delete downloaded documents on unload |
| `LogMinimumLevel` | `All` … `Critical`, `Off` |
| `AutoUpdateEnabled` | Enables the CTRL+G update controls |
| `AutoUpdateManifestUrl` | URL of the static update manifest |

### Deploy to another PC
Copy the **entire** `Player\bin\Release\` folder (not just the `.exe` — it includes CEF and libvlc
natives). The target PC needs **.NET Framework 4.8**, **Microsoft PowerPoint**, and the **Visual C++
Redistributable**. Edit `Player.exe.config` before first launch.

---

## Updating the player

The player can update itself from a plain **static server**. Update checks are **manual** from the
CTRL+G panel — the app never auto-updates at startup.

**Publishing a new version is one command** (on the Windows build machine):

```bat
update-server\build-update-package.cmd
```

It auto-increments the version, **rebuilds** the player so the shipped `.exe` carries the new
version, packages `Player/bin/Release/` into a ZIP, computes its SHA256, and updates the manifest.
Then serve the `update-server/` folder over HTTP and point each client's `AutoUpdateManifestUrl` at
`player-update.xml`.

> 📖 **Full step-by-step guide, examples, and options:** [update-server/README.md](update-server/README.md).

---

## Communication protocol

Real-time over **MQTT** (topics prefixed `rooms/{room}`); presentation data over **REST**
(`apiURI` received on connection). Client identity is `R{room}_M{monitor}`.

| Topic | Direction | Meaning |
|-------|-----------|---------|
| `rooms/{room}/init` | server→client | Initialize a presentation |
| `rooms/{room}/goto` | server→client | Go to scene / sub-scene |
| `rooms/{room}/unload` | server→client | Unload the presentation |
| `rooms/{room}/live-init` · `live-goto` · `live-unload` | server→client | Live content |
| `rooms/{room}/display-mode-start/stop` | server→client | Display mode |
| `rooms/{room}/client/{clientId}` | both | `room-init` + client status messages |

Full payload examples live in [JSON_communication_data.txt](JSON_communication_data.txt) — the
source of truth for the protocol.

---

## Security

- **Display mode = remote command execution**: an MQTT message can make the client run a process,
  open a URL, or execute a downloaded file. Powerful by design → the MQTT channel **must** be secured.
- **MQTT currently has no authentication** — anyone who knows the room can publish commands. Keep it
  on a trusted network; adding auth/TLS requires coordinating with the NodeJS server.
- **Plain-HTTP downloads** are allowed for content and updates. For real deployments use HTTPS and
  the optional SHA256 verification in the update manifest.

---

## More

[CLAUDE.md](CLAUDE.md) is the in-depth guide for developers and AI agents: full file map, session
flow, error codes, content bounds, conventions, and known gotchas.
