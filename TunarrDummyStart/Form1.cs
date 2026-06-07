using System.Diagnostics;
using Microsoft.Win32;

namespace TunarrDummyStart;

public partial class Form1 : Form
{
    private const string StartupRegistryValueName = "TunarrDummyStart";
    private const string ConfigRegistryPath = @"Software\NoID Softwork\TunarrDummyStart";

    private readonly RegistryConfigStore _configStore = new();
    private readonly FfmpegService _ffmpegService = new();
    private readonly ChannelRunnerService _channelRunner;
    private readonly object _processSync = new();
    private readonly Dictionary<int, Process> _activeProcesses = new();
    private readonly Dictionary<int, ChannelStatusCard> _channelStatusCards = new();
    private readonly bool _startHidden;

    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;
    private bool _allowClose;
    private bool _trayHintShown;
    private List<string> _detectedHwAccels = new() { "None" };
    private bool _isLoadingConfig;

    public Form1(bool startHidden = false)
    {
        _startHidden = startHidden;
        InitializeComponent();
        _channelRunner = new ChannelRunnerService(AppendLog, UpdateChannelStatus);
        cboHwAccel.SelectedIndexChanged += cboHwAccel_SelectedIndexChanged;
        InitializeTrayIcon();
    }

    private void Form1_Load(object sender, EventArgs e)
    {
        _isLoadingConfig = true;
        AppConfig config = _configStore.LoadConfig(AppendLog);
        ApplyConfigToUi(config);
        InitializeChannelStatusCards(config.ChannelCount);
        _configStore.ApplyWindowsStartupSetting(config.StartWithWindows, writeLog: false, AppendLog);
        SetRunningState(isRunning: false);
        _ = DetectAndPopulateHwAccelsAsync(config.HwAccel);

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

    private async void btnDetectHwAccel_Click(object sender, EventArgs e)
    {
        await DetectAndPopulateHwAccelsAsync(cboHwAccel.SelectedItem as string);
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

        string? ffmpegExecutable = _ffmpegService.ResolveExecutable(config.FfmpegPath);
        if (ffmpegExecutable is null)
        {
            AppendLog("Unable to resolve ffmpeg.exe. Set a valid path or add ffmpeg to PATH.");
            return;
        }

        _configStore.SaveConfig(config);
        _configStore.ApplyWindowsStartupSetting(config.StartWithWindows, writeLog: true, AppendLog);

        _runCancellation = new CancellationTokenSource();
        CancellationToken token = _runCancellation.Token;
        SetRunningState(isRunning: true);
        InitializeChannelStatusCards(config.ChannelCount);

        _runTask = _channelRunner.RunKeepAliveLoopAsync(config, ffmpegExecutable, token);
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
            _channelRunner.KillAllActiveProcesses();
            _runCancellation?.Dispose();
            _runCancellation = null;
            _runTask = null;
            SetRunningState(isRunning: false);
        }
    }

