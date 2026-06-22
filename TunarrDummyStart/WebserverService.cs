using System;
using System.Collections.Generic;
using System.IO;
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
    <style>
        :root {
            --bg-color: #0b0f19;
            --panel-bg: rgba(17, 24, 39, 0.7);
            --border-color: rgba(255, 255, 255, 0.08);
            --accent-color: #3b82f6;
            --accent-glow: rgba(59, 130, 246, 0.5);
            --text-color: #f3f4f6;
            --text-muted: #9ca3af;
            
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
            backdrop-filter: blur(12px);
            border-bottom: 1px solid var(--border-color);
            padding: 1.25rem 2rem;
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

        .container {
            max-width: 1200px;
            width: 100%;
            margin: 2rem auto;
            padding: 0 1.5rem;
            display: flex;
            flex-direction: column;
            gap: 2rem;
            flex-grow: 1;
        }

        .dashboard-grid {
            display: grid;
            grid-template-columns: 1fr;
            gap: 2rem;
        }

        @media (min-width: 1024px) {
            .dashboard-grid {
                grid-template-columns: 2fr 1fr;
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
        }

        .card:hover {
            border-color: rgba(255,255,255,0.15);
            box-shadow: 0 15px 35px -5px rgba(0,0,0,0.6);
        }

        .card-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 1.5rem;
            border-bottom: 1px solid var(--border-color);
            padding-bottom: 0.75rem;
        }

        .card-title {
            font-size: 1.1rem;
            font-weight: 600;
            color: var(--text-color);
        }

        /* Controls */
        .controls-row {
            display: flex;
            gap: 1rem;
        }

        .btn {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: 0.5rem;
            padding: 0.75rem 1.5rem;
            border-radius: 10px;
            font-family: inherit;
            font-size: 0.95rem;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s ease;
            border: 1px solid transparent;
        }

        .btn-primary {
            background: #2563eb;
            color: white;
            box-shadow: 0 4px 14px rgba(37, 99, 235, 0.4);
        }

        .btn-primary:hover {
            background: #1d4ed8;
            transform: translateY(-1px);
        }

        .btn-danger {
            background: #dc2626;
            color: white;
            box-shadow: 0 4px 14px rgba(220, 38, 38, 0.4);
        }

        .btn-danger:hover {
            background: #b91c1c;
            transform: translateY(-1px);
        }

        .btn-secondary {
            background: rgba(255,255,255,0.05);
            color: var(--text-color);
            border-color: var(--border-color);
        }

        .btn-secondary:hover {
            background: rgba(255,255,255,0.1);
        }

        .btn:disabled {
            opacity: 0.5;
            cursor: not-allowed;
            transform: none !important;
            box-shadow: none !important;
        }

        /* Channel Status Grid */
        .channels-container {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
            gap: 1rem;
        }

        .channel-card {
            background: rgba(255,255,255,0.02);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 1rem;
            display: flex;
            flex-direction: column;
            gap: 0.5rem;
            position: relative;
            overflow: hidden;
            transition: all 0.25s ease;
        }

        .channel-card:hover {
            transform: translateY(-2px);
            border-color: rgba(255,255,255,0.1);
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
        }

        .channel-card.state-connecting {
            --accent-state: var(--state-connecting-accent);
            --bg-state: var(--state-connecting-bg);
            background: rgba(59, 130, 246, 0.03);
        }

        .channel-card.state-retrying {
            --accent-state: var(--state-retrying-accent);
            --bg-state: var(--state-retrying-bg);
            background: rgba(245, 158, 11, 0.03);
        }

        .channel-card.state-failed {
            --accent-state: var(--state-failed-accent);
            --bg-state: var(--state-failed-bg);
            background: rgba(239, 68, 68, 0.03);
        }

        .channel-card.state-disabled {
            --accent-state: var(--state-disabled-accent);
            --bg-state: var(--state-disabled-bg);
            background: rgba(75, 75, 75, 0.02);
            opacity: 0.6;
        }

        .channel-id {
            font-size: 0.85rem;
            font-weight: 700;
            color: var(--text-muted);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

        .channel-state-badge {
            font-size: 0.75rem;
            font-weight: 600;
            padding: 0.15rem 0.5rem;
            border-radius: 20px;
            background: var(--bg-state, var(--state-idle-bg));
            color: var(--accent-state, var(--state-idle-accent));
            align-self: flex-start;
        }

        .channel-detail {
            font-size: 0.75rem;
            color: var(--text-muted);
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            margin-top: 0.25rem;
        }

        .channel-duration {
            font-size: 0.7rem;
            color: var(--text-muted);
            margin-top: auto;
        }

        /* Form styling */
        .form-group {
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
            overflow-y: auto;
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
        .modal {
            display: none;
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: rgba(0,0,0,0.7);
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
            display: flex;
            flex-direction: column;
            gap: 1rem;
        }

        .modal-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 1px solid var(--border-color);
            padding-bottom: 0.5rem;
        }

        .channels-table-container {
            max-height: 350px;
            overflow-y: auto;
            border: 1px solid var(--border-color);
            border-radius: 8px;
            background: rgba(0,0,0,0.2);
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
            color: var(--text-muted);
            font-weight: 600;
        }

        .checkbox-container {
            display: flex;
            align-items: center;
            gap: 0.5rem;
            cursor: pointer;
        }

        .checkbox-container input {
            cursor: pointer;
        }

        .grid-inline-fields {
            display: flex;
            gap: 0.5rem;
        }

        .modal-footer {
            display: flex;
            justify-content: flex-end;
            gap: 0.75rem;
            margin-top: 0.5rem;
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
        </div>
    </header>

    <div class="container">
        <!-- Control buttons card -->
        <div class="card">
            <div class="card-header">
                <div class="card-title">Runner Control</div>
                <button id="btnToggleChannels" class="channel-editor-btn" onclick="openChannelEditor()">Configure Channels</button>
            </div>
            <div class="controls-row">
                <button id="btnStart" class="btn btn-primary" onclick="startRunner()">Start Keep-Alive</button>
                <button id="btnStop" class="btn btn-danger" onclick="stopRunner()">Stop Keep-Alive</button>
            </div>
        </div>

        <!-- System and Tunarr Controls card -->
        <div class="card">
            <div class="card-header">
                <div class="card-title">System & Service Control</div>
                <span id="tunarrStatusBadge" class="channel-state-badge" style="background:var(--state-idle-bg); color:var(--state-idle-accent);">Tunarr: Checking...</span>
            </div>
            <div style="display:flex; flex-direction:column; gap:1.25rem;">
                <div>
                    <span style="font-size:0.85rem; color:var(--text-muted); display:block; margin-bottom:0.5rem; font-weight:600;">Tunarr Service:</span>
                    <div class="controls-row">
                        <button class="btn btn-primary" onclick="controlTunarr('start')">Start</button>
                        <button class="btn btn-secondary" onclick="controlTunarr('restart')">Restart</button>
                        <button class="btn btn-danger" onclick="controlTunarr('stop')">Stop</button>
                    </div>
                </div>
                <div style="display:grid; grid-template-columns: 1fr 1fr; gap: 1rem;">
                    <div>
                        <span style="font-size:0.85rem; color:var(--text-muted); display:block; margin-bottom:0.5rem; font-weight:600;">PC Server App:</span>
                        <div class="controls-row">
                            <button class="btn btn-secondary" style="width:100%;" onclick="controlPcServer('pcserver/restart')">Restart App</button>
                            <button class="btn btn-danger" style="width:100%;" onclick="controlPcServer('pcserver/close')">Close App</button>
                        </div>
                    </div>
                    <div>
                        <span style="font-size:0.85rem; color:var(--text-muted); display:block; margin-bottom:0.5rem; font-weight:600;">Host Computer Power:</span>
                        <div class="controls-row">
                            <button class="btn btn-secondary" style="border-color:#f59e0b; color:#f59e0b; width:100%;" onclick="controlPcServer('pc/restart', true)">Restart PC</button>
                            <button class="btn btn-danger" style="width:100%;" onclick="controlPcServer('pc/shutdown', true)">Shutdown PC</button>
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
                </div>
                <div id="channelsGrid" class="channels-container">
                    <!-- Loaded dynamically -->
                </div>
            </div>

            <!-- Configuration Section -->
            <div class="card">
                <div class="card-header">
                    <div class="card-title">Settings</div>
                </div>
                <form id="configForm" onsubmit="saveConfig(event)">
                    <div class="form-group">
                        <label for="txtBaseUrl">Tunarr Base Channels URL</label>
                        <input type="text" id="txtBaseUrl" required>
                    </div>

                    <div class="form-row">
                        <div class="form-group">
                            <label for="nudChannelCount">Channel Count</label>
                            <input type="number" id="nudChannelCount" min="1" max="200" required>
                        </div>
                        <div class="form-group">
                            <label for="nudStartupDelay">Startup Delay (s)</label>
                            <input type="number" id="nudStartupDelay" min="0" required>
                        </div>
                    </div>

                    <div class="form-row">
                        <div class="form-group">
                            <label for="nudStaggerDelay">Stagger Delay (ms)</label>
                            <input type="number" id="nudStaggerDelay" min="0" required>
                        </div>
                        <div class="form-group">
                            <label for="nudRetryCount">Retry Count</label>
                            <input type="number" id="nudRetryCount" min="0" required>
                        </div>
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
                                <option value="opencl">opencl</option>
                                <option value="vulkan">vulkan</option>
                            </select>
                        </div>
                        <div class="form-group">
                            <label for="nudThreads">Threads/Proc (0=auto)</label>
                            <input type="number" id="nudThreads" min="0" max="32" required>
                        </div>
                    </div>

                    <div class="form-row">
                        <div class="form-group">
                            <label for="chkWebserver">Web Server Enabled</label>
                            <select id="chkWebserver">
                                <option value="true">Yes</option>
                                <option value="false">No</option>
                            </select>
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
                    </div>

                    <div class="form-group">
                        <label for="txtTunarrExePath">Tunarr Exe Path (for process mode)</label>
                        <input type="text" id="txtTunarrExePath">
                    </div>

                    <div class="form-group">
                        <label for="txtFfmpegPath">FFmpeg Path (Optional)</label>
                        <input type="text" id="txtFfmpegPath">
                    </div>

                    <button type="submit" class="btn btn-primary" style="width: 100%; margin-top: 0.5rem;">Save Settings</button>
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
            </div>
            <p style="font-size:0.8rem; color:var(--text-muted)">Override URLs and retries or enable/disable specific channels.</p>
            
            <div class="channels-table-container">
                <table>
                    <thead>
                        <tr>
                            <th style="width: 80px">ID</th>
                            <th style="width: 80px">Enabled</th>
                            <th>Custom URL (Optional)</th>
                            <th style="width: 120px">Retry Override</th>
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
            </div>
        </div>
    </div>

    <script>
        let currentChannels = [];
        let globalConfig = null;

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
                if (data.isRunning) {
                    indicator.classList.add('active');
                    document.getElementById('btnStart').disabled = true;
                    document.getElementById('btnStop').disabled = false;
                } else {
                    indicator.classList.remove('active');
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
                            stateClass = 'state-connecting';
                            stateName = 'Connecting';
                            break;
                        case 2: // Connected
                            stateClass = 'state-connected';
                            stateName = 'Connected';
                            activeCount++;
                            break;
                        case 3: // Retrying
                            stateClass = 'state-retrying';
                            stateName = 'Retrying';
                            break;
                        case 4: // StartFailed
                        case 5: // UnexpectedExit
                            stateClass = 'state-failed';
                            stateName = c.state === 4 ? 'Start Failed' : 'Disconnected';
                            break;
                        case 6: // Canceled
                            stateClass = 'state-idle';
                            stateName = 'Stopped';
                            break;
                        case 7: // Stopped
                            stateClass = 'state-failed';
                            stateName = 'Max Retries';
                            break;
                        case 8: // Disabled
                            stateClass = 'state-disabled';
                            stateName = 'Disabled';
                            enabledCount--; // Don't count disabled
                            break;
                    }

                    const card = document.createElement('div');
                    card.className = `channel-card ${stateClass}`;
                    
                    const durationText = c.runDurationSeconds > 0 
                        ? `${c.runDurationSeconds.toFixed(1)}s` 
                        : '';

                    card.innerHTML = `
                        <div class="channel-id">
                            <span>Ch ${c.channel}</span>
                            <span class="channel-state-badge">${stateName}</span>
                        </div>
                        <div class="channel-detail" title="${c.message || ''}">${c.message || 'Waiting...'}</div>
                        <div class="channel-duration">${durationText}</div>
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
                document.getElementById('txtFfmpegPath').value = config.ffmpegPath || '';
                document.getElementById('txtWebserverPassword').value = config.webserverPassword || '';
                document.getElementById('chkWaitForTunarr').value = config.waitForTunarr.toString();
                document.getElementById('chkTunarrUseService').value = config.tunarrUseService.toString();
                document.getElementById('txtTunarrServiceName').value = config.tunarrServiceName || '';
                document.getElementById('txtTunarrExePath').value = config.tunarrExePath || '';

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
                hwAccel: document.getElementById('cboHwAccel').value,
                threadsPerProcess: parseInt(document.getElementById('nudThreads').value),
                enableWebserver: document.getElementById('chkWebserver').value === 'true',
                webserverPort: parseInt(document.getElementById('nudWebPort').value),
                ffmpegPath: document.getElementById('txtFfmpegPath').value.trim(),
                webserverPassword: document.getElementById('txtWebserverPassword').value,
                waitForTunarr: document.getElementById('chkWaitForTunarr').value === 'true',
                tunarrUseService: document.getElementById('chkTunarrUseService').value === 'true',
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
                    fetchConfig();
                } else {
                    alert('Save failed: ' + data.error);
                }
            } catch (err) {
                alert('Request failed: ' + err);
            }
        }

        async function startRunner() {
            try {
                await fetch('/api/start', { method: 'POST' });
                fetchStatus();
            } catch (err) { alert('Failed to start runner: ' + err); }
        }

        async function stopRunner() {
            try {
                await fetch('/api/stop', { method: 'POST' });
                fetchStatus();
            } catch (err) { alert('Failed to stop runner: ' + err); }
        }

        async function controlTunarr(action) {
            try {
                const res = await fetch(`/api/tunarr/${action}`, { method: 'POST' });
                const data = await res.json();
                if (data.success) {
                    fetchTunarrStatus();
                } else {
                    alert('Action failed');
                }
            } catch (err) { alert('Failed to control Tunarr service: ' + err); }
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
                } else {
                    alert('Action failed');
                }
            } catch (err) { alert('Failed to send PC control request: ' + err); }
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
                } else {
                    badge.innerText = 'Tunarr: Stopped';
                    badge.style.background = 'rgba(239, 68, 68, 0.15)';
                    badge.style.color = '#ef4444';
                }
            } catch (err) {
                const badge = document.getElementById('tunarrStatusBadge');
                badge.innerText = 'Tunarr: Unknown';
                badge.style.background = 'rgba(107, 114, 128, 0.15)';
                badge.style.color = '#9ca3af';
            }
        }

        function clearConsole() {
            document.getElementById('logConsole').innerText = '';
        }

        // Channel Modal Editor
        function openChannelEditor() {
            const modal = document.getElementById('channelModal');
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
                const tr = document.createElement('tr');
                const retryVal = c.retryCount !== null && c.retryCount !== undefined ? c.retryCount : '';
                
                tr.innerHTML = `
                    <td style="font-weight:700">Ch ${c.channelId}</td>
                    <td>
                        <label class="checkbox-container">
                            <input type="checkbox" id="chk_chan_${idx}" ${c.enabled ? 'checked' : ''}>
                        </label>
                    </td>
                    <td>
                        <input type="text" id="url_chan_${idx}" value="${c.url || ''}" placeholder="Default Url" style="padding:0.4rem; font-size:0.8rem;">
                    </td>
                    <td>
                        <input type="number" id="retry_chan_${idx}" value="${retryVal}" placeholder="Global" style="padding:0.4rem; font-size:0.8rem;" min="0">
                    </td>
                `;
                tbody.appendChild(tr);
            });

            modal.classList.add('open');
        }

        function closeChannelEditor() {
            document.getElementById('channelModal').classList.remove('open');
        }

        function saveChannelsEditor() {
            const countInputVal = parseInt(document.getElementById('nudChannelCount').value);
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
            closeChannelEditor();
            alert('Channel configurations updated. Click "Save Settings" to write changes permanently to Registry.');
        }

        // Init
        fetchConfig();
        fetchStatus();
        fetchLogs();
        fetchTunarrStatus();

        // Polling
        setInterval(fetchStatus, 1000);
        setInterval(fetchLogs, 1000);
        setInterval(fetchTunarrStatus, 2000);
    </script>
</body>
</html>
""";
    }
}
