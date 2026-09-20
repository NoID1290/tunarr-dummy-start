using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TunarrDummyStart
{
    public sealed class WebserverService : IDisposable
    {
        private readonly Func<AppConfig> _getConfig;
        private readonly Action<AppConfig> _saveConfig;
        private readonly Func<List<ChannelStatusUpdate>> _getChannelStatuses;
        private readonly Func<List<string>> _getLogs;
        private readonly Action _startRunner;
        private readonly Action _stopRunner;
        private readonly Func<bool> _isRunnerRunning;
        private readonly Action<string> _logMessage;

        private readonly Action _startTunarr;
        private readonly Action _stopTunarr;
        private readonly Action _restartTunarr;
        private readonly Func<bool> _isTunarrRunning;
        private readonly Action _restartPcServer;
        private readonly Action _closePcServer;
        private readonly Action _restartComputer;
        private readonly Action _shutdownComputer;

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private int _port;
        private bool _isRunning;
        private X509Certificate2? _serverCertificate;
        private DateTime _lastSslErrorTime = DateTime.MinValue;

        public bool IsRunning => _isRunning;
        public int Port => _port;

        public WebserverService(
            Func<AppConfig> getConfig,
            Action<AppConfig> saveConfig,
            Func<List<ChannelStatusUpdate>> getChannelStatuses,
            Func<List<string>> getLogs,
            Action startRunner,
            Action stopRunner,
            Func<bool> isRunnerRunning,
            Action<string> logMessage,
            Action startTunarr,
            Action stopTunarr,
            Action restartTunarr,
            Func<bool> isTunarrRunning,
            Action restartPcServer,
            Action closePcServer,
            Action restartComputer,
            Action shutdownComputer)
        {
            _getConfig = getConfig;
            _saveConfig = saveConfig;
            _getChannelStatuses = getChannelStatuses;
            _getLogs = getLogs;
            _startRunner = startRunner;
            _stopRunner = stopRunner;
            _isRunnerRunning = isRunnerRunning;
            _logMessage = logMessage;

            _startTunarr = startTunarr;
            _stopTunarr = stopTunarr;
            _restartTunarr = restartTunarr;
            _isTunarrRunning = isTunarrRunning;
            _restartPcServer = restartPcServer;
            _closePcServer = closePcServer;
            _restartComputer = restartComputer;
            _shutdownComputer = shutdownComputer;
        }

        private X509Certificate2 GetOrCreateCertificate()
        {
            if (_serverCertificate != null) return _serverCertificate;

            using (RSA rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    "CN=localhost",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, false));

                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                        false));

                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                        false));

                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddDnsName("localhost");
                sanBuilder.AddIpAddress(IPAddress.Loopback);
                sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
                sanBuilder.AddIpAddress(IPAddress.Any);
                request.CertificateExtensions.Add(sanBuilder.Build());

                var certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddYears(10));

                byte[] pfx = certificate.Export(X509ContentType.Pfx, "password");
                _serverCertificate = new X509Certificate2(pfx, "password", X509KeyStorageFlags.Exportable);
                return _serverCertificate;
            }
        }

        public void Start(int port)
        {
            if (_isRunning)
            {
                if (_port == port) return;
                Stop();
            }

            _port = port;
            _cts = new CancellationTokenSource();

            try
            {
                // TcpListener bound to IPAddress.Any (0.0.0.0) allows remote connections 
                // on non-restricted ports (>1024) without requiring Administrator permissions.
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _isRunning = true;
                var config = _getConfig();
                bool useSsl = !string.IsNullOrEmpty(config.WebserverPassword);
                string proto = useSsl ? "https" : "http";
                _logMessage($"Web server started remotely at {proto}://*:{port}/ (Password protection: {(useSsl ? "Enabled" : "Disabled")})");
                Task.Run(() => ListenLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                _logMessage($"Web server could not be started: {ex.Message}");
                _listener = null;
                _isRunning = false;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            try
            {
                _listener?.Stop();
            }
            catch { }
            _listener = null;
            _logMessage("Web server stopped.");
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleClientAsync(client), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    _logMessage($"Web server loop exception: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                Stream stream = client.GetStream();
                SslStream? sslStream = null;
                try
                {
                    var config = _getConfig();
                    bool useSsl = !string.IsNullOrEmpty(config.WebserverPassword);

                    if (useSsl)
                    {
                        var cert = GetOrCreateCertificate();
                        sslStream = new SslStream(stream, false);
                        try
                        {
                            await sslStream.AuthenticateAsServerAsync(cert);
                            stream = sslStream;
                        }
                        catch (Exception ex)
                        {
                            if ((DateTime.UtcNow - _lastSslErrorTime).TotalSeconds >= 10)
                            {
                                _logMessage($"SSL handshake failed (did you connect via HTTP instead of HTTPS?): {ex.Message}. Suppressing identical errors for 10s.");
                                _logMessage($"SSL handshake failed: {ex.Message}. Suppressing identical errors for 10s.");
                                _lastSslErrorTime = DateTime.UtcNow;
                            }
                            return;
                        }
                    }

                    try
                    {
                        stream.ReadTimeout = 5000;
                        stream.WriteTimeout = 5000;
                    }
                    catch { }

                    // Read request header
                    var headerBuffer = new List<byte>();
                    var leftoverBodyBuffer = new List<byte>();
                    byte[] buffer = new byte[4096];
                    bool headerFound = false;

                    while (!headerFound)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead <= 0) break;
                        

                        for (int i = 0; i < bytesRead; i++)
                        {
                            if (!headerFound)
                            {
                                headerBuffer.Add(buffer[i]);

                                // Check for ending sequence \r\n\r\n
                                if (headerBuffer.Count >= 4 &&
                                    headerBuffer[headerBuffer.Count - 4] == 13 && // \r
                                    headerBuffer[headerBuffer.Count - 3] == 10 && // \n
                                    headerBuffer[headerBuffer.Count - 2] == 13 && // \r
                                    headerBuffer[headerBuffer.Count - 1] == 10)   // \n
                                    headerBuffer[headerBuffer.Count - 4] == 13 &&
                                    headerBuffer[headerBuffer.Count - 3] == 10 &&
                                    headerBuffer[headerBuffer.Count - 2] == 13 &&
                                    headerBuffer[headerBuffer.Count - 1] == 10)
                                {
                                    headerFound = true;
                                }
                            }
                            else
                            {
                                leftoverBodyBuffer.Add(buffer[i]);
                            }
                        }

                        if (headerBuffer.Count > 8192) // Limit header size
                        if (headerBuffer.Count > 8192)
                        {
                            await SendErrorResponseAsync(stream, 400, "Bad Request (Header too large)");
                            return;
                        }
                    }

                    if (!headerFound || headerBuffer.Count == 0) return;

                    string headerText = Encoding.UTF8.GetString(headerBuffer.ToArray());
                    string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;

                    string requestLine = lines[0];
                    string[] requestParts = requestLine.Split(' ');
                    if (requestParts.Length < 2)
                    {
                        await SendErrorResponseAsync(stream, 400, "Bad Request");
                        return;
                    }

                    string method = requestParts[0].ToUpper();
                    string rawUrl = requestParts[1];

                    // Strip query strings
                    int queryIdx = rawUrl.IndexOf('?');
                    string path = queryIdx >= 0 ? rawUrl.Substring(0, queryIdx) : rawUrl;

                    // Extract Content-Length
                    int contentLength = 0;
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(line.Substring(15).Trim(), out contentLength);
                        }
                    }

                    // Read body if content is posted
                    string body = string.Empty;
                    if (contentLength > 0 && contentLength < 1024 * 1024) // 1MB maximum
                    if (contentLength > 0 && contentLength < 1024 * 1024)
                    {
                        byte[] bodyBytes = new byte[contentLength];
                        int totalRead = 0;
                        
                        // Copy leftovers first

                        int leftoversToCopy = Math.Min(leftoverBodyBuffer.Count, contentLength);
                        if (leftoversToCopy > 0)
                        {
                            leftoverBodyBuffer.CopyTo(0, bodyBytes, 0, leftoversToCopy);
                            totalRead += leftoversToCopy;
                        }

                        while (totalRead < contentLength)
                        {
                            int read = await stream.ReadAsync(bodyBytes, totalRead, contentLength - totalRead);
                            if (read <= 0) break;
                            totalRead += read;
                        }
                        body = Encoding.UTF8.GetString(bodyBytes);
                    }

                    // Route mapping
                    if (method == "OPTIONS")
                    {
                        await SendCorsOkAsync(stream);
                        return;
                    }

                    // Authentication check (except OPTIONS)
                    bool isAuthenticated = false;
                    if (string.IsNullOrEmpty(config.WebserverPassword))
                    {
                        isAuthenticated = true;
                    }
                    else
                    {
                        string? authHeader = null;
                        foreach (string line in lines)
                        {
                            if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                            {
                                authHeader = line.Substring(14).Trim();
                                break;
                            }
                        }

                        if (authHeader != null && authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                string base64 = authHeader.Substring(6).Trim();
                                string credentials = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                                int colonIdx = credentials.IndexOf(':');
                                if (colonIdx >= 0)
                                {
                                    string password = credentials.Substring(colonIdx + 1);
                                    if (password == config.WebserverPassword)
                                    {
                                        isAuthenticated = true;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (!isAuthenticated)
                    {
                        await SendUnauthorizedResponseAsync(stream);
                        return;
                    }

                    // Routes
                    if (method == "GET" && path == "/")
                    {
                        await SendResponseAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(DashboardHtml));
                        return;
                    }

                    if (method == "GET" && path == "/api/status")
                    {
                        var status = new
                        {
                            isRunning = _isRunnerRunning(),
                            channels = _getChannelStatuses()
                        };
                        string json = JsonSerializer.Serialize(status, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "GET" && path == "/api/log")
                    {
                        string logText = string.Join("\n", _getLogs());
                        await SendResponseAsync(stream, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(logText));
                        return;
                    }

                    if (method == "GET" && path == "/api/config")
                    {
                        string json = JsonSerializer.Serialize(_getConfig(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "POST" && path == "/api/config")
                    {
                        try
                        {
                            var newConfig = JsonSerializer.Deserialize<AppConfig>(body, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                            if (newConfig != null)
                            {
                                if (string.IsNullOrWhiteSpace(newConfig.BaseUrl) || !Uri.TryCreate(newConfig.BaseUrl, UriKind.Absolute, out _))
                                {
                                    string errorJson = JsonSerializer.Serialize(new { success = false, error = "Invalid Base URL" });
                                    await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(errorJson));
                                    return;
                                }

                                _saveConfig(newConfig);
                                string okJson = JsonSerializer.Serialize(new { success = true });
                                await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(okJson));
                            }
                            else
                            {
                                string errorJson = JsonSerializer.Serialize(new { success = false, error = "Invalid configuration data" });
                                await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(errorJson));
                            }
                        }
                        catch (Exception ex)
                        {
                            string errorJson = JsonSerializer.Serialize(new { success = false, error = ex.Message });
                            await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(errorJson));
                        }
                        return;
                    }

                    if (method == "GET" && path == "/api/hwaccel/detect")
                    {
                        var ffmpegService = new FfmpegService();
                        string? ffmpegExe = ffmpegService.ResolveExecutable(_getConfig().FfmpegPath);
                        List<string> backends = new();
                        if (ffmpegExe != null)
                        {
                            backends = await ffmpegService.DetectHwAccelsAsync(ffmpegExe);
                        }
                        var detectedGpus = FfmpegService.DetectHardwareGpus();
                        string recommended = FfmpegService.ResolveHwAccel("Auto", backends, detectedGpus);

                        _logMessage(backends.Count > 0
                            ? $"HW Accel probe: found {string.Join(", ", backends)} (Recommended: {recommended})"
                            : "HW Accel probe: no hardware backends found.");

                        var resObj = new
                        {
                            backends = backends,
                            gpus = detectedGpus,
                            recommended = recommended,
                            ffmpegFound = ffmpegExe != null
                        };
                        string json = JsonSerializer.Serialize(resObj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "GET" && path == "/api/ffmpeg/check")
                    {
                        var ffmpegService = new FfmpegService();
                        string? ffmpegExe = ffmpegService.ResolveExecutable(_getConfig().FfmpegPath);
                        bool exists = ffmpegExe != null && File.Exists(ffmpegExe);
                        string versionStr = "";
                        if (exists)
                        {
                            try
                            {
                                var psi = new ProcessStartInfo
                                {
                                    FileName = ffmpegExe!,
                                    Arguments = "-version",
                                    RedirectStandardOutput = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };
                                using var proc = Process.Start(psi);
                                if (proc != null)
                                {
                                    string output = await proc.StandardOutput.ReadToEndAsync();
                                    await proc.WaitForExitAsync();
                                    string firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                                    versionStr = firstLine.Trim();
                                }
                            }
                            catch { }
                        }

                        var resObj = new
                        {
                            resolved = exists,
                            path = ffmpegExe ?? "",
                            version = versionStr
                        };
                        string json = JsonSerializer.Serialize(resObj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "POST" && path == "/api/start")
                    {
                        _startRunner();
                        string json = JsonSerializer.Serialize(new { success = true });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "POST" && path == "/api/stop")
                    {
                        _stopRunner();
                        string json = JsonSerializer.Serialize(new { success = true });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "POST" && path == "/api/tunarr/start")
                    {
                        _startTunarr();
                        string json = JsonSerializer.Serialize(new { success = true });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "POST" && path == "/api/tunarr/stop")
                    {
                        _stopTunarr();
                        string json = JsonSerializer.Serialize(new { success = true });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "POST" && path == "/api/tunarr/restart")
                    {
                        _restartTunarr();
                        string json = JsonSerializer.Serialize(new { success = true });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "GET" && path == "/api/tunarr/status")
                    {
                        var status = new { isRunning = _isTunarrRunning() };
                        string json = JsonSerializer.Serialize(status, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "POST" && path == "/api/pcserver/restart")
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500);
                            _restartPcServer();
                        });
                        string json = JsonSerializer.Serialize(new { success = true });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "POST" && path == "/api/pcserver/close")
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500);
                            _closePcServer();
                        });
                        string json = JsonSerializer.Serialize(new { success = true });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "POST" && path == "/api/pc/restart")
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500);
                            _restartComputer();
                        });
                        string json = JsonSerializer.Serialize(new { success = true });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    if (method == "POST" && path == "/api/pc/shutdown")
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500);
                            _shutdownComputer();
                        });
                        string json = JsonSerializer.Serialize(new { success = true });
                        await SendResponseAsync(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                        return;
                    }

                    await SendErrorResponseAsync(stream, 404, "Not Found");
                }
                catch (Exception ex)
                {
                    _logMessage($"Web server exception: {ex.Message}");
                    try
                    {
                        await SendErrorResponseAsync(stream, 500, $"Internal Server Error: {ex.Message}");
                    }
                    catch { }
                }
                finally
                {
                    sslStream?.Dispose();
                }
            }
        }

        private async Task SendResponseAsync(Stream stream, int statusCode, string contentType, byte[] bodyBytes)
        {
            var headerSb = new StringBuilder();
            headerSb.Append($"HTTP/1.1 {statusCode} {GetStatusCodePhrase(statusCode)}\r\n");
            headerSb.Append($"Content-Type: {contentType}\r\n");
            headerSb.Append($"Content-Length: {bodyBytes.Length}\r\n");
            headerSb.Append("Access-Control-Allow-Origin: *\r\n");
            headerSb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            headerSb.Append("Access-Control-Allow-Headers: Content-Type, Authorization\r\n");
            headerSb.Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.UTF8.GetBytes(headerSb.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            if (bodyBytes.Length > 0)
            {
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
            }
            await stream.FlushAsync();
        }

        private async Task SendErrorResponseAsync(Stream stream, int statusCode, string message)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(message);
            await SendResponseAsync(stream, statusCode, "text/plain; charset=utf-8", bodyBytes);
        }

        private async Task SendCorsOkAsync(Stream stream)
        {
            await SendResponseAsync(stream, 200, "text/plain", Array.Empty<byte>());
        }

        private async Task SendUnauthorizedResponseAsync(Stream stream)
        {
            var headerSb = new StringBuilder();
            headerSb.Append("HTTP/1.1 401 Unauthorized\r\n");
            headerSb.Append("WWW-Authenticate: Basic realm=\"TunarrDummyStart\"\r\n");
            headerSb.Append("Content-Length: 12\r\n");
            headerSb.Append("Content-Type: text/plain; charset=utf-8\r\n");
            headerSb.Append("Access-Control-Allow-Origin: *\r\n");
            headerSb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            headerSb.Append("Access-Control-Allow-Headers: Content-Type, Authorization\r\n");
            headerSb.Append("Connection: close\r\n\r\n");
            headerSb.Append("Unauthorized");
            byte[] bytes = Encoding.UTF8.GetBytes(headerSb.ToString());
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        private static string GetStatusCodePhrase(int code) => code switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "Unknown"
        };

        public void Dispose()
        {
            Stop();
        }

        private const string DashboardHtml = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Tunarr Dummy Starter — Control Panel</title>
    <link href="https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;600;700&display=swap" rel="stylesheet">
    <link href="https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;500;600;700&display=swap" rel="stylesheet">
    <style>
        :root {
            --bg-color: #0b0f19;
            --panel-bg: rgba(17, 24, 39, 0.7);
            --panel-bg: rgba(17, 24, 39, 0.75);
            --panel-bg-elevated: rgba(26, 36, 56, 0.85);
            --border-color: rgba(255, 255, 255, 0.08);
            --border-focus: rgba(59, 130, 246, 0.5);
            --accent-color: #3b82f6;
            --accent-glow: rgba(59, 130, 246, 0.5);
            --accent-glow: rgba(59, 130, 246, 0.4);
            --text-color: #f3f4f6;
            --text-muted: #9ca3af;
            --input-bg: rgba(15, 23, 42, 0.8);
            
            --state-connected-bg: rgba(16, 185, 129, 0.15);
            --state-connected-accent: #10b981;
            --state-connecting-bg: rgba(59, 130, 246, 0.15);
            --state-connecting-accent: #3b82f6;
            --state-retrying-bg: rgba(245, 158, 11, 0.15);
            --state-retrying-accent: #f59e0b;
            --state-failed-bg: rgba(239, 68, 68, 0.15);
            --state-failed-accent: #ef4444;
            --state-idle-bg: rgba(107, 114, 128, 0.15);
            --state-idle-accent: #9ca3af;
            --state-disabled-bg: rgba(75, 75, 75, 0.1);
            --state-disabled-accent: #5e6675;
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        body {
            font-family: 'Outfit', -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
            background: radial-gradient(circle at top right, #1e293b, var(--bg-color));
            color: var(--text-color);
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            overflow-x: hidden;
            padding-bottom: 2rem;
        }

        header {
            background: rgba(15, 23, 42, 0.6);
            background: rgba(15, 23, 42, 0.7);
            backdrop-filter: blur(12px);
            border-bottom: 1px solid var(--border-color);
            padding: 1.25rem 2rem;
            padding: 1rem 2rem;
            display: flex;
            justify-content: space-between;
            align-items: center;
            position: sticky;
            top: 0;
            z-index: 100;
        }

        .logo-section {
            display: flex;
            align-items: center;
            gap: 0.75rem;
        }

        .logo-indicator {
            width: 12px;
            height: 12px;
            border-radius: 50%;
            background-color: var(--state-idle-accent);
            box-shadow: 0 0 8px var(--state-idle-accent);
            transition: all 0.5s ease;
        }

        .logo-indicator.active {
            background-color: var(--state-connected-accent);
            box-shadow: 0 0 12px var(--state-connected-accent);
            animation: pulse 2s infinite;
        }

        @keyframes pulse {
            0% { transform: scale(1); opacity: 1; }
            50% { transform: scale(1.2); opacity: 0.7; }
            100% { transform: scale(1); opacity: 1; }
        }

        .logo-title {
            font-size: 1.25rem;
            font-weight: 700;
            letter-spacing: -0.5px;
            background: linear-gradient(135deg, #60a5fa, #3b82f6);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
        }

        .header-meta {
            display: flex;
            align-items: center;
            gap: 1.25rem;
        }

        .container {
            max-width: 1200px;
            max-width: 1280px;
            width: 100%;
            margin: 2rem auto;
            margin: 1.5rem auto;
            padding: 0 1.5rem;
            display: flex;
            flex-direction: column;
            gap: 2rem;
            gap: 1.5rem;
            flex-grow: 1;
        }

        .dashboard-grid {
            display: grid;
            grid-template-columns: 1fr;
            gap: 2rem;
            gap: 1.5rem;
        }

        @media (min-width: 1024px) {
            .dashboard-grid {
                grid-template-columns: 2fr 1fr;
                grid-template-columns: 1.6fr 1.4fr;
            }
        }

        .card {
            background: var(--panel-bg);
            backdrop-filter: blur(8px);
            border: 1px solid var(--border-color);
            border-radius: 16px;
            padding: 1.5rem;
            box-shadow: 0 10px 30px -10px rgba(0,0,0,0.5);
            transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
            transition: all 0.25s ease;
        }

        .card:hover {
            border-color: rgba(255,255,255,0.15);
            box-shadow: 0 15px 35px -5px rgba(0,0,0,0.6);
            border-color: rgba(255,255,255,0.12);
        }

        .card-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 1.5rem;
            margin-bottom: 1.25rem;
            border-bottom: 1px solid var(--border-color);
            padding-bottom: 0.75rem;
        }

        .card-title {
            font-size: 1.1rem;
            font-weight: 600;
            color: var(--text-color);
            display: flex;
            align-items: center;
            gap: 0.5rem;
        }

        /* Controls */
        .card-title svg {
            width: 18px;
            height: 18px;
            fill: currentColor;
            opacity: 0.8;
        }

        .section-subhead {
            font-size: 0.8rem;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            color: #60a5fa;
            margin: 1.25rem 0 0.75rem 0;
            padding-bottom: 0.35rem;
            border-bottom: 1px dashed rgba(255,255,255,0.08);
        }

        .controls-row {
            display: flex;
            gap: 1rem;
            gap: 0.75rem;
            flex-wrap: wrap;
        }

        .btn {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: 0.5rem;
            padding: 0.75rem 1.5rem;
            border-radius: 10px;
            padding: 0.65rem 1.25rem;
            border-radius: 8px;
            font-family: inherit;
            font-size: 0.95rem;
            font-size: 0.9rem;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s ease;
            border: 1px solid transparent;
            text-decoration: none;
        }

        .btn-primary {
            background: #2563eb;
            color: white;
            box-shadow: 0 4px 14px rgba(37, 99, 235, 0.4);
            box-shadow: 0 4px 14px rgba(37, 99, 235, 0.35);
        }

        .btn-primary:hover {
        .btn-primary:hover:not(:disabled) {
            background: #1d4ed8;
            transform: translateY(-1px);
        }

        .btn-danger {
            background: #dc2626;
            color: white;
            box-shadow: 0 4px 14px rgba(220, 38, 38, 0.4);
            box-shadow: 0 4px 14px rgba(220, 38, 38, 0.35);
        }

        .btn-danger:hover {
        .btn-danger:hover:not(:disabled) {
            background: #b91c1c;
            transform: translateY(-1px);
        }

        .btn-warning {
            background: #d97706;
            color: white;
        }

        .btn-warning:hover:not(:disabled) {
            background: #b45309;
        }

        .btn-secondary {
            background: rgba(255,255,255,0.05);
            background: rgba(255,255,255,0.06);
            color: var(--text-color);
            border-color: var(--border-color);
        }

        .btn-secondary:hover {
            background: rgba(255,255,255,0.1);
        .btn-secondary:hover:not(:disabled) {
            background: rgba(255,255,255,0.12);
        }

        .btn-sm {
            padding: 0.4rem 0.75rem;
            font-size: 0.8rem;
            border-radius: 6px;
        }

        .btn:disabled {
            opacity: 0.5;
            opacity: 0.45;
            cursor: not-allowed;
            transform: none !important;
            box-shadow: none !important;
        }

        /* Form elements */
        .form-group {
            display: flex;
            flex-direction: column;
            gap: 0.4rem;
            margin-bottom: 0.9rem;
        }

        .form-row {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 0.9rem;
        }

        .form-row-3 {
            display: grid;
            grid-template-columns: 1fr 1fr 1fr;
            gap: 0.9rem;
        }

        label {
            font-size: 0.825rem;
            font-weight: 500;
            color: var(--text-muted);
        }

        input[type="text"],
        input[type="number"],
        input[type="password"],
        select {
            background: var(--input-bg);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 0.55rem 0.75rem;
            color: var(--text-color);
            font-family: inherit;
            font-size: 0.88rem;
            outline: none;
            transition: all 0.2s ease;
            width: 100%;
        }

        input:focus, select:focus {
            border-color: var(--border-focus);
            box-shadow: 0 0 0 2px var(--accent-glow);
        }

        input:disabled, select:disabled {
            opacity: 0.4;
            cursor: not-allowed;
        }

        .input-group-btn {
            display: flex;
            gap: 0.5rem;
        }

        /* Checkbox & Switch */
        .checkbox-label {
            display: flex;
            align-items: center;
            gap: 0.6rem;
            cursor: pointer;
            user-select: none;
            font-size: 0.88rem;
            color: var(--text-color);
        }

        .checkbox-label input[type="checkbox"] {
            appearance: none;
            width: 18px;
            height: 18px;
            border: 1px solid var(--border-color);
            border-radius: 4px;
            background: var(--input-bg);
            cursor: pointer;
            display: grid;
            place-content: center;
            transition: all 0.2s ease;
        }

        .checkbox-label input[type="checkbox"]:checked {
            background: #2563eb;
            border-color: #2563eb;
        }

        .checkbox-label input[type="checkbox"]:checked::before {
            content: "";
            width: 9px;
            height: 5px;
            border-left: 2px solid white;
            border-bottom: 2px solid white;
            transform: rotate(-45deg) translate(1px, -1px);
        }

        /* Channel Status Grid */
        .channels-container {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
            gap: 1rem;
            grid-template-columns: repeat(auto-fill, minmax(170px, 1fr));
            gap: 0.85rem;
            max-height: 480px;
            overflow-y: auto;
            padding-right: 0.25rem;
        }

        .channel-card {
            background: rgba(255,255,255,0.02);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 1rem;
            border-left: 4px solid var(--state-idle-accent);
            border-radius: 10px;
            padding: 0.85rem;
            display: flex;
            flex-direction: column;
            gap: 0.5rem;
            position: relative;
            overflow: hidden;
            transition: all 0.25s ease;
            gap: 0.4rem;
            transition: all 0.2s ease;
        }

        .channel-card:hover {
            transform: translateY(-2px);
            border-color: rgba(255,255,255,0.1);
            background: rgba(255,255,255,0.04);
        }

        .channel-card::before {
            content: '';
            position: absolute;
            left: 0;
            top: 0;
            bottom: 0;
            width: 4px;
            background-color: var(--accent-state, var(--state-idle-accent));
        }

        .channel-card.state-connected {
            --accent-state: var(--state-connected-accent);
            --bg-state: var(--state-connected-bg);
            background: rgba(16, 185, 129, 0.03);
            border-left-color: var(--state-connected-accent);
            background: var(--state-connected-bg);
        }

        .channel-card.state-connecting {
            --accent-state: var(--state-connecting-accent);
            --bg-state: var(--state-connecting-bg);
            background: rgba(59, 130, 246, 0.03);
            border-left-color: var(--state-connecting-accent);
            background: var(--state-connecting-bg);
        }

        .channel-card.state-retrying {
            --accent-state: var(--state-retrying-accent);
            --bg-state: var(--state-retrying-bg);
            background: rgba(245, 158, 11, 0.03);
            border-left-color: var(--state-retrying-accent);
            background: var(--state-retrying-bg);
        }

        .channel-card.state-failed {
            --accent-state: var(--state-failed-accent);
            --bg-state: var(--state-failed-bg);
            background: rgba(239, 68, 68, 0.03);
            border-left-color: var(--state-failed-accent);
            background: var(--state-failed-bg);
        }

        .channel-card.state-disabled {
            --accent-state: var(--state-disabled-accent);
            --bg-state: var(--state-disabled-bg);
            background: rgba(75, 75, 75, 0.02);
            border-left-color: var(--state-disabled-accent);
            background: var(--state-disabled-bg);
            opacity: 0.6;
        }

        .channel-id {
            font-size: 0.85rem;
            font-weight: 700;
            color: var(--text-muted);
        .channel-header-row {
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

        .channel-title {
            font-weight: 700;
            font-size: 0.95rem;
        }

        .channel-state-badge {
            font-size: 0.75rem;
            font-size: 0.72rem;
            padding: 0.2rem 0.5rem;
            border-radius: 9999px;
            font-weight: 600;
            padding: 0.15rem 0.5rem;
            border-radius: 20px;
            background: var(--bg-state, var(--state-idle-bg));
            color: var(--accent-state, var(--state-idle-accent));
            align-self: flex-start;
            text-transform: capitalize;
            background: rgba(255,255,255,0.08);
        }

        .channel-detail {
        .channel-attempt {
            font-size: 0.75rem;
            color: var(--text-muted);
            font-weight: 500;
        }

        .channel-detail {
            font-size: 0.76rem;
            color: var(--text-muted);
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            margin-top: 0.25rem;
        }

        .channel-duration {
            font-size: 0.7rem;
            color: var(--text-muted);
            font-size: 0.72rem;
            color: #93c5fd;
            margin-top: auto;
            font-variant-numeric: tabular-nums;
        }

        /* Form styling */
        .form-group {
        /* Console */
        .console-container {
            background: #000000;
            border: 1px solid var(--border-color);
            border-radius: 10px;
            padding: 1rem;
            height: 280px;
            display: flex;
            flex-direction: column;
            gap: 0.5rem;
            margin-bottom: 1.25rem;
        }

        label {
            font-size: 0.85rem;
            font-weight: 600;
            color: var(--text-muted);
        }

        input[type="text"], input[type="number"], select {
            background: rgba(255, 255, 255, 0.05);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 0.6rem 0.8rem;
            color: var(--text-color);
            font-family: inherit;
            font-size: 0.9rem;
            transition: border-color 0.2s ease;
            width: 100%;
        }

        input:focus, select:focus {
            outline: none;
            border-color: var(--accent-color);
            box-shadow: 0 0 0 2px var(--accent-glow);
        }

        .form-row {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 1rem;
        }

        /* Log console */
        .console-container {
            position: relative;
        }

        .console {
            background: #030712;
            border: 1px solid var(--border-color);
            border-radius: 12px;
            font-family: 'Consolas', 'Courier New', monospace;
            font-size: 0.85rem;
            padding: 1rem;
            color: #34d399;
            height: 320px;
            flex-grow: 1;
            overflow-y: auto;
            font-family: 'JetBrains Mono', 'Cascadia Code', Consolas, monospace;
            font-size: 0.78rem;
            color: #86efac;
            white-space: pre-wrap;
            box-shadow: inset 0 2px 8px rgba(0,0,0,0.8);
            line-height: 1.4;
        }

        .channel-editor-btn {
            font-size: 0.75rem;
            background: rgba(255,255,255,0.05);
            border: 1px solid var(--border-color);
            padding: 0.2rem 0.5rem;
            border-radius: 6px;
            cursor: pointer;
            color: var(--text-color);
            transition: all 0.2s;
        }

        .channel-editor-btn:hover {
            background: rgba(255,255,255,0.15);
        }

        /* Modal styling */
        /* Modal */
        .modal {
            display: none;
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: rgba(0,0,0,0.7);
            width: 100vw;
            height: 100vh;
            background: rgba(0,0,0,0.75);
            backdrop-filter: blur(4px);
            z-index: 1000;
            align-items: center;
            justify-content: center;
            backdrop-filter: blur(4px);
        }

        .modal.open {
            display: flex;
        }

        .modal-content {
            background: #111827;
            border: 1px solid var(--border-color);
            border-radius: 16px;
            width: 95%;
            max-width: 700px;
            padding: 1.5rem;
            box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.5);
            max-width: 780px;
            max-height: 85vh;
            display: flex;
            flex-direction: column;
            gap: 1rem;
            padding: 1.5rem;
            box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.7);
        }

        .modal-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 1px solid var(--border-color);
            padding-bottom: 0.5rem;
            padding-bottom: 0.75rem;
            margin-bottom: 1rem;
        }

        .channels-table-container {
            max-height: 350px;
            flex-grow: 1;
            overflow-y: auto;
            border: 1px solid var(--border-color);
            border-radius: 8px;
            background: rgba(0,0,0,0.2);
            margin-bottom: 1rem;
        }

        table {
            width: 100%;
            border-collapse: collapse;
            font-size: 0.85rem;
            text-align: left;
        }

        th, td {
            padding: 0.75rem;
            border-bottom: 1px solid var(--border-color);
        }

        th {
            background: rgba(255,255,255,0.03);
            background: rgba(255,255,255,0.04);
            color: var(--text-muted);
            text-align: left;
            padding: 0.65rem;
            border-bottom: 1px solid var(--border-color);
            font-weight: 600;
        }

        .checkbox-container {
        td {
            padding: 0.5rem;
            border-bottom: 1px solid rgba(255,255,255,0.05);
            vertical-align: middle;
        }

        .modal-footer {
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 0.5rem;
            cursor: pointer;
            border-top: 1px solid var(--border-color);
            padding-top: 1rem;
        }

        .checkbox-container input {
            cursor: pointer;
        /* Toast notifications */
        #toast {
            position: fixed;
            bottom: 2rem;
            right: 2rem;
            background: #1e293b;
            border: 1px solid #3b82f6;
            color: white;
            padding: 0.75rem 1.25rem;
            border-radius: 10px;
            box-shadow: 0 10px 25px rgba(0,0,0,0.5);
            font-size: 0.88rem;
            display: flex;
            align-items: center;
            gap: 0.6rem;
            transform: translateY(100px);
            opacity: 0;
            transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
            z-index: 2000;
        }

        .grid-inline-fields {
            display: flex;
            gap: 0.5rem;
        #toast.show {
            transform: translateY(0);
            opacity: 1;
        }

        .modal-footer {
            display: flex;
            justify-content: flex-end;
            gap: 0.75rem;
            margin-top: 0.5rem;
        #toast.toast-error {
            border-color: #ef4444;
            background: #2b1313;
        }

        .status-pill {
            display: inline-flex;
            align-items: center;
            gap: 0.35rem;
            font-size: 0.75rem;
            padding: 0.25rem 0.65rem;
            border-radius: 9999px;
            font-weight: 600;
        }
    </style>
</head>
<body>
    <header>
        <div class="logo-section">
            <div id="statusIndicator" class="logo-indicator"></div>
            <div class="logo-title">Tunarr Dummy Starter</div>
        </div>
        <div id="connectionStatus" style="font-size:0.85rem; color:var(--text-muted);">
            Connecting...
        <div class="header-meta">
            <span id="runnerStatusBadge" class="status-pill" style="background:var(--state-idle-bg); color:var(--state-idle-accent);">Keep-Alive: Idle</span>
            <span id="connectionStatus" style="font-size:0.85rem; color:var(--text-muted);">Connecting...</span>
        </div>
    </header>

    <div class="container">
        <!-- Control buttons card -->
        <!-- Runner Control Card -->
        <div class="card">
            <div class="card-header">
                <div class="card-title">Runner Control</div>
                <button id="btnToggleChannels" class="channel-editor-btn" onclick="openChannelEditor()">Configure Channels</button>
                <div class="controls-row">
                    <button class="btn btn-secondary btn-sm" onclick="openChannelEditor()">Configure Channels</button>
                    <button class="btn btn-primary btn-sm" onclick="document.getElementById('configForm').requestSubmit()">Save Settings</button>
                </div>
            </div>
            <div class="controls-row">
                <button id="btnStart" class="btn btn-primary" onclick="startRunner()">Start Keep-Alive</button>
                <button id="btnStop" class="btn btn-danger" onclick="stopRunner()">Stop Keep-Alive</button>
            </div>
        </div>

        <!-- System and Tunarr Controls card -->
        <!-- Security & Service Control Card -->
        <div class="card">
            <div class="card-header">
                <div class="card-title">System & Service Control</div>
                <span id="tunarrStatusBadge" class="channel-state-badge" style="background:var(--state-idle-bg); color:var(--state-idle-accent);">Tunarr: Checking...</span>
                <div class="card-title">Security & Service Control</div>
                <span id="tunarrStatusBadge" class="status-pill" style="background:var(--state-idle-bg); color:var(--state-idle-accent);">Tunarr: Checking...</span>
            </div>
            <div style="display:flex; flex-direction:column; gap:1.25rem;">
                <div>
                    <span style="font-size:0.85rem; color:var(--text-muted); display:block; margin-bottom:0.5rem; font-weight:600;">Tunarr Service:</span>
                    <label style="display:block; margin-bottom:0.5rem; font-weight:600;">Tunarr Service Actions:</label>
                    <div class="controls-row">
                        <button class="btn btn-primary" onclick="controlTunarr('start')">Start</button>
                        <button class="btn btn-secondary" onclick="controlTunarr('restart')">Restart</button>
                        <button class="btn btn-danger" onclick="controlTunarr('stop')">Stop</button>
                        <button class="btn btn-primary btn-sm" onclick="controlTunarr('start')">Start Tunarr</button>
                        <button class="btn btn-secondary btn-sm" onclick="controlTunarr('restart')">Restart Tunarr</button>
                        <button class="btn btn-danger btn-sm" onclick="controlTunarr('stop')">Stop Tunarr</button>
                    </div>
                </div>
                <div style="display:grid; grid-template-columns: 1fr 1fr; gap: 1rem;">
                <div class="form-row">
                    <div>
                        <span style="font-size:0.85rem; color:var(--text-muted); display:block; margin-bottom:0.5rem; font-weight:600;">PC Server App:</span>
                        <label style="display:block; margin-bottom:0.5rem; font-weight:600;">App Server Process:</label>
                        <div class="controls-row">
                            <button class="btn btn-secondary" style="width:100%;" onclick="controlPcServer('pcserver/restart')">Restart App</button>
                            <button class="btn btn-danger" style="width:100%;" onclick="controlPcServer('pcserver/close')">Close App</button>
                            <button class="btn btn-secondary btn-sm" style="flex:1;" onclick="controlPcServer('pcserver/restart')">Restart App</button>
                            <button class="btn btn-danger btn-sm" style="flex:1;" onclick="controlPcServer('pcserver/close')">Close App</button>
                        </div>
                    </div>
                    <div>
                        <span style="font-size:0.85rem; color:var(--text-muted); display:block; margin-bottom:0.5rem; font-weight:600;">Host Computer Power:</span>
                        <label style="display:block; margin-bottom:0.5rem; font-weight:600;">Host Computer Power:</label>
                        <div class="controls-row">
                            <button class="btn btn-secondary" style="border-color:#f59e0b; color:#f59e0b; width:100%;" onclick="controlPcServer('pc/restart', true)">Restart PC</button>
                            <button class="btn btn-danger" style="width:100%;" onclick="controlPcServer('pc/shutdown', true)">Shutdown PC</button>
                            <button class="btn btn-warning btn-sm" style="flex:1;" onclick="controlPcServer('pc/restart', true)">Restart PC</button>
                            <button class="btn btn-danger btn-sm" style="flex:1;" onclick="controlPcServer('pc/shutdown', true)">Shutdown PC</button>
                        </div>
                    </div>
                </div>
            </div>
        </div>

        <div class="dashboard-grid">
            <!-- Channel Status Section -->
            <div class="card">
                <div class="card-header">
                    <div class="card-title">Channels</div>
                    <span id="activeCount" style="font-size:0.85rem; color:var(--text-muted);">0 / 0 Running</span>
            <!-- Left Column: Channel Status Grid & Live Logs -->
            <div style="display:flex; flex-direction:column; gap:1.5rem;">
                <!-- Channel Status Section -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title">Channels</div>
                        <span id="activeCount" style="font-size:0.85rem; color:var(--text-muted);">0 / 0 Running</span>
                    </div>
                    <div id="channelsGrid" class="channels-container">
                        <!-- Loaded dynamically -->
                    </div>
                </div>
                <div id="channelsGrid" class="channels-container">
                    <!-- Loaded dynamically -->

                <!-- Live Log Console Section -->
                <div class="card">
                    <div class="card-header">
                        <div class="card-title">Keep-Alive Log</div>
                        <div class="controls-row">
                            <label class="checkbox-label" style="font-size:0.8rem;">
                                <input type="checkbox" id="chkAutoScroll" checked> Auto-Scroll
                            </label>
                            <button class="btn btn-secondary btn-sm" onclick="clearConsole()">Clear Screen</button>
                        </div>
                    </div>
                    <div class="console-container">
                        <div id="logConsole" class="console">Loading logs...</div>
                    </div>
                </div>
            </div>

            <!-- Configuration Section -->
            <!-- Right Column: Settings Form -->
            <div class="card">
                <div class="card-header">
                    <div class="card-title">Settings</div>
                    <div class="card-title">Settings & Parameters</div>
                    <button type="button" class="btn btn-primary btn-sm" onclick="document.getElementById('configForm').requestSubmit()">Save</button>
                </div>
                <form id="configForm" onsubmit="saveConfig(event)">
                    <!-- Stream URL & Channel Count -->
                    <div class="form-group">
                        <label for="txtBaseUrl">Tunarr Base Channels URL</label>
                        <input type="text" id="txtBaseUrl" required>
                        <input type="text" id="txtBaseUrl" placeholder="http://127.0.0.1:8000/stream/channels" required>
                    </div>

                    <div class="form-row">
                        <div class="form-group">
                            <label for="nudChannelCount">Channel Count</label>
                            <label for="nudChannelCount">Channel Count (1..200)</label>
                            <input type="number" id="nudChannelCount" min="1" max="200" required>
                        </div>
                        <div class="form-group">
                            <label for="nudStartupDelay">Startup Delay (s)</label>
                            <label for="nudStartupDelay">Startup Delay (seconds)</label>
                            <input type="number" id="nudStartupDelay" min="0" required>
                        </div>
                    </div>

                    <div class="form-row">
                    <div class="form-row-3">
                        <div class="form-group">
                            <label for="nudStaggerDelay">Stagger Delay (ms)</label>
                            <label for="nudStaggerDelay">Stagger (ms)</label>
                            <input type="number" id="nudStaggerDelay" min="0" required>
                        </div>
                        <div class="form-group">
                            <label for="nudRetryCount">Retry Count</label>
                            <input type="number" id="nudRetryCount" min="0" required>
                        </div>
                        <div class="form-group">
                            <label for="nudRetryDelay">Retry Delay (ms)</label>
                            <input type="number" id="nudRetryDelay" min="0" placeholder="1500">
                        </div>
                    </div>

                    <!-- FFmpeg & Hardware Acceleration Section -->
                    <div class="section-subhead">FFmpeg & Hardware Acceleration</div>

                    <div class="form-group">
                        <label for="txtFfmpegPath">FFmpeg Executable Path (Optional)</label>
                        <div class="input-group-btn">
                            <input type="text" id="txtFfmpegPath" placeholder="Leave empty to use system PATH">
                            <button type="button" class="btn btn-secondary btn-sm" onclick="checkFfmpeg()">Check</button>
                        </div>
                        <span id="ffmpegStatusBadge" style="font-size:0.75rem; color:var(--text-muted); margin-top:0.2rem;"></span>
                    </div>

                    <div class="form-row">
                        <div class="form-group">
                            <label for="cboHwAccel">Hardware Acceleration</label>
                            <select id="cboHwAccel">
                                <option value="Auto">Auto</option>
                                <option value="None">None</option>
                                <option value="d3d11va">d3d11va</option>
                                <option value="dxva2">dxva2</option>
                                <option value="cuda">cuda</option>
                                <option value="qsv">qsv</option>
                                <option value="v4l2m2m">v4l2m2m (Raspberry Pi/ARM)</option>
                                <option value="vaapi">vaapi (Linux/Intel/AMD)</option>
                                <option value="drm">drm (Direct Rendering)</option>
                                <option value="rkmpp">rkmpp (Rockchip ARM)</option>
                                <option value="cuda">cuda (NVIDIA)</option>
                                <option value="qsv">qsv (Intel QuickSync)</option>
                                <option value="d3d11va">d3d11va (Windows)</option>
                                <option value="dxva2">dxva2 (Windows)</option>
                                <option value="opencl">opencl</option>
                                <option value="vulkan">vulkan</option>
                            </select>
                            <div class="input-group-btn">
                                <select id="cboHwAccel">
                                    <option value="Auto">Auto</option>
                                    <option value="None">None</option>
                                    <option value="v4l2m2m">v4l2m2m (Raspberry Pi/ARM)</option>
                                    <option value="vaapi">vaapi (Linux/Intel/AMD)</option>
                                    <option value="drm">drm (Direct Rendering)</option>
                                    <option value="rkmpp">rkmpp (Rockchip ARM)</option>
                                    <option value="cuda">cuda (NVIDIA)</option>
                                    <option value="qsv">qsv (Intel QuickSync)</option>
                                    <option value="d3d11va">d3d11va (Windows)</option>
                                    <option value="dxva2">dxva2 (Windows)</option>
                                    <option value="opencl">opencl</option>
                                    <option value="vulkan">vulkan</option>
                                </select>
                                <button type="button" id="btnDetectHwAccel" class="btn btn-secondary btn-sm" onclick="detectHwAccel()">Detect</button>
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="nudThreads">Threads/Proc (0=auto)</label>
                            <label for="nudThreads">Threads / Proc (0=auto)</label>
                            <input type="number" id="nudThreads" min="0" max="32" required>
                        </div>
                    </div>

                    <!-- Automation & Autostart Section -->
                    <div class="section-subhead">Automation & Startup</div>

                    <div class="form-row" style="margin-bottom:0.75rem;">
                        <label class="checkbox-label">
                            <input type="checkbox" id="chkAutoStart">
                            <span>Auto Start At Launch</span>
                        </label>
                        <label class="checkbox-label">
                            <input type="checkbox" id="chkStartWithWindows">
                            <span>Start With Windows / Boot</span>
                        </label>
                    </div>

                    <!-- Remote Web Server & Security Section -->
                    <div class="section-subhead">Remote Web Server & Security</div>

                    <div class="form-row">
                        <div class="form-group">
                            <label for="chkWebserver">Web Server Enabled</label>
                            <select id="chkWebserver">
                                <option value="true">Yes</option>
                                <option value="false">No</option>
                            </select>
                        <div class="form-group" style="justify-content:center;">
                            <label class="checkbox-label" style="margin-top:0.5rem;">
                                <input type="checkbox" id="chkWebserver">
                                <span>Enable Web Server</span>
                            </label>
                        </div>
                        <div class="form-group">
                            <label for="nudWebPort">Web Server Port</label>
                            <input type="number" id="nudWebPort" min="1" max="65535" required>
                        </div>
                    </div>

                    <div class="form-group">
                        <label for="txtWebserverPassword">Web Server Password (HTTPS if set)</label>
                        <input type="password" id="txtWebserverPassword">
                    </div>

                    <div class="form-row">
                        <div class="form-group">
                            <label for="chkWaitForTunarr">Wait for Tunarr on Startup</label>
                            <select id="chkWaitForTunarr">
                                <option value="true">Yes</option>
                                <option value="false">No</option>
                            </select>
                        <label for="txtWebserverPassword">Web Server Password (Enables HTTPS if set)</label>
                        <div class="input-group-btn">
                            <input type="password" id="txtWebserverPassword" placeholder="Leave empty for HTTP without password">
                            <button type="button" class="btn btn-secondary btn-sm" onclick="togglePasswordVisibility()">Show</button>
                        </div>
                        <div class="form-group">
                            <label for="chkTunarrUseService">Use Service instead of Process</label>
                            <select id="chkTunarrUseService">
                                <option value="true">Yes</option>
                                <option value="false">No</option>
                            </select>
                        </div>
                    </div>

                    <div class="form-group">
                        <label for="txtTunarrServiceName">Tunarr Service Name</label>
                        <input type="text" id="txtTunarrServiceName">
                    <!-- Tunarr Service Configuration Section -->
                    <div class="section-subhead">Tunarr Service Integration</div>

                    <div class="form-row" style="margin-bottom:0.75rem;">
                        <label class="checkbox-label">
                            <input type="checkbox" id="chkWaitForTunarr">
                            <span>Wait for Tunarr on Startup</span>
                        </label>
                        <label class="checkbox-label">
                            <input type="checkbox" id="chkTunarrUseService" onchange="onTunarrUseServiceChanged()">
                            <span>Use Service vs Process</span>
                        </label>
                    </div>

                    <div class="form-group">
                        <label for="txtTunarrExePath">Tunarr Exe Path (for process mode)</label>
                        <input type="text" id="txtTunarrExePath">
                    <div class="form-group" id="groupServiceName">
                        <label for="txtTunarrServiceName">Tunarr Service Name (Systemd / WinService)</label>
                        <input type="text" id="txtTunarrServiceName" placeholder="Tunarr">
                    </div>

                    <div class="form-group">
                        <label for="txtFfmpegPath">FFmpeg Path (Optional)</label>
                        <input type="text" id="txtFfmpegPath">
                    <div class="form-group" id="groupExePath">
                        <label for="txtTunarrExePath">Tunarr Executable Path (Process Mode)</label>
                        <input type="text" id="txtTunarrExePath" placeholder="/usr/bin/tunarr or C:\Tunarr\tunarr.exe">
                    </div>

                    <button type="submit" class="btn btn-primary" style="width: 100%; margin-top: 0.5rem;">Save Settings</button>
                    <button type="submit" class="btn btn-primary" style="width: 100%; margin-top: 1rem; padding: 0.85rem;">Save All Settings</button>
                </form>
            </div>
        </div>

        <!-- Log Section -->
        <div class="card">
            <div class="card-header">
                <div class="card-title">Live Log Console</div>
                <button class="channel-editor-btn" onclick="clearConsole()">Clear Screen</button>
            </div>
            <div class="console-container">
                <div id="logConsole" class="console">Loading logs...</div>
            </div>
        </div>
    </div>

    <!-- Modal Channel Config Editor -->
    <div id="channelModal" class="modal">
        <div class="modal-content">
            <div class="modal-header">
                <h3 style="font-weight:600;">Configure Channels</h3>
                <button class="channel-editor-btn" onclick="closeChannelEditor()">Close</button>
                <button class="btn btn-secondary btn-sm" onclick="closeChannelEditor()">Close</button>
            </div>
            <p style="font-size:0.8rem; color:var(--text-muted)">Override URLs and retries or enable/disable specific channels.</p>
            <p style="font-size:0.82rem; color:var(--text-muted); margin-bottom: 0.75rem;">
                Customize individual stream URLs and retry overrides, or toggle channels on/off.
            </p>
            
            <div class="channels-table-container">
                <table>
                    <thead>
                        <tr>
                            <th style="width: 80px">ID</th>
                            <th style="width: 80px">Enabled</th>
                            <th style="width: 75px">Channel</th>
                            <th style="width: 70px; text-align:center;">Enabled</th>
                            <th>Custom URL (Optional)</th>
                            <th style="width: 120px">Retry Override</th>
                            <th style="width: 130px">Retry Override</th>
                        </tr>
                    </thead>
                    <tbody id="channelsTableBody">
                        <!-- Loaded dynamically -->
                    </tbody>
                </table>
            </div>

            <div class="modal-footer">
                <button class="btn btn-secondary" onclick="closeChannelEditor()">Cancel</button>
                <button class="btn btn-primary" onclick="saveChannelsEditor()">Apply Changes</button>
                <div class="controls-row">
                    <button class="btn btn-secondary btn-sm" onclick="addChannelRow()">+ Add Channel</button>
                    <button class="btn btn-secondary btn-sm" onclick="removeLastChannelRow()">- Remove Last</button>
                </div>
                <div class="controls-row">
                    <button class="btn btn-secondary" onclick="closeChannelEditor()">Cancel</button>
                    <button class="btn btn-primary" onclick="saveChannelsEditor()">Save & Apply</button>
                </div>
            </div>
        </div>
    </div>

    <!-- Toast message container -->
    <div id="toast">
        <span id="toastMessage">Notification</span>
    </div>

    <script>
        let currentChannels = [];
        let globalConfig = null;

        function showToast(message, isError = false) {
            const toast = document.getElementById('toast');
            const msg = document.getElementById('toastMessage');
            msg.innerText = message;
            toast.className = isError ? 'toast-error show' : 'show';
            setTimeout(() => {
                toast.className = '';
            }, 3500);
        }

        function togglePasswordVisibility() {
            const pwdInput = document.getElementById('txtWebserverPassword');
            pwdInput.type = pwdInput.type === 'password' ? 'text' : 'password';
        }

        function onTunarrUseServiceChanged() {
            const useService = document.getElementById('chkTunarrUseService').checked;
            document.getElementById('txtTunarrServiceName').disabled = !useService;
            document.getElementById('txtTunarrExePath').disabled = useService;
            document.getElementById('groupServiceName').style.opacity = useService ? '1' : '0.45';
            document.getElementById('groupExePath').style.opacity = useService ? '0.45' : '1';
        }

        async function fetchStatus() {
            try {
                const res = await fetch('/api/status');
                if (!res.ok) throw new Error('API error');
                const data = await res.json();
                
                // Connection Indicator
                document.getElementById('connectionStatus').innerText = 'Connected';
                document.getElementById('connectionStatus').style.color = '#10b981';

                // Runner Indicator & Buttons
                const indicator = document.getElementById('statusIndicator');
                const runnerBadge = document.getElementById('runnerStatusBadge');

                if (data.isRunning) {
                    indicator.classList.add('active');
                    runnerBadge.innerText = 'Keep-Alive: Running';
                    runnerBadge.style.background = 'var(--state-connected-bg)';
                    runnerBadge.style.color = 'var(--state-connected-accent)';
                    document.getElementById('btnStart').disabled = true;
                    document.getElementById('btnStop').disabled = false;
                } else {
                    indicator.classList.remove('active');
                    runnerBadge.innerText = 'Keep-Alive: Stopped';
                    runnerBadge.style.background = 'var(--state-idle-bg)';
                    runnerBadge.style.color = 'var(--state-idle-accent)';
                    document.getElementById('btnStart').disabled = false;
                    document.getElementById('btnStop').disabled = true;
                }

                // Channels grid
                const grid = document.getElementById('channelsGrid');
                grid.innerHTML = '';

                let activeCount = 0;
                let enabledCount = 0;

                // Sort channel statuses
                const sortedChannels = Object.values(data.channels).sort((a, b) => a.channel - b.channel);
                
                sortedChannels.forEach(c => {
                    enabledCount++;
                    let stateClass = 'state-idle';
                    let stateName = 'Idle';

                    switch (c.state) {
                        case 1: // Connecting
                        case 1:
                            stateClass = 'state-connecting';
                            stateName = 'Connecting';
                            break;
                        case 2: // Connected
                        case 2:
                            stateClass = 'state-connected';
                            stateName = 'Connected';
                            activeCount++;
                            break;
                        case 3: // Retrying
                        case 3:
                            stateClass = 'state-retrying';
                            stateName = 'Retrying';
                            break;
                        case 4: // StartFailed
                        case 5: // UnexpectedExit
                        case 4:
                            stateClass = 'state-failed';
                            stateName = c.state === 4 ? 'Start Failed' : 'Disconnected';
                            stateName = 'Start Failed';
                            break;
                        case 6: // Canceled
                        case 5:
                            stateClass = 'state-failed';
                            stateName = 'Disconnected';
                            break;
                        case 6:
                            stateClass = 'state-idle';
                            stateName = 'Stopped';
                            break;
                        case 7: // Stopped
                        case 7:
                            stateClass = 'state-failed';
                            stateName = 'Max Retries';
                            break;
                        case 8: // Disabled
                        case 8:
                            stateClass = 'state-disabled';
                            stateName = 'Disabled';
                            enabledCount--; // Don't count disabled
                            enabledCount--;
                            break;
                    }

                    const card = document.createElement('div');
                    card.className = `channel-card ${stateClass}`;
                    
                    const durationText = c.runDurationSeconds > 0 
                        ? `${c.runDurationSeconds.toFixed(1)}s` 
                        : '';

                    const attemptText = (c.state === 1 || c.state === 3) && c.maxAttempts > 0
                        ? `Attempt: ${c.attempt}/${c.maxAttempts}`
                        : '';

                    card.innerHTML = `
                        <div class="channel-id">
                            <span>Ch ${c.channel}</span>
                        <div class="channel-header-row">
                            <span class="channel-title">Ch ${c.channel}</span>
                            <span class="channel-state-badge">${stateName}</span>
                        </div>
                        ${attemptText ? `<div class="channel-attempt">${attemptText}</div>` : ''}
                        <div class="channel-detail" title="${c.message || ''}">${c.message || 'Waiting...'}</div>
                        <div class="channel-duration">${durationText}</div>
                        ${durationText ? `<div class="channel-duration">${durationText}</div>` : ''}
                    `;
                    grid.appendChild(card);
                });

                document.getElementById('activeCount').innerText = `${activeCount} / ${enabledCount} Active`;

            } catch (err) {
                document.getElementById('connectionStatus').innerText = 'Offline';
                document.getElementById('connectionStatus').style.color = '#ef4444';
                document.getElementById('statusIndicator').classList.remove('active');
            }
        }

        async function fetchLogs() {
            try {
                const res = await fetch('/api/log');
                if (!res.ok) throw new Error('API error');
                const text = await res.text();
                const consoleDiv = document.getElementById('logConsole');
                
                // Smart auto-scroll: if user is scrolled up, don't force scroll
                const shouldScroll = consoleDiv.scrollHeight - consoleDiv.clientHeight <= consoleDiv.scrollTop + 30;
                consoleDiv.innerText = text || "No logs yet.";
                if (shouldScroll) {
                if (document.getElementById('chkAutoScroll').checked) {
                    consoleDiv.scrollTop = consoleDiv.scrollHeight;
                }
            } catch (err) {
                document.getElementById('logConsole').innerText = "Failed to fetch logs.";
            }
        }

        async function fetchConfig() {
            try {
                const res = await fetch('/api/config');
                if (!res.ok) throw new Error('API error');
                const config = await res.json();
                globalConfig = config;

                document.getElementById('txtBaseUrl').value = config.baseUrl;
                document.getElementById('nudChannelCount').value = config.channelCount;
                document.getElementById('nudStartupDelay').value = config.startupDelaySeconds;
                document.getElementById('nudStaggerDelay').value = config.staggerDelayMs;
                document.getElementById('nudRetryCount').value = config.retryCount;
                document.getElementById('cboHwAccel').value = config.hwAccel;
                document.getElementById('nudThreads').value = config.threadsPerProcess;
                document.getElementById('chkWebserver').value = config.enableWebserver.toString();
                document.getElementById('nudWebPort').value = config.webserverPort;
                document.getElementById('txtBaseUrl').value = config.baseUrl || '';
                document.getElementById('nudChannelCount').value = config.channelCount || 1;
                document.getElementById('nudStartupDelay').value = config.startupDelaySeconds ?? 0;
                document.getElementById('nudStaggerDelay').value = config.staggerDelayMs ?? 0;
                document.getElementById('nudRetryCount').value = config.retryCount ?? 0;
                document.getElementById('nudRetryDelay').value = config.retryDelayMs ?? 1500;
                document.getElementById('txtFfmpegPath').value = config.ffmpegPath || '';
                document.getElementById('cboHwAccel').value = config.hwAccel || 'Auto';
                document.getElementById('nudThreads').value = config.threadsPerProcess ?? 0;

                document.getElementById('chkAutoStart').checked = !!config.autoStartOnLaunch;
                document.getElementById('chkStartWithWindows').checked = !!config.startWithWindows;
                document.getElementById('chkWebserver').checked = config.enableWebserver !== false;
                document.getElementById('nudWebPort').value = config.webserverPort || 1290;
                document.getElementById('txtWebserverPassword').value = config.webserverPassword || '';
                document.getElementById('chkWaitForTunarr').value = config.waitForTunarr.toString();
                document.getElementById('chkTunarrUseService').value = config.tunarrUseService.toString();
                document.getElementById('txtTunarrServiceName').value = config.tunarrServiceName || '';

                document.getElementById('chkWaitForTunarr').checked = !!config.waitForTunarr;
                document.getElementById('chkTunarrUseService').checked = !!config.tunarrUseService;
                document.getElementById('txtTunarrServiceName').value = config.tunarrServiceName || 'Tunarr';
                document.getElementById('txtTunarrExePath').value = config.tunarrExePath || '';

                onTunarrUseServiceChanged();
                currentChannels = config.channels || [];
            } catch (err) {
                console.error("Failed to load config", err);
            }
        }

        async function saveConfig(e) {
            e.preventDefault();
            if (!globalConfig) return;

            const updatedConfig = {
                ...globalConfig,
                baseUrl: document.getElementById('txtBaseUrl').value.trim(),
                channelCount: parseInt(document.getElementById('nudChannelCount').value),
                startupDelaySeconds: parseInt(document.getElementById('nudStartupDelay').value),
                staggerDelayMs: parseInt(document.getElementById('nudStaggerDelay').value),
                retryCount: parseInt(document.getElementById('nudRetryCount').value),
                channelCount: parseInt(document.getElementById('nudChannelCount').value) || 1,
                startupDelaySeconds: parseInt(document.getElementById('nudStartupDelay').value) || 0,
                staggerDelayMs: parseInt(document.getElementById('nudStaggerDelay').value) || 0,
                retryCount: parseInt(document.getElementById('nudRetryCount').value) || 0,
                retryDelayMs: parseInt(document.getElementById('nudRetryDelay').value) || 1500,
                ffmpegPath: document.getElementById('txtFfmpegPath').value.trim(),
                hwAccel: document.getElementById('cboHwAccel').value,
                threadsPerProcess: parseInt(document.getElementById('nudThreads').value),
                enableWebserver: document.getElementById('chkWebserver').value === 'true',
                webserverPort: parseInt(document.getElementById('nudWebPort').value),
                ffmpegPath: document.getElementById('txtFfmpegPath').value.trim(),
                threadsPerProcess: parseInt(document.getElementById('nudThreads').value) || 0,
                autoStartOnLaunch: document.getElementById('chkAutoStart').checked,
                startWithWindows: document.getElementById('chkStartWithWindows').checked,
                enableWebserver: document.getElementById('chkWebserver').checked,
                webserverPort: parseInt(document.getElementById('nudWebPort').value) || 1290,
                webserverPassword: document.getElementById('txtWebserverPassword').value,
                waitForTunarr: document.getElementById('chkWaitForTunarr').value === 'true',
                tunarrUseService: document.getElementById('chkTunarrUseService').value === 'true',
                waitForTunarr: document.getElementById('chkWaitForTunarr').checked,
                tunarrUseService: document.getElementById('chkTunarrUseService').checked,
                tunarrServiceName: document.getElementById('txtTunarrServiceName').value.trim(),
                tunarrExePath: document.getElementById('txtTunarrExePath').value.trim(),
                channels: currentChannels
            };

            // Automatically scale channels if count changed
            while (updatedConfig.channels.length < updatedConfig.channelCount) {
                const nextId = updatedConfig.channels.length > 0 ? Math.max(...updatedConfig.channels.map(c => c.channelId)) + 1 : 1;
                updatedConfig.channels.push({ channelId: nextId, url: '', enabled: true, retryCount: null });
            }
            if (updatedConfig.channels.length > updatedConfig.channelCount) {
                updatedConfig.channels = updatedConfig.channels.slice(0, updatedConfig.channelCount);
            }

            try {
                const res = await fetch('/api/config', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(updatedConfig)
                });
                const data = await res.json();
                if (data.success) {
                    alert('Configuration saved successfully!');
                    showToast('Settings saved successfully!');
                    fetchConfig();
                    fetchStatus();
                } else {
                    alert('Save failed: ' + data.error);
                    showToast('Save failed: ' + data.error, true);
                }
            } catch (err) {
                alert('Request failed: ' + err);
                showToast('Save request failed: ' + err, true);
            }
        }

        async function detectHwAccel() {
            const btn = document.getElementById('btnDetectHwAccel');
            const originalText = btn.innerText;
            btn.innerText = 'Detecting...';
            btn.disabled = true;

            try {
                const res = await fetch('/api/hwaccel/detect');
                if (!res.ok) throw new Error('API error');
                const data = await res.json();

                const select = document.getElementById('cboHwAccel');
                
                data.backends.forEach(backend => {
                    let exists = false;
                    for (let i = 0; i < select.options.length; i++) {
                        if (select.options[i].value.toLowerCase() === backend.toLowerCase()) {
                            exists = true;
                            break;
                        }
                    }
                    if (!exists) {
                        const opt = document.createElement('option');
                        opt.value = backend;
                        opt.innerText = backend;
                        select.appendChild(opt);
                    }
                });

                if (data.recommended && data.recommended !== 'None') {
                    select.value = data.recommended;
                }

                showToast(`Hardware acceleration detected: ${data.backends.join(', ') || 'None'}`);
                fetchLogs();
            } catch (err) {
                showToast('Hardware acceleration detection failed: ' + err, true);
            } finally {
                btn.innerText = originalText;
                btn.disabled = false;
            }
        }

        async function checkFfmpeg() {
            const badge = document.getElementById('ffmpegStatusBadge');
            badge.innerText = 'Checking...';
            badge.style.color = 'var(--text-muted)';

            try {
                const res = await fetch('/api/ffmpeg/check');
                if (!res.ok) throw new Error('API error');
                const data = await res.json();

                if (data.resolved) {
                    badge.innerText = `✓ Found: ${data.path} (${data.version ? data.version.substring(0, 35) + '...' : ''})`;
                    badge.style.color = '#10b981';
                    showToast('FFmpeg resolved: ' + data.path);
                } else {
                    badge.innerText = '✗ FFmpeg executable not found on host!';
                    badge.style.color = '#ef4444';
                    showToast('FFmpeg not found. Check path or install ffmpeg.', true);
                }
            } catch (err) {
                badge.innerText = 'Error checking FFmpeg';
                badge.style.color = '#ef4444';
            }
        }

        async function startRunner() {
            try {
                await fetch('/api/start', { method: 'POST' });
                showToast('Keep-Alive runner started');
                fetchStatus();
            } catch (err) { alert('Failed to start runner: ' + err); }
            } catch (err) { showToast('Failed to start runner: ' + err, true); }
        }

        async function stopRunner() {
            try {
                await fetch('/api/stop', { method: 'POST' });
                showToast('Keep-Alive runner stopped');
                fetchStatus();
            } catch (err) { alert('Failed to stop runner: ' + err); }
            } catch (err) { showToast('Failed to stop runner: ' + err, true); }
        }

        async function controlTunarr(action) {
            try {
                const res = await fetch(`/api/tunarr/${action}`, { method: 'POST' });
                const data = await res.json();
                if (data.success) {
                    showToast(`Tunarr action '${action}' sent`);
                    fetchTunarrStatus();
                    fetchLogs();
                } else {
                    alert('Action failed');
                    showToast('Action failed', true);
                }
            } catch (err) { alert('Failed to control Tunarr service: ' + err); }
            } catch (err) { showToast('Failed to control Tunarr: ' + err, true); }
        }

        async function controlPcServer(endpoint, confirmRequired = false) {
            if (confirmRequired && !confirm('Are you sure you want to perform this action?')) {
                return;
            }
            try {
                const res = await fetch(`/api/${endpoint}`, { method: 'POST' });
                const data = await res.json();
                if (data.success) {
                    alert('Request sent successfully');
                    showToast('Request sent successfully');
                } else {
                    alert('Action failed');
                    showToast('Action failed', true);
                }
            } catch (err) { alert('Failed to send PC control request: ' + err); }
            } catch (err) { showToast('Failed to send control request: ' + err, true); }
        }

        async function fetchTunarrStatus() {
            try {
                const res = await fetch('/api/tunarr/status');
                if (!res.ok) throw new Error('API error');
                const data = await res.json();
                const badge = document.getElementById('tunarrStatusBadge');
                if (data.isRunning) {
                    badge.innerText = 'Tunarr: Running';
                    badge.style.background = 'rgba(16, 185, 129, 0.15)';
                    badge.style.color = '#10b981';
                    badge.style.background = 'var(--state-connected-bg)';
                    badge.style.color = 'var(--state-connected-accent)';
                } else {
                    badge.innerText = 'Tunarr: Stopped';
                    badge.style.background = 'rgba(239, 68, 68, 0.15)';
                    badge.style.color = '#ef4444';
                    badge.style.background = 'var(--state-failed-bg)';
                    badge.style.color = 'var(--state-failed-accent)';
                }
            } catch (err) {
                const badge = document.getElementById('tunarrStatusBadge');
                badge.innerText = 'Tunarr: Unknown';
                badge.style.background = 'rgba(107, 114, 128, 0.15)';
                badge.style.color = '#9ca3af';
                badge.style.background = 'var(--state-idle-bg)';
                badge.style.color = 'var(--state-idle-accent)';
            }
        }

        function clearConsole() {
            document.getElementById('logConsole').innerText = '';
        }

        // Channel Modal Editor
        function openChannelEditor() {
            const modal = document.getElementById('channelModal');
        // Channels Table Modal
        function renderChannelsTable(channels) {
            const tbody = document.getElementById('channelsTableBody');
            tbody.innerHTML = '';

            // Ensure currentChannels matches the spinner count in size
            const countInputVal = parseInt(document.getElementById('nudChannelCount').value);
            
            // Adjust currentChannels size to match the UI count
            let tempChannels = JSON.parse(JSON.stringify(currentChannels));
            while (tempChannels.length < countInputVal) {
                const nextId = tempChannels.length > 0 ? Math.max(...tempChannels.map(c => c.channelId)) + 1 : 1;
                tempChannels.push({ channelId: nextId, url: '', enabled: true, retryCount: null });
            }
            if (tempChannels.length > countInputVal) {
                tempChannels = tempChannels.slice(0, countInputVal);
            }

            tempChannels.forEach((c, idx) => {
            channels.forEach((c, idx) => {
                const tr = document.createElement('tr');
                const retryVal = c.retryCount !== null && c.retryCount !== undefined ? c.retryCount : '';
                
                tr.innerHTML = `
                    <td style="font-weight:700">Ch ${c.channelId}</td>
                    <td>
                        <label class="checkbox-container">
                            <input type="checkbox" id="chk_chan_${idx}" ${c.enabled ? 'checked' : ''}>
                        </label>
                    <td style="text-align:center;">
                        <input type="checkbox" id="chk_chan_${idx}" ${c.enabled ? 'checked' : ''} style="width:16px; height:16px;">
                    </td>
                    <td>
                        <input type="text" id="url_chan_${idx}" value="${c.url || ''}" placeholder="Default Url" style="padding:0.4rem; font-size:0.8rem;">
                        <input type="text" id="url_chan_${idx}" value="${c.url || ''}" placeholder="Default: {BaseUrl}/${c.channelId}.m3u8" style="padding:0.4rem; font-size:0.8rem;">
                    </td>
                    <td>
                        <input type="number" id="retry_chan_${idx}" value="${retryVal}" placeholder="Global" style="padding:0.4rem; font-size:0.8rem;" min="0">
                    </td>
                `;
                tbody.appendChild(tr);
            });
        }

        function openChannelEditor() {
            const modal = document.getElementById('channelModal');
            const countInputVal = parseInt(document.getElementById('nudChannelCount').value) || 1;
            
            let tempChannels = JSON.parse(JSON.stringify(currentChannels));
            while (tempChannels.length < countInputVal) {
                const nextId = tempChannels.length > 0 ? Math.max(...tempChannels.map(c => c.channelId)) + 1 : 1;
                tempChannels.push({ channelId: nextId, url: '', enabled: true, retryCount: null });
            }
            if (tempChannels.length > countInputVal) {
                tempChannels = tempChannels.slice(0, countInputVal);
            }

            renderChannelsTable(tempChannels);
            modal.classList.add('open');
        }

        function addChannelRow() {
            const tbody = document.getElementById('channelsTableBody');
            const currentRows = tbody.rows.length;
            const nextId = currentRows + 1;

            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td style="font-weight:700">Ch ${nextId}</td>
                <td style="text-align:center;">
                    <input type="checkbox" id="chk_chan_${currentRows}" checked style="width:16px; height:16px;">
                </td>
                <td>
                    <input type="text" id="url_chan_${currentRows}" value="" placeholder="Default: {BaseUrl}/${nextId}.m3u8" style="padding:0.4rem; font-size:0.8rem;">
                </td>
                <td>
                    <input type="number" id="retry_chan_${currentRows}" value="" placeholder="Global" style="padding:0.4rem; font-size:0.8rem;" min="0">
                </td>
            `;
            tbody.appendChild(tr);
            document.getElementById('nudChannelCount').value = nextId;
        }

        function removeLastChannelRow() {
            const tbody = document.getElementById('channelsTableBody');
            if (tbody.rows.length > 1) {
                tbody.deleteRow(tbody.rows.length - 1);
                document.getElementById('nudChannelCount').value = tbody.rows.length;
            }
        }

        function closeChannelEditor() {
            document.getElementById('channelModal').classList.remove('open');
        }

        function saveChannelsEditor() {
            const countInputVal = parseInt(document.getElementById('nudChannelCount').value);
        async function saveChannelsEditor() {
            const tableRows = document.getElementById('channelsTableBody').rows;
            const updated = [];

            for (let i = 0; i < tableRows.length; i++) {
                const enabled = document.getElementById(`chk_chan_${i}`).checked;
                const url = document.getElementById(`url_chan_${i}`).value.trim();
                const retryStr = document.getElementById(`retry_chan_${i}`).value;
                const retryCount = retryStr !== "" ? parseInt(retryStr) : null;
                
                updated.push({
                    channelId: i + 1,
                    url: url,
                    enabled: enabled,
                    retryCount: retryCount
                });
            }

            currentChannels = updated;
            document.getElementById('nudChannelCount').value = updated.length;
            closeChannelEditor();
            alert('Channel configurations updated. Click "Save Settings" to write changes permanently to Registry.');

            if (globalConfig) {
                const updatedConfig = {
                    ...globalConfig,
                    channels: currentChannels,
                    channelCount: currentChannels.length
                };

                try {
                    const res = await fetch('/api/config', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(updatedConfig)
                    });
                    const data = await res.json();
                    if (data.success) {
                        showToast('Channel configuration saved and applied!');
                        fetchConfig();
                        fetchStatus();
                    } else {
                        showToast('Failed to save channel config: ' + data.error, true);
                    }
                } catch (err) {
                    showToast('Request failed: ' + err, true);
                }
            }
        }

        // Init
        // Initialize
        fetchConfig();
        fetchStatus();
        fetchLogs();
        fetchTunarrStatus();
        checkFfmpeg();

        // Polling
        // Polling loops
        setInterval(fetchStatus, 1000);
        setInterval(fetchLogs, 1000);
        setInterval(fetchTunarrStatus, 2000);
    </script>
</body>
</html>
""";
    }
}