    private void btnStop_Click(object sender, EventArgs e)
    {
        _runCancellation?.Cancel();
        _channelRunner.KillAllActiveProcesses();
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

        _configStore.SaveConfig(config);
        _configStore.ApplyWindowsStartupSetting(config.StartWithWindows, writeLog: true, AppendLog);
        AppendLog($"Config saved to Windows Registry (HKCU\\{ConfigRegistryPath})");
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
            _channelRunner.KillAllActiveProcesses();
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
        InitializeChannelStatusCards(config.ChannelCount);

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
        string url = FfmpegService.BuildChannelUrl(config.BaseUrl, channel);
        int maxAttempts = config.RetryCount + 1;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            UpdateChannelStatus(channel, new ChannelStatusUpdate(channel, ChannelRunState.Connecting, attempt, maxAttempts, $"Connecting to {url}", null, false, TimeSpan.Zero, DateTimeOffset.Now));
            AppendLog($"Channel {channel}: connecting (attempt {attempt}/{maxAttempts}) -> {url}");

            ChannelRunResult result = await RunPersistentClientOnceAsync(channel, config, ffmpegExecutable, url, token);
            if (result.State == ChannelRunState.Canceled)
            {
                UpdateChannelStatus(channel, new ChannelStatusUpdate(channel, ChannelRunState.Canceled, attempt, maxAttempts, "Stopped", result.ExitCode, result.ConnectionEstablished, result.RunDuration, DateTimeOffset.Now));
                AppendLog($"Channel {channel}: stopped");
                return;
            }

            UpdateChannelStatus(channel, new ChannelStatusUpdate(channel, result.State, attempt, maxAttempts, result.ErrorSummary, result.ExitCode, result.ConnectionEstablished, result.RunDuration, DateTimeOffset.Now));
            AppendLog($"Channel {channel}: disconnected after {result.RunDuration.TotalSeconds:F1}s (exit={result.ExitCode}, connected={result.ConnectionEstablished}) -> {result.ErrorSummary}");

            if (attempt < maxAttempts)
            {
                UpdateChannelStatus(channel, new ChannelStatusUpdate(channel, ChannelRunState.Retrying, attempt, maxAttempts, $"Waiting {config.RetryDelayMs} ms before retry", result.ExitCode, result.ConnectionEstablished, result.RunDuration, DateTimeOffset.Now));
                AppendLog($"Channel {channel}: waiting {config.RetryDelayMs} ms before retry.");
                await Task.Delay(config.RetryDelayMs, token);
            }
        }

