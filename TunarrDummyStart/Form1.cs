using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TunarrDummyStart
{
    public partial class Form1 : Form
    {
        private const string StartupRegistryValueName = "TunarrDummyStart";
        private const string ConfigRegistryPath = @"Software\NoID Softwork\TunarrDummyStart";

        private readonly RegistryConfigStore _configStore = new();
        private readonly FfmpegService _ffmpegService = new();
        private readonly ChannelRunnerService _channelRunner;
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

        private AppConfig _currentConfig = AppConfig.CreateDefault();
        private WebserverService? _webserver;
        private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _recentLogs = new();
        private const int MaxLogBufferCount = 500;

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
            _currentConfig = _configStore.LoadConfig(AppendLog);
            ApplyConfigToUi(_currentConfig);
            InitializeChannelStatusCards(_currentConfig.Channels);
            _configStore.ApplyWindowsStartupSetting(_currentConfig.StartWithWindows, writeLog: false, AppendLog);
            SetRunningState(isRunning: false);
            _ = DetectAndPopulateHwAccelsAsync(_currentConfig.HwAccel);

            if (_startHidden)
            {
                HideToTray(showHint: false);
                AppendLog("Started hidden in system tray.");
            }

            StartWebserverIfEnabled(_currentConfig);

            if (_currentConfig.AutoStartOnLaunch)
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
            InitializeChannelStatusCards(config.Channels);

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

            StartWebserverIfEnabled(config);
        }

        private void btnConfigureChannels_Click(object sender, EventArgs e)
        {
            int targetCount = Decimal.ToInt32(nudChannelCount.Value);
            while (_currentConfig.Channels.Count < targetCount)
            {
                int nextId = _currentConfig.Channels.Count > 0 ? _currentConfig.Channels.Max(c => c.ChannelId) + 1 : 1;
                _currentConfig.Channels.Add(new ChannelConfig { ChannelId = nextId, Url = string.Empty, Enabled = true });
            }
            while (_currentConfig.Channels.Count > targetCount)
            {
                _currentConfig.Channels.RemoveAt(_currentConfig.Channels.Count - 1);
            }

            using var form = new ChannelsConfigForm(_currentConfig.Channels);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                _currentConfig.Channels = form.Channels;
                _configStore.SaveConfig(_currentConfig);
                AppendLog("Granular channel configurations updated.");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                HideToTray(showHint: true);
                return;
            }

            _webserver?.Stop();
            _webserver?.Dispose();

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
            int targetCount = Decimal.ToInt32(nudChannelCount.Value);
            while (_currentConfig.Channels.Count < targetCount)
            {
                int nextId = _currentConfig.Channels.Count > 0 ? _currentConfig.Channels.Max(c => c.ChannelId) + 1 : 1;
                _currentConfig.Channels.Add(new ChannelConfig { ChannelId = nextId, Url = string.Empty, Enabled = true });
            }
            while (_currentConfig.Channels.Count > targetCount)
            {
                _currentConfig.Channels.RemoveAt(_currentConfig.Channels.Count - 1);
            }

            _currentConfig.BaseUrl = txtBaseUrl.Text.Trim();
            _currentConfig.ChannelCount = targetCount;
            _currentConfig.StartupDelaySeconds = Decimal.ToInt32(nudStartupDelay.Value);
            _currentConfig.StaggerDelayMs = Decimal.ToInt32(nudStaggerDelay.Value);
            _currentConfig.RetryCount = Decimal.ToInt32(nudRetryCount.Value);
            _currentConfig.RetryDelayMs = 1500;
            _currentConfig.FfmpegPath = txtFfmpegPath.Text.Trim();
            _currentConfig.AutoStartOnLaunch = chkAutoStartOnLaunch.Checked;
            _currentConfig.StartWithWindows = chkStartWithWindows.Checked;
            _currentConfig.HwAccel = cboHwAccel.SelectedItem as string ?? "None";
            _currentConfig.ThreadsPerProcess = Decimal.ToInt32(nudThreadsPerProcess.Value);
            _currentConfig.EnableWebserver = chkEnableWebserver.Checked;
            _currentConfig.WebserverPort = Decimal.ToInt32(nudWebserverPort.Value);

            return _currentConfig;
        }

        private void ApplyConfigToUi(AppConfig config)
        {
            _currentConfig = config;
            txtBaseUrl.Text = config.BaseUrl;
            nudChannelCount.Value = NormalizeNumericValue(nudChannelCount, config.ChannelCount);
            nudStartupDelay.Value = NormalizeNumericValue(nudStartupDelay, config.StartupDelaySeconds);
            nudStaggerDelay.Value = NormalizeNumericValue(nudStaggerDelay, config.StaggerDelayMs);
            nudRetryCount.Value = NormalizeNumericValue(nudRetryCount, config.RetryCount);
            txtFfmpegPath.Text = config.FfmpegPath;
            chkAutoStartOnLaunch.Checked = config.AutoStartOnLaunch;
            chkStartWithWindows.Checked = config.StartWithWindows;
            nudThreadsPerProcess.Value = NormalizeNumericValue(nudThreadsPerProcess, config.ThreadsPerProcess);
            chkEnableWebserver.Checked = config.EnableWebserver;
            nudWebserverPort.Value = NormalizeNumericValue(nudWebserverPort, config.WebserverPort);
            SelectHwAccel(config.HwAccel);
        }

        private void InitializeChannelStatusCards(List<ChannelConfig> channels)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => InitializeChannelStatusCards(channels));
                return;
            }

            flpChannelStatus.SuspendLayout();
            
            // Dispose controls explicitly to avoid handle leaks
            foreach (Control control in flpChannelStatus.Controls)
            {
                control.Dispose();
            }
            flpChannelStatus.Controls.Clear();
            _channelStatusCards.Clear();

            foreach (var ch in channels)
            {
                ChannelStatusCard card = new(ch.ChannelId);
                var state = ch.Enabled ? ChannelRunState.Idle : ChannelRunState.Disabled;
                var msg = ch.Enabled ? "Waiting to start" : "Disabled";
                card.ApplyStatus(new ChannelStatusUpdate(ch.ChannelId, state, 0, 0, msg, null, false, TimeSpan.Zero, DateTimeOffset.Now));
                _channelStatusCards[ch.ChannelId] = card;
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

        private void SetRunningState(bool isRunning)
        {
            btnStart.Enabled = !isRunning;
            btnStop.Enabled = isRunning;
            btnSaveConfig.Enabled = !isRunning;
            btnBrowseFfmpeg.Enabled = !isRunning;
            btnDetectHwAccel.Enabled = !isRunning;
            btnConfigureChannels.Enabled = !isRunning;

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
            chkEnableWebserver.Enabled = !isRunning;
            nudWebserverPort.Enabled = !isRunning;
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
            _webserver?.Stop();
            _webserver?.Dispose();
            Close();
        }

        private void AppendLog(string message)
        {
            string cleanLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            _recentLogs.Enqueue(cleanLine);
            while (_recentLogs.Count > MaxLogBufferCount)
            {
                _recentLogs.TryDequeue(out _);
            }

            string line = cleanLine + Environment.NewLine;
            if (txtLog.InvokeRequired)
            {
                txtLog.BeginInvoke(() =>
                {
                    if (txtLog.TextLength > 100000)
                    {
                        txtLog.Select(0, 20000);
                        txtLog.SelectedText = "";
                    }
                    txtLog.AppendText(line);
                    txtLog.SelectionStart = txtLog.TextLength;
                    txtLog.ScrollToCaret();
                });
                return;
            }

            if (txtLog.TextLength > 100000)
            {
                txtLog.Select(0, 20000);
                txtLog.SelectedText = "";
            }
            txtLog.AppendText(line);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private void StartWebserverIfEnabled(AppConfig config)
        {
            _webserver?.Stop();
            _webserver?.Dispose();
            _webserver = null;

            if (config.EnableWebserver)
            {
                _webserver = new WebserverService(
                    getConfig: () => _currentConfig,
                    saveConfig: RemoteSaveConfig,
                    getChannelStatuses: () => _channelRunner.Statuses.Values.OrderBy(c => c.Channel).ToList(),
                    getLogs: GetRecentLogs,
                    startRunner: RemoteStart,
                    stopRunner: RemoteStop,
                    isRunnerRunning: () => _runTask != null && !_runTask.IsCompleted,
                    logMessage: AppendLog
                );
                _webserver.Start(config.WebserverPort);
            }
        }

        public void RemoteStart()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RemoteStart));
                return;
            }
            _ = StartRunAsync();
        }

        public void RemoteStop()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RemoteStop));
                return;
            }
            btnStop_Click(this, EventArgs.Empty);
        }

        public void RemoteSaveConfig(AppConfig newConfig)
        {
            if (newConfig == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<AppConfig>(RemoteSaveConfig), newConfig);
                return;
            }

            _configStore.SaveConfig(newConfig);
            ApplyConfigToUi(newConfig);
            InitializeChannelStatusCards(newConfig.Channels);
            StartWebserverIfEnabled(newConfig);
            AppendLog("Configuration updated and applied remotely.");
        }

        public static List<string> GetRecentLogs()
        {
            return new List<string>(_recentLogs);
        }
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

        public bool EnableWebserver { get; set; } = true;

        public int WebserverPort { get; set; } = 1290;

        public List<ChannelConfig> Channels { get; set; } = new();

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
                ThreadsPerProcess = 0,
                EnableWebserver = true,
                WebserverPort = 1290,
                Channels = new List<ChannelConfig>()
            };
        }
    }
}
