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
            InitializeCustomControls();
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

            _tmrTunarrStatus.Stop();
            _tmrTunarrStatus.Dispose();

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
            _currentConfig.WebserverPassword = txtPassword.Text;
            _currentConfig.WaitForTunarr = chkWaitForTunarr.Checked;
            _currentConfig.TunarrUseService = chkTunarrUseService.Checked;
            _currentConfig.TunarrServiceName = txtTunarrServiceName.Text.Trim();
            _currentConfig.TunarrExePath = txtTunarrExePath.Text.Trim();

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
            txtPassword.Text = config.WebserverPassword;
            chkWaitForTunarr.Checked = config.WaitForTunarr;
            chkTunarrUseService.Checked = config.TunarrUseService;
            txtTunarrServiceName.Text = config.TunarrServiceName;
            txtTunarrExePath.Text = config.TunarrExePath;
            txtTunarrServiceName.Enabled = config.TunarrUseService;
            txtTunarrExePath.Enabled = !config.TunarrUseService;
            btnBrowseTunarr.Enabled = !config.TunarrUseService;
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

        // ── Custom Dynamic Controls for Tunarr & PC Control ──────────────────
        private GroupBox grpTunarrAndSecurity = null!;
        private Label lblPassword = null!;
        private TextBox txtPassword = null!;
        private CheckBox chkWaitForTunarr = null!;
        private CheckBox chkTunarrUseService = null!;
        private Label lblTunarrServiceName = null!;
        private TextBox txtTunarrServiceName = null!;
        private Label lblTunarrExePath = null!;
        private TextBox txtTunarrExePath = null!;
        private Button btnBrowseTunarr = null!;
        private Button btnStartTunarr = null!;
        private Button btnRestartTunarr = null!;
        private Button btnStopTunarr = null!;
        private Label lblTunarrStatus = null!;
        private Button btnRestartApp = null!;
        private Button btnCloseApp = null!;
        private Button btnRestartPC = null!;
        private Button btnShutdownPC = null!;
        private readonly System.Windows.Forms.Timer _tmrTunarrStatus = new();

        private void InitializeCustomControls()
        {
            int shiftAmount = 140;

            lblChannelStatus.Top += shiftAmount;
            flpChannelStatus.Top += shiftAmount;
            pnlSep3.Top += shiftAmount;
            lblLog.Top += shiftAmount;
            txtLog.Top += shiftAmount;
            txtLog.Height -= shiftAmount;

            this.Height += shiftAmount;
            txtLog.Height += shiftAmount;

            Color cBack      = Color.FromArgb(28, 28, 28);
            Color cInput     = Color.FromArgb(45, 45, 45);
            Color cText      = Color.FromArgb(220, 220, 220);
            Color cLabel     = Color.FromArgb(185, 185, 185);
            Color cSection   = Color.FromArgb(100, 180, 255);
            Color cSep       = Color.FromArgb(65, 65, 65);
            Color cBtn       = Color.FromArgb(52, 52, 52);
            Color cBtnBorder = Color.FromArgb(80, 80, 80);

            grpTunarrAndSecurity = new GroupBox
            {
                Text = "Security & Service Control",
                Left = 12,
                Top = 206,
                Width = this.ClientSize.Width - 24,
                Height = 130,
                ForeColor = cSection,
                BackColor = cBack,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            lblPassword = new Label { Text = "Webserver Password:", Left = 10, Top = 20, Width = 130, ForeColor = cLabel };
            txtPassword = new TextBox
            {
                Left = 145, Top = 18, Width = 150, PasswordChar = '*',
                BackColor = cInput, ForeColor = cText, BorderStyle = BorderStyle.FixedSingle
            };

            chkWaitForTunarr = new CheckBox { Text = "Wait for Tunarr on Startup", Left = 320, Top = 19, Width = 180, ForeColor = cLabel };

            chkTunarrUseService = new CheckBox { Text = "Use Service instead of Process", Left = 10, Top = 50, Width = 200, ForeColor = cLabel };
            chkTunarrUseService.CheckedChanged += ChkTunarrUseService_CheckedChanged;

            lblTunarrServiceName = new Label { Text = "Service Name:", Left = 220, Top = 52, Width = 80, ForeColor = cLabel };
            txtTunarrServiceName = new TextBox
            {
                Left = 305, Top = 50, Width = 100,
                BackColor = cInput, ForeColor = cText, BorderStyle = BorderStyle.FixedSingle
            };

            lblTunarrExePath = new Label { Text = "Exe Path:", Left = 415, Top = 52, Width = 60, ForeColor = cLabel };
            txtTunarrExePath = new TextBox
            {
                Left = 475, Top = 50, Width = 230,
                BackColor = cInput, ForeColor = cText, BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            btnBrowseTunarr = new Button
            {
                Text = "Browse", Left = 715, Top = 48, Width = 60, Height = 25,
                BackColor = cBtn, ForeColor = cText, FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnBrowseTunarr.FlatAppearance.BorderColor = cBtnBorder;
            btnBrowseTunarr.Click += BtnBrowseTunarr_Click;

            btnStartTunarr = new Button
            {
                Text = "Start Tunarr", Left = 10, Top = 85, Width = 90, Height = 30,
                BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnStartTunarr.Click += (s, e) => StartTunarr();

            btnRestartTunarr = new Button
            {
                Text = "Restart Tunarr", Left = 105, Top = 85, Width = 100, Height = 30,
                BackColor = cBtn, ForeColor = cText, FlatStyle = FlatStyle.Flat
            };
            btnRestartTunarr.FlatAppearance.BorderColor = cBtnBorder;
            btnRestartTunarr.Click += (s, e) => RestartTunarr();

            btnStopTunarr = new Button
            {
                Text = "Stop Tunarr", Left = 210, Top = 85, Width = 90, Height = 30,
                BackColor = Color.FromArgb(180, 40, 30), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnStopTunarr.Click += (s, e) => StopTunarr();

            lblTunarrStatus = new Label { Text = "Status: Unknown", Left = 310, Top = 92, Width = 130, ForeColor = Color.LightGray };

            btnRestartApp = new Button
            {
                Text = "Restart App", Left = 450, Top = 85, Width = 90, Height = 30,
                BackColor = cBtn, ForeColor = cText, FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnRestartApp.FlatAppearance.BorderColor = cBtnBorder;
            btnRestartApp.Click += (s, e) => RestartPcServer();

            btnCloseApp = new Button
            {
                Text = "Close App", Left = 545, Top = 85, Width = 90, Height = 30,
                BackColor = cBtn, ForeColor = cText, FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnCloseApp.FlatAppearance.BorderColor = cBtnBorder;
            btnCloseApp.Click += (s, e) => ClosePcServer();

            btnRestartPC = new Button
            {
                Text = "Restart PC", Left = 660, Top = 85, Width = 90, Height = 30,
                BackColor = Color.FromArgb(180, 80, 0), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnRestartPC.Click += (s, e) => {
                if (MessageBox.Show("Are you sure you want to restart this computer?", "Restart PC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                    RestartComputer();
                }
            };

            btnShutdownPC = new Button
            {
                Text = "Shutdown PC", Left = 755, Top = 85, Width = 90, Height = 30,
                BackColor = Color.FromArgb(180, 40, 30), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnShutdownPC.Click += (s, e) => {
                if (MessageBox.Show("Are you sure you want to shut down this computer?", "Shutdown PC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                    ShutdownComputer();
                }
            };

            grpTunarrAndSecurity.Controls.Add(lblPassword);
            grpTunarrAndSecurity.Controls.Add(txtPassword);
            grpTunarrAndSecurity.Controls.Add(chkWaitForTunarr);
            grpTunarrAndSecurity.Controls.Add(chkTunarrUseService);
            grpTunarrAndSecurity.Controls.Add(lblTunarrServiceName);
            grpTunarrAndSecurity.Controls.Add(txtTunarrServiceName);
            grpTunarrAndSecurity.Controls.Add(lblTunarrExePath);
            grpTunarrAndSecurity.Controls.Add(txtTunarrExePath);
            grpTunarrAndSecurity.Controls.Add(btnBrowseTunarr);
            grpTunarrAndSecurity.Controls.Add(btnStartTunarr);
            grpTunarrAndSecurity.Controls.Add(btnRestartTunarr);
            grpTunarrAndSecurity.Controls.Add(btnStopTunarr);
            grpTunarrAndSecurity.Controls.Add(lblTunarrStatus);
            grpTunarrAndSecurity.Controls.Add(btnRestartApp);
            grpTunarrAndSecurity.Controls.Add(btnCloseApp);
            grpTunarrAndSecurity.Controls.Add(btnRestartPC);
            grpTunarrAndSecurity.Controls.Add(btnShutdownPC);

            this.Controls.Add(grpTunarrAndSecurity);

            _tmrTunarrStatus.Interval = 2000;
            _tmrTunarrStatus.Tick += (s, e) => {
                bool isRunning = IsTunarrRunning();
                lblTunarrStatus.Text = "Status: " + (isRunning ? "Running" : "Stopped");
                lblTunarrStatus.ForeColor = isRunning ? Color.LightGreen : Color.Coral;
            };
            _tmrTunarrStatus.Start();

            int rightEdge = grpTunarrAndSecurity.Width;
            btnShutdownPC.Left = rightEdge - 100;
            btnRestartPC.Left = rightEdge - 195;
            btnCloseApp.Left = rightEdge - 310;
            btnRestartApp.Left = rightEdge - 405;
        }

        private void ChkTunarrUseService_CheckedChanged(object? sender, EventArgs e)
        {
            txtTunarrServiceName.Enabled = chkTunarrUseService.Checked;
            txtTunarrExePath.Enabled = !chkTunarrUseService.Checked;
            btnBrowseTunarr.Enabled = !chkTunarrUseService.Checked;
        }

        private void BtnBrowseTunarr_Click(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Executables|*.exe;*.cmd;*.bat|All files|*.*";
                ofd.Title = "Select Tunarr Executable";
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    txtTunarrExePath.Text = ofd.FileName;
                }
            }
        }

        public void StartTunarr()
        {
            try
            {
                if (_currentConfig.TunarrUseService)
                {
                    string serviceName = string.IsNullOrWhiteSpace(_currentConfig.TunarrServiceName) ? "Tunarr" : _currentConfig.TunarrServiceName;
                    AppendLog($"Starting Tunarr Windows Service: {serviceName}...");
                    RunCommand("cmd.exe", $"/c net start \"{serviceName}\"");
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
                        WorkingDirectory = Path.GetDirectoryName(_currentConfig.TunarrExePath),
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
                    string serviceName = string.IsNullOrWhiteSpace(_currentConfig.TunarrServiceName) ? "Tunarr" : _currentConfig.TunarrServiceName;
                    AppendLog($"Stopping Tunarr Windows Service: {serviceName}...");
                    RunCommand("cmd.exe", $"/c net stop \"{serviceName}\"");
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
                    AppendLog($"Terminated {killed} process(es) matching name '{processName}'.");
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
                    string serviceName = string.IsNullOrWhiteSpace(_currentConfig.TunarrServiceName) ? "Tunarr" : _currentConfig.TunarrServiceName;
                    using var sc = new System.ServiceProcess.ServiceController(serviceName);
                    return sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
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
                AppendLog($"Command execution failed: {ex.Message}");
            }
        }

        public void RestartPcServer()
        {
            AppendLog("Restarting Tunarr Dummy Starter application...");
            Application.Restart();
            Environment.Exit(0);
        }

        public void ClosePcServer()
        {
            AppendLog("Exiting Tunarr Dummy Starter application...");
            _allowClose = true;
            Application.Exit();
        }

        public void RestartComputer()
        {
            AppendLog("Restarting computer...");
            Process.Start("shutdown", "/r /t 2");
        }

        public void ShutdownComputer()
        {
            AppendLog("Shutting down computer...");
            Process.Start("shutdown", "/s /t 2");
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

        public string WebserverPassword { get; set; } = string.Empty;

        public bool TunarrUseService { get; set; } = false;

        public string TunarrServiceName { get; set; } = "Tunarr";

        public string TunarrExePath { get; set; } = string.Empty;

        public bool WaitForTunarr { get; set; } = false;

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
                WebserverPassword = string.Empty,
                TunarrUseService = false,
                TunarrServiceName = "Tunarr",
                TunarrExePath = string.Empty,
                WaitForTunarr = false,
                Channels = new List<ChannelConfig>()
            };
        }
    }
}
