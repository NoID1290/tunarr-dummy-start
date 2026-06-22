using System.Diagnostics;

namespace TunarrDummyStart;

internal sealed class ChannelRunnerService
{
    private readonly object _processSync = new();
    private readonly Dictionary<int, Process> _activeProcesses = new();
    private readonly Action<string> _log;
    private readonly Action<int, ChannelStatusUpdate> _statusUpdater;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, ChannelStatusUpdate> _statuses = new();

    public System.Collections.Concurrent.ConcurrentDictionary<int, ChannelStatusUpdate> Statuses => _statuses;

    public ChannelRunnerService(Action<string> log, Action<int, ChannelStatusUpdate> statusUpdater)
    {
        _log = log;
        _statusUpdater = statusUpdater;
    }

    public async Task RunKeepAliveLoopAsync(AppConfig config, string ffmpegExecutable, CancellationToken token)
    {
        var enabledChannels = config.Channels.Where(c => c.Enabled).OrderBy(c => c.ChannelId).ToList();

        List<string> detectedBackends = await new FfmpegService().DetectHwAccelsAsync(ffmpegExecutable);
        List<string> detectedGpus = FfmpegService.DetectHardwareGpus();
        string resolvedHwAccel = FfmpegService.ResolveHwAccel(config.HwAccel, detectedBackends, detectedGpus);
        int gpuCount = detectedGpus.Count;

        _log($"Starting keep-alive run. Channels={config.Channels.Count} (Enabled={enabledChannels.Count}), StartupDelay={config.StartupDelaySeconds}s, Stagger={config.StaggerDelayMs}ms, GlobalRetry={config.RetryCount}");
        _log($"Hardware acceleration: config={config.HwAccel}, resolved={resolvedHwAccel}, detected GPUs={gpuCount} ({string.Join(", ", detectedGpus)})");

        _statuses.Clear();
        foreach (var chan in config.Channels)
        {
            var initialState = chan.Enabled ? ChannelRunState.Idle : ChannelRunState.Disabled;
            var initialMsg = chan.Enabled ? "Waiting to start" : "Disabled";
            var update = new ChannelStatusUpdate(chan.ChannelId, initialState, 0, 0, initialMsg, null, false, TimeSpan.Zero, DateTimeOffset.Now);
            _statuses[chan.ChannelId] = update;
            _statusUpdater(chan.ChannelId, update);
        }

        if (config.WaitForTunarr)
        {
            _log($"Wait for Tunarr startup is enabled. Checking connection to {config.BaseUrl}...");
            try
            {
                var uri = new Uri(config.BaseUrl);
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using (var client = new System.Net.Sockets.TcpClient())
                        {
                            var connectTask = client.ConnectAsync(uri.Host, uri.Port);
                            await Task.WhenAny(connectTask, Task.Delay(2000, token));
                            if (client.Connected)
                            {
                                _log("Tunarr service is responsive and port is listening!");
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // ignore and retry
                    }
                    
                    _log("Tunarr service is not responsive yet. Retrying in 5 seconds...");
                    await Task.Delay(5000, token);
                }
            }
            catch (Exception ex)
            {
                _log($"Error parsing Base URL for startup wait check: {ex.Message}");
            }
        }

        if (config.StartupDelaySeconds > 0)
        {
            _log($"Initial delay: {config.StartupDelaySeconds} second(s)");
            await Task.Delay(TimeSpan.FromSeconds(config.StartupDelaySeconds), token);
        }

        List<Task> workers = new(enabledChannels.Count);
        for (int i = 0; i < enabledChannels.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var chanConfig = enabledChannels[i];
            workers.Add(RunChannelWorkerAsync(chanConfig, config, ffmpegExecutable, resolvedHwAccel, gpuCount, i, token));

            if (i < enabledChannels.Count - 1 && config.StaggerDelayMs > 0)
            {
                await Task.Delay(config.StaggerDelayMs, token);
            }
        }

        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when the user stops the run.
        }

