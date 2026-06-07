using System.Diagnostics;

namespace TunarrDummyStart;

internal sealed class ChannelRunnerService
{
    private readonly object _processSync = new();
    private readonly Dictionary<int, Process> _activeProcesses = new();
    private readonly Action<string> _log;
    private readonly Action<int, ChannelStatusUpdate> _statusUpdater;
    public ChannelRunnerService(Action<string> log, Action<int, ChannelStatusUpdate> statusUpdater)
    {
        _log = log;
        _statusUpdater = statusUpdater;
    }

    public async Task RunKeepAliveLoopAsync(AppConfig config, string ffmpegExecutable, CancellationToken token)
    {
        _log($"Starting keep-alive run. Channels={config.ChannelCount}, StartupDelay={config.StartupDelaySeconds}s, Stagger={config.StaggerDelayMs}ms, Retry={config.RetryCount}");

        if (config.StartupDelaySeconds > 0)
        {
            _log($"Initial delay: {config.StartupDelaySeconds} second(s)");
            await Task.Delay(TimeSpan.FromSeconds(config.StartupDelaySeconds), token);
        }

        List<Task> workers = new(config.ChannelCount);
        for (int channel = 1; channel <= config.ChannelCount; channel++)
        {
            token.ThrowIfCancellationRequested();
            workers.Add(RunChannelWorkerAsync(channel, config, ffmpegExecutable, token));

            if (channel < config.ChannelCount && config.StaggerDelayMs > 0)
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

    private async Task RunChannelWorkerAsync(int channel, AppConfig config, string ffmpegExecutable, CancellationToken token)
    {
        string url = FfmpegService.BuildChannelUrl(config.BaseUrl, channel);
        int maxAttempts = config.RetryCount + 1;
        TimeSpan retryDelay = TimeSpan.FromMilliseconds(Math.Max(0, config.RetryDelayMs));

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            UpdateStatus(channel, ChannelRunState.Connecting, attempt, maxAttempts, $"Connecting to {url}", null, false, TimeSpan.Zero);
            _log($"Channel {channel}: connecting (attempt {attempt}/{maxAttempts}) -> {url}");

            ChannelRunResult result = await RunPersistentClientOnceAsync(channel, config, ffmpegExecutable, url, attempt, maxAttempts, token);
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

    private async Task<ChannelRunResult> RunPersistentClientOnceAsync(int channel, AppConfig config, string ffmpegExecutable, string url, int attempt, int maxAttempts, CancellationToken token)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        string hwAccelArgs = FfmpegService.BuildHwAccelArgs(config.HwAccel);
        string threadsArg = config.ThreadsPerProcess > 0 ? $"-threads {config.ThreadsPerProcess}" : string.Empty;
        var argParts = new List<string> { "-hide_banner", "-loglevel error", "-nostdin" };
        if (!string.IsNullOrEmpty(threadsArg)) argParts.Add(threadsArg);
        if (!string.IsNullOrEmpty(hwAccelArgs)) argParts.Add(hwAccelArgs);
        argParts.Add($"-i \"{url}\"");
        argParts.Add("-f null -");
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
        _log($"Channel {channel}: ffmpeg process started (PID {process.Id})");

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
            return new ChannelRunResult(ChannelRunState.Canceled, -1, "canceled", connectionEstablished, stopwatch.Elapsed);
        }
        finally
        {
            UnregisterActiveProcess(channel, process);
        }

        string stdErr = await stdErrTask;
        string summarizedError = SummarizeError(stdErr, process.ExitCode);
        stopwatch.Stop();
        return new ChannelRunResult(ChannelRunState.UnexpectedExit, process.ExitCode, summarizedError, connectionEstablished, stopwatch.Elapsed);
    }

    private void UpdateStatus(int channel, ChannelRunState state, int attempt, int maxAttempts, string message, int? exitCode, bool connectionEstablished, TimeSpan runDuration)
    {
        _statusUpdater(channel, new ChannelStatusUpdate(channel, state, attempt, maxAttempts, message, exitCode, connectionEstablished, runDuration, DateTimeOffset.Now));
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