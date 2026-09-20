using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#if WINDOWS
using System.ServiceProcess;
#endif

namespace TunarrDummyStart;

public sealed class ServerManager : IDisposable
{
    private readonly ConfigStore _configStore;
    private readonly FfmpegService _ffmpegService = new();
    private readonly ChannelRunnerService _channelRunner;
    private readonly ConcurrentQueue<string> _recentLogs = new();
    private const int MaxLogBufferCount = 500;

    private AppConfig _currentConfig;
    private WebserverService? _webserver;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _isDisposed;

    public event Action<string>? OnLog;
    public event Action<int, ChannelStatusUpdate>? OnChannelStatus;
    public event Action<bool>? OnRunningStateChanged;

    public AppConfig CurrentConfig => _currentConfig;
    public ConfigStore ConfigStore => _configStore;
    public bool IsRunnerRunning => _runTask != null && !_runTask.IsCompleted;
    public WebserverService? Webserver => _webserver;

    public ServerManager(string? customConfigPath = null)
    {
        _configStore = new ConfigStore(customConfigPath);
        _channelRunner = new ChannelRunnerService(AppendLog, (ch, status) =>
        {
            OnChannelStatus?.Invoke(ch, status);
        });

        _currentConfig = _configStore.LoadConfig(AppendLog);
    }

    public void Initialize()
    {
        if (_currentConfig.EnableWebserver)
        {
            StartWebserver();
        }
    }

    public void AppendLog(string message)
    {
        string timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        _recentLogs.Enqueue(timestamped);
        while (_recentLogs.Count > MaxLogBufferCount)
        {
            _recentLogs.TryDequeue(out _);
        }

        OnLog?.Invoke(timestamped);
    }

    public List<string> GetRecentLogs()
    {
        return _recentLogs.ToList();
    }

    public List<ChannelStatusUpdate> GetChannelStatuses()
    {
        return _channelRunner.Statuses.Values.OrderBy(c => c.Channel).ToList();
    }

    public void SaveConfig(AppConfig newConfig)
    {
        _currentConfig = newConfig;
        _configStore.SaveConfig(newConfig, AppendLog);
        _configStore.ApplyStartupSetting(newConfig.StartWithWindows, writeLog: true, AppendLog);

        if (newConfig.EnableWebserver)
        {
            if (_webserver == null || _webserver.Port != newConfig.WebserverPort || !_webserver.IsRunning)
            {
                StartWebserver();
            }
        }
        else
        {
            StopWebserver();
        }
    }

    public void StartWebserver()
    {
        StopWebserver();

        if (!_currentConfig.EnableWebserver) return;

        _webserver = new WebserverService(
            getConfig: () => _currentConfig,
            saveConfig: SaveConfig,
            getChannelStatuses: GetChannelStatuses,
            getLogs: GetRecentLogs,
            startRunner: () => _ = StartRunnerAsync(),
            stopRunner: StopRunner,
            isRunnerRunning: () => IsRunnerRunning,
            logMessage: AppendLog,
            startTunarr: StartTunarr,
            stopTunarr: StopTunarr,
            restartTunarr: RestartTunarr,
            isTunarrRunning: IsTunarrRunning,
            restartPcServer: RestartPcServer,
            closePcServer: ClosePcServer,
            restartComputer: RestartComputer,
            shutdownComputer: ShutdownComputer
        );

        _webserver.Start(_currentConfig.WebserverPort);
    }

    public void StopWebserver()
    {
        _webserver?.Stop();
        _webserver?.Dispose();
        _webserver = null;
    }

    public Task StartRunnerAsync()
    {
        if (IsRunnerRunning)
        {
            AppendLog("A keep-alive run is already active.");
            return Task.CompletedTask;
        }

        string? validationMessage = ValidateConfig(_currentConfig);
        if (validationMessage != null)
        {
            AppendLog($"Config error: {validationMessage}");
            return Task.CompletedTask;
        }

        string? ffmpegExecutable = _ffmpegService.ResolveExecutable(_currentConfig.FfmpegPath);
        if (ffmpegExecutable == null)
        {
            AppendLog("Unable to resolve FFmpeg executable. Make sure ffmpeg is in PATH or specify its path in settings.");
            return Task.CompletedTask;
        }

        _runCancellation = new CancellationTokenSource();
        CancellationToken token = _runCancellation.Token;
        OnRunningStateChanged?.Invoke(true);

        _runTask = Task.Run(async () =>
        {
            try
            {
                await _channelRunner.RunKeepAliveLoopAsync(_currentConfig, ffmpegExecutable, token);
            }
            catch (OperationCanceledException)
            {
                AppendLog("Keep-alive run canceled by user.");
            }
            catch (Exception ex)
            {
                AppendLog($"Unexpected keep-alive error: {ex.Message}");
            }
            finally
            {
                _channelRunner.KillAllActiveProcesses();
                _runCancellation?.Dispose();
                _runCancellation = null;
                _runTask = null;
                OnRunningStateChanged?.Invoke(false);
            }
        }, token);

        return Task.CompletedTask;
    }

    public void StopRunner()
    {
        _runCancellation?.Cancel();
        _channelRunner.KillAllActiveProcesses();
    }

    private static string? ValidateConfig(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            return "Base URL is required.";
        }

