using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace TunarrDummyStart;

public partial class Form1 : Form
{
    private const string StartupRegistryValueName = "TunarrDummyStart";

    private readonly string _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    private readonly object _processSync = new();
    private readonly Dictionary<int, Process> _activeProcesses = new();
    private readonly bool _startHidden;

    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;
    private bool _allowClose;
    private bool _trayHintShown;

    public Form1(bool startHidden = false)
    {
        _startHidden = startHidden;
        InitializeComponent();
        InitializeTrayIcon();
    }

    private void Form1_Load(object sender, EventArgs e)
    {
        AppConfig config = LoadConfig();
        ApplyWindowsStartupSetting(config.StartWithWindows, writeLog: false);
        SetRunningState(isRunning: false);

        if (_startHidden)
        {
            HideToTray(showHint: false);
            AppendLog("Started hidden in system tray.");
        }

        if (config.AutoStartOnLaunch)
        {
            AppendLog("Auto-start at launch is enabled. Starting keep-alive automatically.");
            _ = StartRunAsync();
        }
    }

    private void btnBrowseFfmpeg_Click(object sender, EventArgs e)
    {
        if (ofdFfmpeg.ShowDialog(this) == DialogResult.OK)
        {
            txtFfmpegPath.Text = ofdFfmpeg.FileName;
        }
    }

    private async void btnStart_Click(object sender, EventArgs e)
    {
        await StartRunAsync();
    }

    private async Task StartRunAsync()
    {
        if (_runTask is not null && !_runTask.IsCompleted)
        {
            AppendLog("A run is already active.");
            return;
        }

        AppConfig config = ReadConfigFromUi();
        string? validationMessage = ValidateConfig(config);
        if (validationMessage is not null)
        {
            AppendLog($"Config error: {validationMessage}");
            return;
        }

        string? ffmpegExecutable = ResolveFfmpegExecutable(config.FfmpegPath);
        if (ffmpegExecutable is null)
        {
            AppendLog("Unable to resolve ffmpeg.exe. Set a valid path or add ffmpeg to PATH.");
            return;
        }

        SaveConfig(config);
        ApplyWindowsStartupSetting(config.StartWithWindows, writeLog: true);

        _runCancellation = new CancellationTokenSource();
        CancellationToken token = _runCancellation.Token;
        SetRunningState(isRunning: true);

        _runTask = RunKeepAliveLoopAsync(config, ffmpegExecutable, token);
        try
        {
            await _runTask;
        }
        catch (OperationCanceledException)
        {
            AppendLog("Run canceled by user.");
        }
        catch (Exception ex)
        {
            AppendLog($"Unexpected run error: {ex.Message}");
        }
        finally
        {
            KillAllActiveProcesses();
            _runCancellation?.Dispose();
            _runCancellation = null;
            _runTask = null;
            SetRunningState(isRunning: false);
        }
    }

    private void btnStop_Click(object sender, EventArgs e)
    {
        _runCancellation?.Cancel();
        KillAllActiveProcesses();
    }

    private void btnSaveConfig_Click(object sender, EventArgs e)
    {
        AppConfig config = ReadConfigFromUi();
        string? validationMessage = ValidateConfig(config);
        if (validationMessage is not null)
        {
            AppendLog($"Config error: {validationMessage}");
            return;
        }

        SaveConfig(config);
        ApplyWindowsStartupSetting(config.StartWithWindows, writeLog: true);
        AppendLog($"Config saved to {_configPath}");
    }

