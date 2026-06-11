# Static update server

This folder is a minimal static-server layout for player auto-update tests.

The version in `player-update.xml` is compared with `MainForm.APP_VERSION` in
`Player/MainForm.cs`, not with the Windows file version metadata of `Player.exe`.

1. Build the player in Release.
2. Run the packaging script from this folder:

```bat
build-update-package.cmd 1.0.1
```

The script zips the complete `Player/bin/Release/` folder contents as
`Player-<version>.zip`, calculates SHA256, and updates `player-update.xml`.
If the repo drive has little free space, pass a different static-server output folder:

```bat
build-update-package.cmd 1.0.1 D:\ContentDistribution-player-update-server
```

3. Serve this folder, for example:

```bat
python -m http.server 8080
```

The player setting `AutoUpdateManifestUrl` can then point to:

```text
http://localhost:8080/player-update.xml
```

Open the player control panel with `CTRL+G` and press `Check for updates`.
If the manifest version is newer than `MainForm.APP_VERSION`, the player stages the
update under `ContentsFolder/updates/<version>/`, writes `install-update.cmd`, and
asks whether to install it. If confirmed, it launches the script and exits. The script
waits for the current player PID to exit, copies the staged package over the running
app folder, and starts `Player.exe` again.
