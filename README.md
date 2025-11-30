# Tunarr Dummy VLC Instances

Automated PowerShell scripts for managing dummy VLC streaming instances for the Tunarr media server.

## Overview

This project provides utilities to:
- Start multiple dummy VLC instances for testing/streaming
- Kill all VLC processes
- Execute commands via Stream Deck integration

## Scripts

### `tunarr-dummy-start.ps1`
Launches multiple VLC dummy instances configured to stream from a Tunarr server.

**Configuration:**
- `$CMAX`: Number of channels (default: 6)
- `$TIMEWAIT`: Initial delay before starting instances (default: 300s for normal, 2s for Stream Deck)
- `$WAITFORIT`: Delay between instance launches (default: 500ms)
- `$baseUrl`: Server URL (default: http://10.0.0.174:8000/stream/channels)

### `tunarr-dummy-kill.ps1`
Terminates all running VLC media player processes.

### `tunarr-dummy-streamdeck-execute.ps1`
Stream Deck integration variant with reduced startup delay (2 seconds).

## Requirements

- Windows PowerShell 5.1+
- VLC media player installed at `C:\Program Files\VideoLAN\VLC\vlc.exe`
- Access to Tunarr server

## Usage

```powershell
# Start dummy instances
.\tunarr-dummy-start.ps1

# Kill all VLC processes
.\tunarr-dummy-kill.ps1

# Stream Deck integration
.\tunarr-dummy-streamdeck-execute.ps1
```

## License

MIT