    private void Form1_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideToTray(showHint: true);
            return;
        }

        if (_runTask is not null && !_runTask.IsCompleted)
        {
            _runCancellation?.Cancel();
            KillAllActiveProcesses();
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
        }
    }

    private void Form1_Resize(object sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray(showHint: true);
        }
    }

    private async Task RunKeepAliveLoopAsync(AppConfig config, string ffmpegExecutable, CancellationToken token)
    {
        AppendLog($"Starting keep-alive run. Channels={config.ChannelCount}, StartupDelay={config.StartupDelaySeconds}s, Stagger={config.StaggerDelayMs}ms, Retry={config.RetryCount}");

        if (config.StartupDelaySeconds > 0)
        {
            AppendLog($"Initial delay: {config.StartupDelaySeconds} second(s)");
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
            // Cancellation is expected when user presses Stop.
        }

        AppendLog("Keep-alive run finished.");
    }

    private async Task RunChannelWorkerAsync(int channel, AppConfig config, string ffmpegExecutable, CancellationToken token)
    {
        string url = BuildChannelUrl(config.BaseUrl, channel);
        int maxAttempts = config.RetryCount + 1;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            AppendLog($"Channel {channel}: connecting (attempt {attempt}/{maxAttempts}) -> {url}");

            ChannelRunResult result = await RunPersistentClientOnceAsync(channel, ffmpegExecutable, url, token);
            if (result.State == ChannelRunState.Canceled)
            {
                AppendLog($"Channel {channel}: stopped");
                return;
            }

            AppendLog($"Channel {channel}: disconnected after {result.RunDuration.TotalSeconds:F1}s (exit={result.ExitCode}, connected={result.ConnectionEstablished}) -> {result.ErrorSummary}");

            if (attempt < maxAttempts)
            {
                await Task.Delay(500, token);
            }
        }

        AppendLog($"Channel {channel}: skipped after retry limit");
    }

    private static string BuildChannelUrl(string baseUrl, int channel)
    {
        return $"{baseUrl.TrimEnd('/')}/{channel}.m3u8";
    }

    private async Task<ChannelRunResult> RunPersistentClientOnceAsync(int channel, string ffmpegExecutable, string url, CancellationToken token)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        ProcessStartInfo startInfo = new()
        {
            FileName = ffmpegExecutable,
            Arguments = $"-hide_banner -loglevel error -nostdin -i \"{url}\" -f null -",
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
        AppendLog($"Channel {channel}: ffmpeg process started (PID {process.Id})");

        Task<string> stdErrTask = process.StandardError.ReadToEndAsync(token);
        bool connectionEstablished = false;

        try
        {
            await Task.Delay(1200, token);
            if (!process.HasExited)
            {
                connectionEstablished = true;
                AppendLog($"Channel {channel}: connection established");
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

    private void KillAllActiveProcesses()
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

    private string? ResolveFfmpegExecutable(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            if (File.Exists(expandedPath))
            {
                return expandedPath;
            }
        }

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        string[] entries = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string entry in entries)
        {
            string candidate = Path.Combine(entry, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private AppConfig ReadConfigFromUi()
    {
        return new AppConfig
        {
            BaseUrl = txtBaseUrl.Text.Trim(),
            ChannelCount = Decimal.ToInt32(nudChannelCount.Value),
            StartupDelaySeconds = Decimal.ToInt32(nudStartupDelay.Value),
            StaggerDelayMs = Decimal.ToInt32(nudStaggerDelay.Value),
            RetryCount = Decimal.ToInt32(nudRetryCount.Value),
            FfmpegPath = txtFfmpegPath.Text.Trim(),
            AutoStartOnLaunch = chkAutoStartOnLaunch.Checked,
            StartWithWindows = chkStartWithWindows.Checked
        };
    }

    private void ApplyConfigToUi(AppConfig config)
    {
        txtBaseUrl.Text = config.BaseUrl;
        nudChannelCount.Value = NormalizeNumericValue(nudChannelCount, config.ChannelCount);
        nudStartupDelay.Value = NormalizeNumericValue(nudStartupDelay, config.StartupDelaySeconds);
        nudStaggerDelay.Value = NormalizeNumericValue(nudStaggerDelay, config.StaggerDelayMs);
        nudRetryCount.Value = NormalizeNumericValue(nudRetryCount, config.RetryCount);
        txtFfmpegPath.Text = config.FfmpegPath;
        chkAutoStartOnLaunch.Checked = config.AutoStartOnLaunch;
        chkStartWithWindows.Checked = config.StartWithWindows;
    }

    private static decimal NormalizeNumericValue(NumericUpDown control, int value)
    {
        decimal decimalValue = value;
        if (decimalValue < control.Minimum)
        {
            return control.Minimum;
        }

        if (decimalValue > control.Maximum)
        {
            return control.Maximum;
        }

        return decimalValue;
    }

    private string? ValidateConfig(AppConfig config)
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

    private AppConfig LoadConfig()
    {
        AppConfig config;
        if (!File.Exists(_configPath))
        {
            config = AppConfig.CreateDefault();
            SaveConfig(config);
            ApplyConfigToUi(config);
            AppendLog($"Created default config at {_configPath}");
            return config;
        }

        try
        {
            string json = File.ReadAllText(_configPath, Encoding.UTF8);
            config = JsonSerializer.Deserialize<AppConfig>(json) ?? AppConfig.CreateDefault();
        }
        catch (Exception ex)
        {
            config = AppConfig.CreateDefault();
            AppendLog($"Failed to parse config; using defaults ({ex.Message})");
        }

        ApplyConfigToUi(config);
        AppendLog($"Loaded config from {_configPath}");
        return config;
    }

    private void SaveConfig(AppConfig config)
    {
        JsonSerializerOptions options = new() { WriteIndented = true };
        string json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(_configPath, json, Encoding.UTF8);
    }

    private void SetRunningState(bool isRunning)
    {
        btnStart.Enabled = !isRunning;
        btnStop.Enabled = isRunning;
        btnSaveConfig.Enabled = !isRunning;
        btnBrowseFfmpeg.Enabled = !isRunning;

        txtBaseUrl.Enabled = !isRunning;
        txtFfmpegPath.Enabled = !isRunning;
        nudChannelCount.Enabled = !isRunning;
        nudStartupDelay.Enabled = !isRunning;
        nudStaggerDelay.Enabled = !isRunning;
        nudRetryCount.Enabled = !isRunning;
        chkAutoStartOnLaunch.Enabled = !isRunning;
        chkStartWithWindows.Enabled = !isRunning;
    }

    private void InitializeTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();

        ToolStripMenuItem openItem = new("Open");
        openItem.Click += (_, _) => RestoreFromTray();

        ToolStripMenuItem quitItem = new("Quit");
        quitItem.Click += (_, _) => QuitFromTray();

        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(quitItem);

        _trayIcon = new NotifyIcon(components)
        {
            Text = "Tunarr Dummy Starter",
            Icon = Icon ?? SystemIcons.Application,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray(bool showHint)
    {
        ShowInTaskbar = false;
        Hide();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = true;

            if (showHint && !_trayHintShown)
            {
                _trayIcon.BalloonTipTitle = "Tunarr Dummy Starter";
                _trayIcon.BalloonTipText = "Still running in system tray. Use tray menu > Quit to exit.";
                _trayIcon.ShowBalloonTip(2000);
                _trayHintShown = true;
            }
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void QuitFromTray()
    {
        _allowClose = true;
        _runCancellation?.Cancel();
        KillAllActiveProcesses();
        Close();
    }

    private void ApplyWindowsStartupSetting(bool enabled, bool writeLog)
    {
        try
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (runKey is null)
            {
                if (writeLog)
                {
                    AppendLog("Could not access Windows startup registry key.");
                }

                return;
            }

            if (enabled)
            {
                string command = $"\"{Application.ExecutablePath}\" --startup";
                runKey.SetValue(StartupRegistryValueName, command, RegistryValueKind.String);

                if (writeLog)
                {
                    AppendLog("Windows startup enabled.");
                }
            }
            else
            {
                if (runKey.GetValue(StartupRegistryValueName) is not null)
                {
                    runKey.DeleteValue(StartupRegistryValueName, throwOnMissingValue: false);
                }

                if (writeLog)
                {
                    AppendLog("Windows startup disabled.");
                }
            }
        }
        catch (Exception ex)
        {
            if (writeLog)
            {
                AppendLog($"Failed to update Windows startup setting: {ex.Message}");
            }
        }
    }

    private void AppendLog(string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
        if (txtLog.InvokeRequired)
        {
            txtLog.BeginInvoke(() =>
            {
                txtLog.AppendText(line);
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            });
            return;
        }

        txtLog.AppendText(line);
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.ScrollToCaret();
    }

    private sealed record ChannelRunResult(ChannelRunState State, int ExitCode, string ErrorSummary, bool ConnectionEstablished, TimeSpan RunDuration);

    private enum ChannelRunState
    {
        UnexpectedExit,
        Canceled,
        StartFailed
    }
}

public sealed class AppConfig
{
    public string BaseUrl { get; set; } = string.Empty;

    public int ChannelCount { get; set; }

    public int StartupDelaySeconds { get; set; }

    public int StaggerDelayMs { get; set; }

    public int RetryCount { get; set; }

    public string FfmpegPath { get; set; } = string.Empty;

    public bool AutoStartOnLaunch { get; set; }

    public bool StartWithWindows { get; set; }

    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            BaseUrl = "http://127.0.0.1:8000/stream/channels",
            ChannelCount = 6,
            StartupDelaySeconds = 2,
            StaggerDelayMs = 500,
            RetryCount = 2,
            FfmpegPath = string.Empty,
            AutoStartOnLaunch = false,
            StartWithWindows = false
        };
    }
}
