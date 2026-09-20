# Tunarr Dummy Start (C# WinForms + FFmpeg)
# Tunarr Dummy Start

Dummy launcher that keeps persistent FFmpeg clients connected to Tunarr.
Cross-platform dummy launcher and daemon that keeps persistent FFmpeg clients connected to Tunarr.

It is intentionally lightweight and does not produce real video output. The app continuously consumes stream URLs to keep Tunarr encoding alive when no real clients are present.

Supported on **Linux ARM64** (Raspberry Pi 4/5, Orange Pi, ARM servers), **Linux ARMv7**, **Linux x64**, and **Windows** (WinForms GUI & Headless). Includes an embedded responsive **Web Dashboard & REST API** and an Android companion app.

---

## Features

- Simple GUI config
- Real-time GUI log with timestamps
- Detailed run log (connect attempts, ffmpeg process start, connection established, disconnect summary)
- FFmpeg keep-alive client mode (no output file)
- Auto fallback: reconnect retry per channel, then skip and continue
- Stop button with safe cancellation
- Windows Registry persistence for app settings
- Auto start keep-alive at app launch (optional)
- Start app with Windows login (optional)
- Tray-first behavior: minimize/close sends app to tray; exit via tray menu `Quit`
- Basic custom app icon
- **Cross-Platform:** Runs as a native desktop GUI on Windows and as a headless background daemon on Linux ARM64, Linux ARMv7, and Linux x64.
- **Embedded Web Dashboard:** Responsive web control panel accessible at `http://<ip>:1290/` from any browser (phone, tablet, PC).
- **REST API:** Control and monitor status remotely via HTTP/HTTPS endpoints or the companion Android app.
- **Hardware Acceleration:** Auto-detects and supports Linux ARM (`v4l2m2m`, `vaapi`, `drm`, `rkmpp`), NVIDIA (`cuda`), Intel (`qsv`), and Windows (`d3d11va`, `dxva2`).
- **Real-Time Live Logs:** Real-time log streaming with timestamps in the terminal, GUI, and Web UI.
- **Persistent Keep-Alive:** Continuous consumption of Tunarr stream URLs (`{BaseUrl}/{channel}.m3u8`).
- **Auto Fallback & Retries:** Reconnect retry per channel, then skip and continue.
- **Portable JSON Configuration:** Saves to `config.json` with automatic migration from Windows Registry.
- **Docker & systemd Ready:** Pre-configured Dockerfile and systemd service unit.
- **Graceful Shutdown:** Safe cancellation on `Ctrl+C` / `SIGTERM` / `SIGINT` terminating child FFmpeg processes.

---

## Requirements

- Windows
- .NET 8 SDK
- `ffmpeg.exe` available in `PATH` or configured in the GUI
- Access to Tunarr server
### Linux (ARM64 / ARM / x64)
- `ffmpeg` installed via package manager (`sudo apt install ffmpeg` on Debian/Ubuntu/Raspberry Pi OS)
- Optional: .NET 8 runtime (not needed if using the self-contained single-file release binary)

## Build And Run
### Windows
- Windows 10 / 11 / Server
- `ffmpeg.exe` in `PATH` or configured in settings

```powershell
cd .\TunarrDummyStart
dotnet build
dotnet run
---

## Quick Start (Linux ARM / x64)

### 1. Download & Run Standalone Binary
Download the pre-compiled single-file executable for your architecture from the [GitHub Releases](https://github.com/NoID1290/tunarr-dummy-start/releases):

```bash
# Make executable
chmod +x TunarrDummyStart-linux-arm64

# Run with automatic keep-alive start
./TunarrDummyStart-linux-arm64 --start
```

## GUI Configuration
Open your browser at `http://<device-ip>:1290` to access the Control Panel.