        _log("Keep-alive run finished.");
    }

    public void KillAllActiveProcesses()
    {
        List<Process> processes;
        lock (_processSync)
        {
            processes = _activeProcesses.Values.Distinct().ToList();
        }

        foreach (Process process in processes)
        {
            TryTerminate(process);
        }
    }

    private async Task RunChannelWorkerAsync(ChannelConfig chanConfig, AppConfig config, string ffmpegExecutable, string resolvedHwAccel, int gpuCount, int workerIndex, CancellationToken token)
    {
        int channel = chanConfig.ChannelId;
        string url = !string.IsNullOrWhiteSpace(chanConfig.Url) 
            ? chanConfig.Url.Trim() 
            : FfmpegService.BuildChannelUrl(config.BaseUrl, channel);
            
        int maxAttempts = (chanConfig.RetryCount ?? config.RetryCount) + 1;
        TimeSpan retryDelay = TimeSpan.FromMilliseconds(Math.Max(0, config.RetryDelayMs));
        string currentHwAccel = resolvedHwAccel;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            UpdateStatus(channel, ChannelRunState.Connecting, attempt, maxAttempts, $"Connecting to {url}", null, false, TimeSpan.Zero);
            _log($"Channel {channel}: connecting (attempt {attempt}/{maxAttempts}) -> {url}");

            ChannelRunResult result = await RunPersistentClientOnceAsync(channel, config, ffmpegExecutable, url, currentHwAccel, gpuCount, workerIndex, attempt, maxAttempts, token);
            if (result.State == ChannelRunState.Canceled)
            {
                UpdateStatus(channel, ChannelRunState.Canceled, attempt, maxAttempts, "Stopped", result.ExitCode, result.ConnectionEstablished, result.RunDuration);
                _log($"Channel {channel}: stopped");
                return;
            }

            UpdateStatus(channel, result.State, attempt, maxAttempts, result.ErrorSummary, result.ExitCode, result.ConnectionEstablished, result.RunDuration);
            _log($"Channel {channel}: disconnected after {result.RunDuration.TotalSeconds:F1}s (exit={result.ExitCode}, connected={result.ConnectionEstablished}) -> {result.ErrorSummary}");

            if (attempt < maxAttempts)
            {
                if (!string.Equals(currentHwAccel, "None", StringComparison.OrdinalIgnoreCase))
                {
                    _log($"Channel {channel}: hardware acceleration '{currentHwAccel}' failed or disconnected, falling back to CPU for next attempt.");
                    currentHwAccel = "None";
                }

                string waitText = retryDelay.TotalSeconds >= 1
                    ? $"Waiting {retryDelay.TotalSeconds:F1}s before retry"
                    : $"Waiting {retryDelay.TotalMilliseconds:F0}ms before retry";

                _log($"Channel {channel}: waiting {waitText.Replace("Waiting ", string.Empty)}");
                UpdateStatus(channel, ChannelRunState.Retrying, attempt, maxAttempts, waitText, result.ExitCode, result.ConnectionEstablished, result.RunDuration);
                await Task.Delay(retryDelay, token);
            }
        }

        UpdateStatus(channel, ChannelRunState.Stopped, maxAttempts, maxAttempts, "Skipped after retry limit", null, false, TimeSpan.Zero);
        _log($"Channel {channel}: skipped after retry limit");
    }

    private async Task<ChannelRunResult> RunPersistentClientOnceAsync(
        int channel,
        AppConfig config,
        string ffmpegExecutable,
        string url,
        string resolvedHwAccel,
        int gpuCount,
        int workerIndex,
        int attempt,
        int maxAttempts,
        CancellationToken token)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        int? gpuDeviceIndex = null;
        if (!string.Equals(resolvedHwAccel, "None", StringComparison.OrdinalIgnoreCase))
        {
            gpuDeviceIndex = gpuCount > 1 ? (workerIndex % gpuCount) : null;
        }

        string hwAccelArgs = FfmpegService.BuildHwAccelArgs(resolvedHwAccel, gpuDeviceIndex);
        string threadsArg = config.ThreadsPerProcess > 0 ? $"-threads {config.ThreadsPerProcess}" : string.Empty;
        var argParts = new List<string> { "-hide_banner", "-loglevel error", "-nostdin" };
        if (!string.IsNullOrEmpty(threadsArg)) argParts.Add(threadsArg);
        if (!string.IsNullOrEmpty(hwAccelArgs)) argParts.Add(hwAccelArgs);
        argParts.Add($"-i \"{url}\"");
        
        // Use "-c copy" (stream copy) so FFmpeg ONLY demuxes the streams without decoding them, drastically reducing CPU usage
        argParts.Add("-c copy -f null -");
        string arguments = string.Join(" ", argParts);

        ProcessStartInfo startInfo = new()
        {
            FileName = ffmpegExecutable,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ChannelRunResult(ChannelRunState.StartFailed, -1, $"failed to start ffmpeg: {ex.Message}", false, stopwatch.Elapsed);
        }

        RegisterActiveProcess(channel, process);
        string gpuLogSuffix = gpuDeviceIndex.HasValue ? $" (GPU {gpuDeviceIndex.Value})" : string.Empty;
        _log($"Channel {channel}: ffmpeg process started (PID {process.Id}){gpuLogSuffix} with args: {arguments}");

        Task<string> stdErrTask = process.StandardError.ReadToEndAsync(token);
        bool connectionEstablished = false;

        try
        {
            await Task.Delay(1200, token);
            if (!process.HasExited)
            {
                connectionEstablished = true;
                UpdateStatus(channel, ChannelRunState.Connected, attempt, maxAttempts, "Connection established", null, true, stopwatch.Elapsed);
                _log($"Channel {channel}: connection established");
            }

            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            stopwatch.Stop();
            // Await standard error read to avoid leaking tasks or stream readers
            try { await stdErrTask; } catch { }
            return new ChannelRunResult(ChannelRunState.Canceled, -1, "canceled", connectionEstablished, stopwatch.Elapsed);
        }
        finally
        {
            UnregisterActiveProcess(channel, process);
        }

        string stdErr = "";
        try { stdErr = await stdErrTask; } catch { }
        string summarizedError = SummarizeError(stdErr, process.ExitCode);
        stopwatch.Stop();
        return new ChannelRunResult(ChannelRunState.UnexpectedExit, process.ExitCode, summarizedError, connectionEstablished, stopwatch.Elapsed);
    }

    private void UpdateStatus(int channel, ChannelRunState state, int attempt, int maxAttempts, string message, int? exitCode, bool connectionEstablished, TimeSpan runDuration)
    {
        var update = new ChannelStatusUpdate(channel, state, attempt, maxAttempts, message, exitCode, connectionEstablished, runDuration, DateTimeOffset.Now);
        _statuses[channel] = update;
        _statusUpdater(channel, update);
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore cleanup failures for dummy execution mode.
        }
    }

    private static string SummarizeError(string stdErr, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(stdErr))
        {
            return $"exit code {exitCode}";
        }

        string compact = stdErr.Replace(Environment.NewLine, " ").Trim();
        return compact.Length <= 220 ? compact : compact[..220] + "...";
    }

    private void RegisterActiveProcess(int channel, Process process)
    {
        lock (_processSync)
        {
            _activeProcesses[channel] = process;
        }
    }

    private void UnregisterActiveProcess(int channel, Process process)
    {
        lock (_processSync)
        {
            if (_activeProcesses.TryGetValue(channel, out Process? existing) && existing == process)
            {
                _activeProcesses.Remove(channel);
            }
        }
    }

    private sealed record ChannelRunResult(ChannelRunState State, int ExitCode, string ErrorSummary, bool ConnectionEstablished, TimeSpan RunDuration);
}