        if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out _))
        {
            return "Base URL must be a valid absolute URL.";
        }

        if (config.ChannelCount < 1)
        {
            return "Channel count must be at least 1.";
        }

        if (config.StaggerDelayMs < 0)
        {
            return "Stagger delay cannot be negative.";
        }

        if (config.StartupDelaySeconds < 0)
        {
            return "Startup delay cannot be negative.";
        }

        if (config.RetryCount < 0)
        {
            return "Retry count cannot be negative.";
        }

        if (!string.IsNullOrWhiteSpace(config.FfmpegPath))
        {
            string expandedPath = Environment.ExpandEnvironmentVariables(config.FfmpegPath);
            if (!File.Exists(expandedPath))
            {
                return "Configured FFmpeg path does not exist.";
            }
        }

        return null;
    }

    public void StartTunarr()
    {
        try
        {
            if (_currentConfig.TunarrUseService)
            {
                string serviceName = string.IsNullOrWhiteSpace(_currentConfig.TunarrServiceName) ? "tunarr" : _currentConfig.TunarrServiceName;
                AppendLog($"Starting Tunarr service '{serviceName}'...");

                if (OperatingSystem.IsWindows())
                {
                    RunCommand("cmd.exe", $"/c net start \"{serviceName}\"");
                }
                else
                {
                    RunCommand("systemctl", $"start {serviceName}");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_currentConfig.TunarrExePath))
                {
                    AppendLog("Error: Tunarr executable path is not configured.");
                    return;
                }
                if (!File.Exists(_currentConfig.TunarrExePath))
                {
                    AppendLog($"Error: Tunarr executable not found at {_currentConfig.TunarrExePath}");
                    return;
                }

                AppendLog($"Starting Tunarr process: {_currentConfig.TunarrExePath}...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = _currentConfig.TunarrExePath,
                    WorkingDirectory = Path.GetDirectoryName(_currentConfig.TunarrExePath) ?? "",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to start Tunarr: {ex.Message}");
        }
    }

    public void StopTunarr()
    {
        try
        {
            if (_currentConfig.TunarrUseService)
            {
                string serviceName = string.IsNullOrWhiteSpace(_currentConfig.TunarrServiceName) ? "tunarr" : _currentConfig.TunarrServiceName;
                AppendLog($"Stopping Tunarr service '{serviceName}'...");

                if (OperatingSystem.IsWindows())
                {
                    RunCommand("cmd.exe", $"/c net stop \"{serviceName}\"");
                }
                else
                {
                    RunCommand("systemctl", $"stop {serviceName}");
                }
            }
            else
            {
                AppendLog("Stopping Tunarr process...");
                string processName = "tunarr";
                if (!string.IsNullOrWhiteSpace(_currentConfig.TunarrExePath))
                {
                    processName = Path.GetFileNameWithoutExtension(_currentConfig.TunarrExePath);
                }

                var processes = Process.GetProcessesByName(processName);
                int killed = 0;
                foreach (var p in processes)
                {
                    try
                    {
                        p.Kill(entireProcessTree: true);
                        killed++;
                    }
                    catch { }
                }
                AppendLog($"Terminated {killed} process(es) matching '{processName}'.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to stop Tunarr: {ex.Message}");
        }
    }

    public void RestartTunarr()
    {
        AppendLog("Restarting Tunarr...");
        StopTunarr();
        Thread.Sleep(1500);
        StartTunarr();
    }

    public bool IsTunarrRunning()
    {
        try
        {
            if (_currentConfig.TunarrUseService)
            {
                string serviceName = string.IsNullOrWhiteSpace(_currentConfig.TunarrServiceName) ? "tunarr" : _currentConfig.TunarrServiceName;
#if WINDOWS
                if (OperatingSystem.IsWindows())
                {
                    using var sc = new ServiceController(serviceName);
                    return sc.Status == ServiceControllerStatus.Running;
                }
#endif
                if (OperatingSystem.IsLinux())
                {
                    return CheckCommandSuccess("systemctl", $"is-active --quiet {serviceName}");
                }

                return false;
            }
            else
            {
                string processName = "tunarr";
                if (!string.IsNullOrWhiteSpace(_currentConfig.TunarrExePath))
                {
                    processName = Path.GetFileNameWithoutExtension(_currentConfig.TunarrExePath);
                }
                return Process.GetProcessesByName(processName).Length > 0;
            }
        }
        catch
        {
            return false;
        }
    }

    public void RestartPcServer()
    {
        AppendLog("Restarting Tunarr Dummy Start application...");
        try
        {
            string? execPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(execPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = execPath,
                    Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1)),
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to spawn restart process: {ex.Message}");
        }

        Environment.Exit(0);
    }

    public void ClosePcServer()
    {
        AppendLog("Exiting Tunarr Dummy Start application...");
        StopRunner();
        StopWebserver();
        Environment.Exit(0);
    }

    public void RestartComputer()
    {
        AppendLog("Restarting computer...");
        if (OperatingSystem.IsWindows())
        {
            RunCommand("shutdown", "/r /t 2");
        }
        else
        {
            RunCommand("systemctl", "reboot");
        }
    }

    public void ShutdownComputer()
    {
        AppendLog("Shutting down computer...");
        if (OperatingSystem.IsWindows())
        {
            RunCommand("shutdown", "/s /t 2");
        }
        else
        {
            RunCommand("systemctl", "poweroff");
        }
    }

    private void RunCommand(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!string.IsNullOrWhiteSpace(output)) AppendLog(output.Trim());
                if (!string.IsNullOrWhiteSpace(error)) AppendLog("Error: " + error.Trim());
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Command execution failed ({fileName} {arguments}): {ex.Message}");
        }
    }

    private static bool CheckCommandSuccess(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit();
                return proc.ExitCode == 0;
            }
        }
        catch { }
        return false;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopRunner();
        StopWebserver();
    }
}
