
$vlcPath = "C:\Program Files\VideoLAN\VLC\vlc.exe"
$baseUrl = "http://10.0.0.174:8000/stream/channels"
$commonArgs = @("-I", "dummy", "--dummy-quiet", "--vout", "none", "--no-audio")


$CMAX = 6 # Number of channels
$WAITFORIT = 500  # Delay in milliseconds between dummy instance launches
$TIMEWAIT = 2  # Initial wait time in seconds before starting dummy instances

Write-Host "Waiting for $TIMEWAIT seconds before starting dummy VLC instances..."
Start-Sleep -Seconds $TIMEWAIT  # Initial wait before starting dummy instances

# Launching scripted VLC dummy instances
for ($i = 1; $i -le $CMAX; $i++) {
    $channelUrl = "$baseUrl/$i.m3u8"
    $args = @($channelUrl) + $commonArgs # ignore error 
    Start-Process -FilePath $vlcPath -ArgumentList $args
    Write-Host "Started VLC for channel $i"
    Start-Sleep -Milliseconds $WAITFORIT 
}