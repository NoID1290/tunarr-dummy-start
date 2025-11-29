# Start VLC with dummy interface for multiple channels
$vlcPath = "C:\Program Files\VideoLAN\VLC\vlc.exe"
$baseUrl = "http://10.0.0.174:8000/stream/channels"
$commonArgs = @("-I", "dummy", "--dummy-quiet", "--vout", "none")

# Start VLC instances for channels 1-6
for ($i = 1; $i -le 6; $i++) {
    $channelUrl = "$baseUrl/$i.m3u8"
    $args = @($channelUrl) + $commonArgs
    Start-Process -FilePath $vlcPath -ArgumentList $args
    Write-Host "Started VLC for channel $i"
    Start-Sleep -Milliseconds 500  # Slight delay between launches
}