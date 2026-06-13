namespace TunarrDummyStart;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        lblBaseUrl = new Label();
        txtBaseUrl = new TextBox();
        lblChannelCount = new Label();
        nudChannelCount = new NumericUpDown();
        lblStartupDelay = new Label();
        nudStartupDelay = new NumericUpDown();
        lblStaggerDelay = new Label();
        nudStaggerDelay = new NumericUpDown();
        lblRetryCount = new Label();
        nudRetryCount = new NumericUpDown();
        lblFfmpegPath = new Label();
        txtFfmpegPath = new TextBox();
        btnBrowseFfmpeg = new Button();
        btnStart = new Button();
        btnStop = new Button();
        btnSaveConfig = new Button();
        lblChannelStatus = new Label();
        flpChannelStatus = new FlowLayoutPanel();
        txtLog = new RichTextBox();
        lblLog = new Label();
        chkAutoStartOnLaunch = new CheckBox();
        chkStartWithWindows = new CheckBox();
        ofdFfmpeg = new OpenFileDialog();
        lblHwAccel = new Label();
        cboHwAccel = new ComboBox();
        btnDetectHwAccel = new Button();
        lblThreadsPerProcess = new Label();
        nudThreadsPerProcess = new NumericUpDown();
        pnlSep1 = new Panel();
        pnlSep2 = new Panel();
        pnlSep3 = new Panel();
        ((System.ComponentModel.ISupportInitialize)nudChannelCount).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudStartupDelay).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudStaggerDelay).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudRetryCount).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudThreadsPerProcess).BeginInit();
        SuspendLayout();

        // ── Theme colours ──────────────────────────────────────────────────
        Color cBack      = Color.FromArgb(28, 28, 28);
        Color cInput     = Color.FromArgb(45, 45, 45);
        Color cText      = Color.FromArgb(220, 220, 220);
        Color cLabel     = Color.FromArgb(185, 185, 185);
        Color cSection   = Color.FromArgb(100, 180, 255);
        Color cSep       = Color.FromArgb(65, 65, 65);
        Color cBtn       = Color.FromArgb(52, 52, 52);
        Color cBtnBorder = Color.FromArgb(80, 80, 80);

        // ── Row 1: Tunarr Base Channels URL  (Y = 16) ─────────────────────
        lblBaseUrl.AutoSize = true;
        lblBaseUrl.ForeColor = cLabel;
        lblBaseUrl.Location = new Point(12, 19);
        lblBaseUrl.Name = "lblBaseUrl";
        lblBaseUrl.TabIndex = 0;
        lblBaseUrl.Text = "Tunarr Base Channels URL:";

        txtBaseUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtBaseUrl.BackColor = cInput;
        txtBaseUrl.BorderStyle = BorderStyle.FixedSingle;
        txtBaseUrl.ForeColor = cText;
        txtBaseUrl.Location = new Point(180, 16);
        txtBaseUrl.Name = "txtBaseUrl";
        txtBaseUrl.Size = new Size(708, 23);
        txtBaseUrl.TabIndex = 1;

        // ── Row 2: Channel Count / Startup Delay / Stagger Delay  (Y = 50) ─
        lblChannelCount.AutoSize = true;
        lblChannelCount.ForeColor = cLabel;
        lblChannelCount.Location = new Point(12, 54);
        lblChannelCount.Name = "lblChannelCount";
        lblChannelCount.TabIndex = 2;
        lblChannelCount.Text = "Channel Count";

        nudChannelCount.BackColor = cInput;
        nudChannelCount.ForeColor = cText;
        nudChannelCount.Location = new Point(130, 51);
        nudChannelCount.Maximum = new decimal(new int[] { 200, 0, 0, 0 });
        nudChannelCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        nudChannelCount.Name = "nudChannelCount";
        nudChannelCount.Size = new Size(72, 23);
        nudChannelCount.TabIndex = 3;
        nudChannelCount.Value = new decimal(new int[] { 1, 0, 0, 0 });

        lblStartupDelay.AutoSize = true;
        lblStartupDelay.ForeColor = cLabel;
        lblStartupDelay.Location = new Point(218, 54);
        lblStartupDelay.Name = "lblStartupDelay";
        lblStartupDelay.TabIndex = 4;
        lblStartupDelay.Text = "Startup Delay (seconds)";

        nudStartupDelay.BackColor = cInput;
        nudStartupDelay.ForeColor = cText;
        nudStartupDelay.Location = new Point(390, 51);
        nudStartupDelay.Maximum = new decimal(new int[] { 7200, 0, 0, 0 });
        nudStartupDelay.Name = "nudStartupDelay";
        nudStartupDelay.Size = new Size(72, 23);
        nudStartupDelay.TabIndex = 5;

        lblStaggerDelay.AutoSize = true;
        lblStaggerDelay.ForeColor = cLabel;
        lblStaggerDelay.Location = new Point(478, 54);
        lblStaggerDelay.Name = "lblStaggerDelay";
        lblStaggerDelay.TabIndex = 6;
        lblStaggerDelay.Text = "Stagger Delay (ms)";

        nudStaggerDelay.BackColor = cInput;
        nudStaggerDelay.ForeColor = cText;
        nudStaggerDelay.Location = new Point(620, 51);
        nudStaggerDelay.Maximum = new decimal(new int[] { 600000, 0, 0, 0 });
        nudStaggerDelay.Name = "nudStaggerDelay";
        nudStaggerDelay.Size = new Size(80, 23);
        nudStaggerDelay.TabIndex = 7;

        // ── Row 3: Retry Count / FFmpeg Path  (Y = 84) ────────────────────
        lblRetryCount.AutoSize = true;
        lblRetryCount.ForeColor = cLabel;
        lblRetryCount.Location = new Point(12, 88);
        lblRetryCount.Name = "lblRetryCount";
        lblRetryCount.TabIndex = 8;
        lblRetryCount.Text = "Retry Count Per Channel";

        nudRetryCount.BackColor = cInput;
        nudRetryCount.ForeColor = cText;
        nudRetryCount.Location = new Point(178, 85);
        nudRetryCount.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
        nudRetryCount.Name = "nudRetryCount";
        nudRetryCount.Size = new Size(72, 23);
        nudRetryCount.TabIndex = 9;

        lblFfmpegPath.AutoSize = true;
        lblFfmpegPath.ForeColor = cLabel;
        lblFfmpegPath.Location = new Point(265, 88);
        lblFfmpegPath.Name = "lblFfmpegPath";
        lblFfmpegPath.TabIndex = 10;
        lblFfmpegPath.Text = "FFmpeg Path (opt.)";

        txtFfmpegPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtFfmpegPath.BackColor = cInput;
        txtFfmpegPath.BorderStyle = BorderStyle.FixedSingle;
        txtFfmpegPath.ForeColor = cText;
        txtFfmpegPath.Location = new Point(400, 85);
        txtFfmpegPath.Name = "txtFfmpegPath";
        txtFfmpegPath.Size = new Size(428, 23);
        txtFfmpegPath.TabIndex = 11;

        btnBrowseFfmpeg.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowseFfmpeg.BackColor = cBtn;
        btnBrowseFfmpeg.FlatStyle = FlatStyle.Flat;
        btnBrowseFfmpeg.FlatAppearance.BorderColor = cBtnBorder;
        btnBrowseFfmpeg.ForeColor = cText;
        btnBrowseFfmpeg.Location = new Point(834, 84);
        btnBrowseFfmpeg.Name = "btnBrowseFfmpeg";
        btnBrowseFfmpeg.Size = new Size(54, 25);
        btnBrowseFfmpeg.TabIndex = 12;
        btnBrowseFfmpeg.Text = "Browse";
        btnBrowseFfmpeg.UseVisualStyleBackColor = false;
        btnBrowseFfmpeg.Click += btnBrowseFfmpeg_Click;

        // ── Row 4: HW Accel / Threads per Process  (Y = 118) ──────────────
        lblHwAccel.AutoSize = true;
        lblHwAccel.ForeColor = cLabel;
        lblHwAccel.Location = new Point(12, 122);
        lblHwAccel.Name = "lblHwAccel";
        lblHwAccel.TabIndex = 20;
        lblHwAccel.Text = "HW Accel:";

        cboHwAccel.BackColor = cInput;
        cboHwAccel.DropDownStyle = ComboBoxStyle.DropDownList;
        cboHwAccel.FlatStyle = FlatStyle.Flat;
        cboHwAccel.ForeColor = cText;
        cboHwAccel.FormattingEnabled = true;
        cboHwAccel.Items.AddRange(new object[] { "None" });
        cboHwAccel.Location = new Point(80, 118);
        cboHwAccel.Name = "cboHwAccel";
        cboHwAccel.Size = new Size(165, 23);
        cboHwAccel.TabIndex = 21;
        cboHwAccel.SelectedIndex = 0;

        btnDetectHwAccel.BackColor = cBtn;
        btnDetectHwAccel.FlatStyle = FlatStyle.Flat;
        btnDetectHwAccel.FlatAppearance.BorderColor = cBtnBorder;
        btnDetectHwAccel.ForeColor = cText;
        btnDetectHwAccel.Location = new Point(252, 117);
        btnDetectHwAccel.Name = "btnDetectHwAccel";
        btnDetectHwAccel.Size = new Size(78, 25);
        btnDetectHwAccel.TabIndex = 22;
        btnDetectHwAccel.Text = "Detect";
        btnDetectHwAccel.UseVisualStyleBackColor = false;
        btnDetectHwAccel.Click += btnDetectHwAccel_Click;

        lblThreadsPerProcess.AutoSize = true;
        lblThreadsPerProcess.ForeColor = cLabel;
        lblThreadsPerProcess.Location = new Point(346, 122);
        lblThreadsPerProcess.Name = "lblThreadsPerProcess";
        lblThreadsPerProcess.TabIndex = 23;
        lblThreadsPerProcess.Text = "Threads / Process (0 = auto):";

        nudThreadsPerProcess.BackColor = cInput;
        nudThreadsPerProcess.ForeColor = cText;
        nudThreadsPerProcess.Location = new Point(548, 118);
        nudThreadsPerProcess.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
        nudThreadsPerProcess.Name = "nudThreadsPerProcess";
        nudThreadsPerProcess.Size = new Size(65, 23);
        nudThreadsPerProcess.TabIndex = 24;

        // ── Separator 1  (Y = 150) ─────────────────────────────────────────
        pnlSep1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlSep1.BackColor = cSep;
        pnlSep1.Location = new Point(12, 150);
        pnlSep1.Name = "pnlSep1";
        pnlSep1.Size = new Size(876, 1);
        pnlSep1.TabStop = false;

        // ── Row 5: Action buttons + Options  (Y = 158) ────────────────────
        btnStart.BackColor = Color.FromArgb(0, 122, 204);
        btnStart.FlatStyle = FlatStyle.Flat;
        btnStart.FlatAppearance.BorderColor = Color.FromArgb(0, 95, 165);
        btnStart.ForeColor = Color.White;
        btnStart.Location = new Point(12, 158);
        btnStart.Name = "btnStart";
        btnStart.Size = new Size(90, 30);
        btnStart.TabIndex = 13;
        btnStart.Text = "Start";
        btnStart.UseVisualStyleBackColor = false;
        btnStart.Click += btnStart_Click;

        btnStop.BackColor = Color.FromArgb(180, 40, 30);
        btnStop.FlatStyle = FlatStyle.Flat;
        btnStop.FlatAppearance.BorderColor = Color.FromArgb(140, 25, 18);
        btnStop.ForeColor = Color.White;
        btnStop.Location = new Point(108, 158);
        btnStop.Name = "btnStop";
        btnStop.Size = new Size(90, 30);
        btnStop.TabIndex = 14;
        btnStop.Text = "Stop";
        btnStop.UseVisualStyleBackColor = false;
        btnStop.Click += btnStop_Click;

        btnSaveConfig.BackColor = cBtn;
        btnSaveConfig.FlatStyle = FlatStyle.Flat;
        btnSaveConfig.FlatAppearance.BorderColor = cBtnBorder;
        btnSaveConfig.ForeColor = cText;
        btnSaveConfig.Location = new Point(204, 158);
        btnSaveConfig.Name = "btnSaveConfig";
        btnSaveConfig.Size = new Size(110, 30);
        btnSaveConfig.TabIndex = 15;
        btnSaveConfig.Text = "Save Config";
        btnSaveConfig.UseVisualStyleBackColor = false;
        btnSaveConfig.Click += btnSaveConfig_Click;

        chkAutoStartOnLaunch.AutoSize = true;
        chkAutoStartOnLaunch.ForeColor = cLabel;
        chkAutoStartOnLaunch.Location = new Point(330, 164);
        chkAutoStartOnLaunch.Name = "chkAutoStartOnLaunch";
        chkAutoStartOnLaunch.TabIndex = 16;
        chkAutoStartOnLaunch.Text = "Auto Start At Launch";
        chkAutoStartOnLaunch.UseVisualStyleBackColor = true;

        chkStartWithWindows.AutoSize = true;
        chkStartWithWindows.ForeColor = cLabel;
        chkStartWithWindows.Location = new Point(490, 164);
        chkStartWithWindows.Name = "chkStartWithWindows";
        chkStartWithWindows.TabIndex = 17;
        chkStartWithWindows.Text = "Start With Windows Login";
        chkStartWithWindows.UseVisualStyleBackColor = true;

        // ── Separator 2  (Y = 198) ─────────────────────────────────────────
        pnlSep2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlSep2.BackColor = cSep;
        pnlSep2.Location = new Point(12, 198);
        pnlSep2.Name = "pnlSep2";
        pnlSep2.Size = new Size(876, 1);
        pnlSep2.TabStop = false;

        // ── Section header: Channel Status  (Y = 206) ─────────────────────
        lblChannelStatus.AutoSize = true;
        lblChannelStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        lblChannelStatus.ForeColor = cSection;
        lblChannelStatus.Location = new Point(12, 206);
        lblChannelStatus.Name = "lblChannelStatus";
        lblChannelStatus.TabIndex = 18;
        lblChannelStatus.Text = "Channel Status";

        // ── Channel status panel  (Y = 223) ───────────────────────────────
        flpChannelStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        flpChannelStatus.AutoScroll = true;
        flpChannelStatus.BackColor = Color.FromArgb(18, 18, 18);
        flpChannelStatus.BorderStyle = BorderStyle.FixedSingle;
        flpChannelStatus.FlowDirection = FlowDirection.LeftToRight;
        flpChannelStatus.Location = new Point(12, 223);
        flpChannelStatus.Name = "flpChannelStatus";
        flpChannelStatus.Padding = new Padding(3);
        flpChannelStatus.Size = new Size(2000, 76);
        flpChannelStatus.TabIndex = 19;
        flpChannelStatus.WrapContents = false;

        // ── Separator 3  (Y = 307) ─────────────────────────────────────────
        pnlSep3.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnlSep3.BackColor = cSep;
        pnlSep3.Location = new Point(12, 307);
        pnlSep3.Name = "pnlSep3";
        pnlSep3.Size = new Size(876, 1);
        pnlSep3.TabStop = false;

        // ── Section header: Keep-Alive Log  (Y = 315) ─────────────────────
        lblLog.AutoSize = true;
        lblLog.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        lblLog.ForeColor = cSection;
        lblLog.Location = new Point(12, 315);
        lblLog.Name = "lblLog";
        lblLog.TabIndex = 25;
        lblLog.Text = "Keep-Alive Log";

        // ── Log box  (Y = 332) ─────────────────────────────────────────────
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.BackColor = Color.Black;
        txtLog.ForeColor = Color.FromArgb(180, 230, 180);
        txtLog.Location = new Point(12, 332);
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.Size = new Size(876, 266);
        txtLog.TabIndex = 26;
        txtLog.Text = "";

        // ── File dialog ────────────────────────────────────────────────────
        ofdFfmpeg.DefaultExt = "exe";
        ofdFfmpeg.FileName = "ffmpeg.exe";
        ofdFfmpeg.Filter = "Executable|*.exe|All files|*.*";
        ofdFfmpeg.Title = "Select ffmpeg.exe";

        // ── Form ───────────────────────────────────────────────────────────
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = cBack;
        ForeColor = cLabel;
        ClientSize = new Size(900, 610);
        Controls.Add(txtLog);
        Controls.Add(lblLog);
        Controls.Add(pnlSep3);
        Controls.Add(flpChannelStatus);
        Controls.Add(lblChannelStatus);
        Controls.Add(pnlSep2);
        Controls.Add(chkStartWithWindows);
        Controls.Add(chkAutoStartOnLaunch);
        Controls.Add(btnSaveConfig);
        Controls.Add(btnStop);
        Controls.Add(btnStart);
        Controls.Add(pnlSep1);
        Controls.Add(nudThreadsPerProcess);
        Controls.Add(lblThreadsPerProcess);
        Controls.Add(btnDetectHwAccel);
        Controls.Add(cboHwAccel);
        Controls.Add(lblHwAccel);
        Controls.Add(btnBrowseFfmpeg);
        Controls.Add(txtFfmpegPath);
        Controls.Add(lblFfmpegPath);
        Controls.Add(nudRetryCount);
        Controls.Add(lblRetryCount);
        Controls.Add(nudStaggerDelay);
        Controls.Add(lblStaggerDelay);
        Controls.Add(nudStartupDelay);
        Controls.Add(lblStartupDelay);
        Controls.Add(nudChannelCount);
        Controls.Add(lblChannelCount);
        Controls.Add(txtBaseUrl);
        Controls.Add(lblBaseUrl);
        MinimumSize = new Size(860, 520);
        Name = "Form1";
        Text = "Tunarr Dummy Starter \u2014 FFmpeg Keep-Alive";
        FormClosing += Form1_FormClosing;
        Load += Form1_Load;
        Resize += Form1_Resize;
        ((System.ComponentModel.ISupportInitialize)nudChannelCount).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudStartupDelay).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudStaggerDelay).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudRetryCount).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudThreadsPerProcess).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label lblBaseUrl;
    private TextBox txtBaseUrl;
    private Label lblChannelCount;
    private NumericUpDown nudChannelCount;
    private Label lblStartupDelay;
    private NumericUpDown nudStartupDelay;
    private Label lblStaggerDelay;
    private NumericUpDown nudStaggerDelay;
    private Label lblRetryCount;
    private NumericUpDown nudRetryCount;
    private Label lblFfmpegPath;
    private TextBox txtFfmpegPath;
    private Button btnBrowseFfmpeg;
    private Button btnStart;
    private Button btnStop;
    private Button btnSaveConfig;
    private Label lblChannelStatus;
    private FlowLayoutPanel flpChannelStatus;
    private RichTextBox txtLog;
    private Label lblLog;
    private CheckBox chkAutoStartOnLaunch;
    private CheckBox chkStartWithWindows;
    private OpenFileDialog ofdFfmpeg;
    private Label lblHwAccel;
    private ComboBox cboHwAccel;
    private Button btnDetectHwAccel;
    private Label lblThreadsPerProcess;
    private NumericUpDown nudThreadsPerProcess;
    private Panel pnlSep1;
    private Panel pnlSep2;
    private Panel pnlSep3;
}
