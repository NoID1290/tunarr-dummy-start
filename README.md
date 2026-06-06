# Tunarr Dummy Start (C# WinForms + FFmpeg)

This project is a Windows Forms dummy launcher that keeps persistent FFmpeg clients connected to Tunarr.

It is intentionally lightweight and does not produce real video output. The app continuously consumes stream URLs to keep Tunarr encoding alive when no real clients are present.

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

## Requirements

- Windows
- .NET 8 SDK
- `ffmpeg.exe` available in `PATH` or configured in the GUI
- Access to Tunarr server

## Build And Run

```powershell
cd .\TunarrDummyStart
dotnet build
dotnet run
```

## GUI Configuration

- `Tunarr Base Channels URL`: Base path such as `http://127.0.0.1:8000/stream/channels`
- `Channel Count`: Number of channels to keep connected (`1..N`)
- `Startup Delay (seconds)`: Delay before beginning any probes
- `Stagger Delay (ms)`: Delay between channels
- `Retry Count Per Channel`: Number of reconnect retries before skipping a failed channel
- `FFmpeg Path (opt.)`: Absolute path to `ffmpeg.exe` (optional if in `PATH`)
- `Auto Start At Launch`: Automatically starts keep-alive when app opens
- `Start With Windows Login`: Registers app in current-user startup registry

## Tray Behavior

- Minimize and close both keep the app running in the system tray
- Double-click tray icon or use tray menu `Open` to restore window
- Use tray menu `Quit` for a full app shutdown

For each channel, the app keeps a client connected to this URL:

`{BaseUrl}/{channel}.m3u8`

Example for channel 3:

`http://127.0.0.1:8000/stream/channels/3.m3u8`

## Fallback Behavior

If a channel disconnects or fails to connect:

1. Retry the same URL up to `Retry Count Per Channel`
2. If still failing, log `skipped after retry limit`
3. Continue with the next channel

## Config Storage

Settings are saved and loaded from:

`HKEY_CURRENT_USER\Software\NoID Softwork\TunarrDummyStart`

The key is created on first run if it does not exist.

## Notes

- This is a dummy starter to prevent Tunarr from stopping encoding when no real clients are connected.
- No video playback UI or output file generation is produced.

## License

MIT
