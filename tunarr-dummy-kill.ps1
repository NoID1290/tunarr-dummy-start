# Kill VLC media player process
Get-Process | Where-Object { $_.ProcessName -like "*vlc*" } | Stop-Process -Force