        UpdateChannelStatus(channel, new ChannelStatusUpdate(channel, ChannelRunState.Stopped, maxAttempts, maxAttempts, "Skipped after retry limit", null, false, TimeSpan.Zero, DateTimeOffset.Now));
        AppendLog($"Channel {channel}: skipped after retry limit");
    }

    private static string BuildChannelUrl(string baseUrl, int channel)
    {
        return $"{baseUrl.TrimEnd('/')}/{channel}.m3u8";
    }

    private async Task<ChannelRunResult> RunPersistentClientOnceAsync(int channel, AppConfig config, string ffmpegExecutable, string url, CancellationToken token)
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

    private async Task DetectAndPopulateHwAccelsAsync(string? preferredSelection = null)
    {
        _isLoadingConfig = true;
        try
        {
            string? ffmpegExe = _ffmpegService.ResolveExecutable(txtFfmpegPath.Text.Trim());
            List<string> items = new() { "None" };

            if (ffmpegExe is not null)
            {
                AppendLog("Detecting hardware acceleration backends...");
                List<string> backends = await _ffmpegService.DetectHwAccelsAsync(ffmpegExe);
                items.AddRange(backends);
                AppendLog(backends.Count > 0
                    ? $"HW Accel detected: {string.Join(", ", backends)}"
                    : "HW Accel: no additional backends found.");
            }
            else
            {
                AppendLog("HW Accel detection skipped (ffmpeg not found).");
            }

            _detectedHwAccels = items;
            string selection = preferredSelection ?? "None";
            cboHwAccel.Items.Clear();
            foreach (string item in _detectedHwAccels)
            {
                cboHwAccel.Items.Add(item);
            }

            SelectHwAccel(selection);
        }
        finally
        {
            _isLoadingConfig = false;
        }
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
            RetryDelayMs = 1500,
            FfmpegPath = txtFfmpegPath.Text.Trim(),
            AutoStartOnLaunch = chkAutoStartOnLaunch.Checked,
            StartWithWindows = chkStartWithWindows.Checked,
            HwAccel = cboHwAccel.SelectedItem as string ?? "None",
            ThreadsPerProcess = Decimal.ToInt32(nudThreadsPerProcess.Value)
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
        nudThreadsPerProcess.Value = NormalizeNumericValue(nudThreadsPerProcess, config.ThreadsPerProcess);
        SelectHwAccel(config.HwAccel);
    }

    private void InitializeChannelStatusCards(int channelCount)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => InitializeChannelStatusCards(channelCount));
            return;
        }

        flpChannelStatus.SuspendLayout();
        flpChannelStatus.Controls.Clear();
        _channelStatusCards.Clear();

        for (int channel = 1; channel <= channelCount; channel++)
        {
            ChannelStatusCard card = new(channel);
            card.ApplyStatus(new ChannelStatusUpdate(channel, ChannelRunState.Idle, 0, 0, "Waiting to start", null, false, TimeSpan.Zero, DateTimeOffset.Now));
            _channelStatusCards[channel] = card;
            flpChannelStatus.Controls.Add(card);
        }

        flpChannelStatus.ResumeLayout();
    }

    private void UpdateChannelStatus(int channel, ChannelStatusUpdate update)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateChannelStatus(channel, update));
            return;
        }

        if (_channelStatusCards.TryGetValue(channel, out ChannelStatusCard? card))
        {
            card.ApplyStatus(update);
        }
    }

    private void SelectHwAccel(string? desiredSelection)
    {
        if (cboHwAccel.Items.Count == 0)
        {
            return;
        }

        string selection = string.IsNullOrWhiteSpace(desiredSelection) ? "None" : desiredSelection.Trim();
        foreach (object item in cboHwAccel.Items)
        {
            if (item is string text && text.Equals(selection, StringComparison.OrdinalIgnoreCase))
            {
                cboHwAccel.SelectedItem = text;
                return;
            }
        }

        cboHwAccel.SelectedIndex = 0;
    }

    private void cboHwAccel_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isLoadingConfig)
        {
            return;
        }

        _configStore.SaveConfig(ReadConfigFromUi());
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
        AppConfig config = AppConfig.CreateDefault();

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ConfigRegistryPath, writable: false);
            if (key is null)
            {
                SaveConfig(config);
                ApplyConfigToUi(config);
                AppendLog($"Created default config in Windows Registry (HKCU\\{ConfigRegistryPath})");
                return config;
            }

            config = new AppConfig
            {
                BaseUrl = ReadString(key, nameof(AppConfig.BaseUrl), config.BaseUrl),
                ChannelCount = ReadInt(key, nameof(AppConfig.ChannelCount), config.ChannelCount),
                StartupDelaySeconds = ReadInt(key, nameof(AppConfig.StartupDelaySeconds), config.StartupDelaySeconds),
                StaggerDelayMs = ReadInt(key, nameof(AppConfig.StaggerDelayMs), config.StaggerDelayMs),
                RetryCount = ReadInt(key, nameof(AppConfig.RetryCount), config.RetryCount),
                FfmpegPath = ReadString(key, nameof(AppConfig.FfmpegPath), config.FfmpegPath),
                AutoStartOnLaunch = ReadBool(key, nameof(AppConfig.AutoStartOnLaunch), config.AutoStartOnLaunch),
                StartWithWindows = ReadBool(key, nameof(AppConfig.StartWithWindows), config.StartWithWindows),
                HwAccel = ReadString(key, nameof(AppConfig.HwAccel), config.HwAccel),
                ThreadsPerProcess = ReadInt(key, nameof(AppConfig.ThreadsPerProcess), config.ThreadsPerProcess)
            };
        }
        catch (Exception ex)
        {
            config = AppConfig.CreateDefault();
            AppendLog($"Failed to load registry config; using defaults ({ex.Message})");
        }

        ApplyConfigToUi(config);
        AppendLog($"Loaded config from Windows Registry (HKCU\\{ConfigRegistryPath})");
        return config;
    }

    private void SaveConfig(AppConfig config)
    {
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(ConfigRegistryPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException($"Could not open registry key HKCU\\{ConfigRegistryPath} for writing.");
        }

        key.SetValue(nameof(AppConfig.BaseUrl), config.BaseUrl, RegistryValueKind.String);
        key.SetValue(nameof(AppConfig.ChannelCount), config.ChannelCount, RegistryValueKind.DWord);
        key.SetValue(nameof(AppConfig.StartupDelaySeconds), config.StartupDelaySeconds, RegistryValueKind.DWord);
        key.SetValue(nameof(AppConfig.StaggerDelayMs), config.StaggerDelayMs, RegistryValueKind.DWord);
        key.SetValue(nameof(AppConfig.RetryCount), config.RetryCount, RegistryValueKind.DWord);
        key.SetValue(nameof(AppConfig.FfmpegPath), config.FfmpegPath, RegistryValueKind.String);
        key.SetValue(nameof(AppConfig.AutoStartOnLaunch), config.AutoStartOnLaunch ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(nameof(AppConfig.StartWithWindows), config.StartWithWindows ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(nameof(AppConfig.HwAccel), config.HwAccel, RegistryValueKind.String);
        key.SetValue(nameof(AppConfig.ThreadsPerProcess), config.ThreadsPerProcess, RegistryValueKind.DWord);
    }

    private static string ReadString(RegistryKey key, string valueName, string defaultValue)
    {
        string? value = key.GetValue(valueName) as string;
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static int ReadInt(RegistryKey key, string valueName, int defaultValue)
    {
        object? raw = key.GetValue(valueName);
        return raw switch
        {
            int i => i,
            string s when int.TryParse(s, out int parsed) => parsed,
            _ => defaultValue
        };
    }

    private static bool ReadBool(RegistryKey key, string valueName, bool defaultValue)
    {
        object? raw = key.GetValue(valueName);
        return raw switch
        {
            int i => i != 0,
            string s when int.TryParse(s, out int parsed) => parsed != 0,
            string s when bool.TryParse(s, out bool parsed) => parsed,
            _ => defaultValue
        };
    }

    private void SetRunningState(bool isRunning)
    {
        btnStart.Enabled = !isRunning;
        btnStop.Enabled = isRunning;
        btnSaveConfig.Enabled = !isRunning;
        btnBrowseFfmpeg.Enabled = !isRunning;
        btnDetectHwAccel.Enabled = !isRunning;

        txtBaseUrl.Enabled = !isRunning;
        txtFfmpegPath.Enabled = !isRunning;
        nudChannelCount.Enabled = !isRunning;
        nudStartupDelay.Enabled = !isRunning;
        nudStaggerDelay.Enabled = !isRunning;
        nudRetryCount.Enabled = !isRunning;
        nudThreadsPerProcess.Enabled = !isRunning;
        chkAutoStartOnLaunch.Enabled = !isRunning;
        chkStartWithWindows.Enabled = !isRunning;
        cboHwAccel.Enabled = !isRunning;
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
        _channelRunner.KillAllActiveProcesses();
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
}

public sealed class AppConfig
{
    public string BaseUrl { get; set; } = string.Empty;

    public int ChannelCount { get; set; }

    public int StartupDelaySeconds { get; set; }

    public int StaggerDelayMs { get; set; }

    public int RetryCount { get; set; }

    public int RetryDelayMs { get; set; }

    public string FfmpegPath { get; set; } = string.Empty;

    public bool AutoStartOnLaunch { get; set; }

    public bool StartWithWindows { get; set; }

    public string HwAccel { get; set; } = "None";

    public int ThreadsPerProcess { get; set; }

    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            BaseUrl = "http://127.0.0.1:8000/stream/channels",
            ChannelCount = 6,
            StartupDelaySeconds = 2,
            StaggerDelayMs = 500,
            RetryCount = 2,
            RetryDelayMs = 1500,
            FfmpegPath = string.Empty,
            AutoStartOnLaunch = false,
            StartWithWindows = false,
            HwAccel = "None",
            ThreadsPerProcess = 0
        };
    }
}