- `Tunarr Base Channels URL`: Base path such as `http://127.0.0.1:8000/stream/channels`
- `Channel Count`: Number of channels to keep connected (`1..N`)
- `Startup Delay (seconds)`: Delay before beginning any probes
- `Stagger Delay (ms)`: Delay between channels
- `Retry Count Per Channel`: Number of reconnect retries before skipping a failed channel
- `FFmpeg Path (opt.)`: Absolute path to `ffmpeg.exe` (optional if in `PATH`)
- `Auto Start At Launch`: Automatically starts keep-alive when app opens
- `Start With Windows Login`: Registers app in current-user startup registry
### 2. Command-Line Options
```bash
./TunarrDummyStart [options]

## Tray Behavior
Options:
  -c, --config <path>    Custom path to config.json file
  -p, --port <number>    Override web server port (default: 1290)
  --url <url>            Override Tunarr base channels URL
  -s, --start            Start keep-alive immediately on launch
  --daemon, --headless   Run in headless daemon mode
  -v, --version          Show version information
  -h, --help             Show help information
```

- Minimize and close both keep the app running in the system tray
- Double-click tray icon or use tray menu `Open` to restore window
- Use tray menu `Quit` for a full app shutdown
### 3. Run with Docker
```bash
# Build multi-arch image or run directly:
docker build -t tunarr-dummy-start .

For each channel, the app keeps a client connected to this URL:
docker run -d \
  --name tunarr-dummy \
  --restart unless-stopped \
  -p 1290:1290 \
  -v /path/to/config:/config \
  tunarr-dummy-start
```

`{BaseUrl}/{channel}.m3u8`
### 4. Run as a systemd Service (Linux ARM)
Copy the included unit file:
```bash
sudo cp tunarr-dummy-start.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now tunarr-dummy-start
```

Example for channel 3:
---

`http://127.0.0.1:8000/stream/channels/3.m3u8`
## Windows Quick Start

## Fallback Behavior
### Build & Run GUI:
```powershell
cd .\TunarrDummyStart
dotnet build -f net8.0-windows
dotnet run -f net8.0-windows
```

If a channel disconnects or fails to connect:
### Run in Headless / Daemon Mode on Windows:
```powershell
dotnet run -f net8.0 -- --start
```

1. Retry the same URL up to `Retry Count Per Channel`
2. If still failing, log `skipped after retry limit`
3. Continue with the next channel
---

## Config Storage
## Configuration

Settings are saved and loaded from:
Settings are saved in `config.json` at:
- **Linux:** `~/.config/TunarrDummyStart/config.json` (or specified via `-c /path/to/config.json` or `TUNARR_CONFIG_PATH`)
- **Windows:** `%APPDATA%\TunarrDummyStart\config.json` (automatically imported from Windows Registry if upgrading)

`HKEY_CURRENT_USER\Software\NoID Softwork\TunarrDummyStart`
### Key Settings:
- `Tunarr Base Channels URL`: e.g. `http://127.0.0.1:8000/stream/channels`
- `Channel Count`: Number of channels to keep connected (`1..N`)
- `Startup Delay (seconds)`: Delay before beginning stream probes
- `Stagger Delay (ms)`: Delay between launching each channel's worker
- `Retry Count Per Channel`: Reconnect retries before skipping a failed channel
- `Hardware Acceleration`: `Auto`, `v4l2m2m` (Raspberry Pi/ARM), `vaapi`, `drm`, `rkmpp`, `cuda`, `qsv`, `None`
- `Web Server Port`: Port for the web control panel and REST API (default: `1290`)
- `Web Server Password`: Optional password (enables HTTPS if set)

The key is created on first run if it does not exist.
---

## Notes
## REST API Endpoints

- This is a dummy starter to prevent Tunarr from stopping encoding when no real clients are connected.
- No video playback UI or output file generation is produced.
| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/` | Responsive Web Dashboard UI |
| `GET` | `/api/status` | Current runner status and channel metrics |
| `GET` | `/api/log` | Live log output |
| `GET` | `/api/config` | Get current configuration |
| `POST`| `/api/config` | Update configuration |
| `POST`| `/api/start` | Start keep-alive runner |
| `POST`| `/api/stop` | Stop keep-alive runner |
| `GET` | `/api/tunarr/status` | Check Tunarr service/process status |
| `POST`| `/api/tunarr/start` | Start Tunarr service/process |
| `POST`| `/api/tunarr/stop` | Stop Tunarr service/process |
| `POST`| `/api/tunarr/restart`| Restart Tunarr service/process |
| `POST`| `/api/pc/restart` | Reboot host machine |
| `POST`| `/api/pc/shutdown`| Shutdown host machine |

---

## License

MIT
