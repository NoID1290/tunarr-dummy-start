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
        txtLog = new RichTextBox();
        lblLog = new Label();
        chkAutoStartOnLaunch = new CheckBox();
        chkStartWithWindows = new CheckBox();
        ofdFfmpeg = new OpenFileDialog();
        ((System.ComponentModel.ISupportInitialize)nudChannelCount).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudStartupDelay).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudStaggerDelay).BeginInit();
        ((System.ComponentModel.ISupportInitialize)nudRetryCount).BeginInit();
        SuspendLayout();
        // 
        // lblBaseUrl
        // 
        lblBaseUrl.AutoSize = true;
        lblBaseUrl.Location = new Point(12, 15);
        lblBaseUrl.Name = "lblBaseUrl";
        lblBaseUrl.Size = new Size(162, 15);
        lblBaseUrl.TabIndex = 0;
        lblBaseUrl.Text = "Tunarr Base Channels URL:";
        // 
        // txtBaseUrl
        // 
        txtBaseUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtBaseUrl.Location = new Point(180, 12);
        txtBaseUrl.Name = "txtBaseUrl";
        txtBaseUrl.Size = new Size(640, 23);
        txtBaseUrl.TabIndex = 1;
        // 
        // lblChannelCount
        // 
        lblChannelCount.AutoSize = true;
        lblChannelCount.Location = new Point(12, 50);
        lblChannelCount.Name = "lblChannelCount";
        lblChannelCount.Size = new Size(84, 15);
        lblChannelCount.TabIndex = 2;
        lblChannelCount.Text = "Channel Count";
        // 
        // nudChannelCount
        // 
        nudChannelCount.Location = new Point(180, 48);
        nudChannelCount.Maximum = new decimal(new int[] { 200, 0, 0, 0 });
        nudChannelCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        nudChannelCount.Name = "nudChannelCount";
        nudChannelCount.Size = new Size(80, 23);
        nudChannelCount.TabIndex = 3;
        nudChannelCount.Value = new decimal(new int[] { 1, 0, 0, 0 });
        // 
        // lblStartupDelay
        // 
        lblStartupDelay.AutoSize = true;
        lblStartupDelay.Location = new Point(280, 50);
        lblStartupDelay.Name = "lblStartupDelay";
        lblStartupDelay.Size = new Size(143, 15);
        lblStartupDelay.TabIndex = 4;
        lblStartupDelay.Text = "Startup Delay (seconds)";
        // 
        // nudStartupDelay
        // 
        nudStartupDelay.Location = new Point(429, 48);
        nudStartupDelay.Maximum = new decimal(new int[] { 7200, 0, 0, 0 });
        nudStartupDelay.Name = "nudStartupDelay";
        nudStartupDelay.Size = new Size(80, 23);
        nudStartupDelay.TabIndex = 5;
        // 
        // lblStaggerDelay
        // 
        lblStaggerDelay.AutoSize = true;
        lblStaggerDelay.Location = new Point(530, 50);
        lblStaggerDelay.Name = "lblStaggerDelay";
        lblStaggerDelay.Size = new Size(134, 15);
        lblStaggerDelay.TabIndex = 6;
        lblStaggerDelay.Text = "Stagger Delay (ms)";
        // 
        // nudStaggerDelay
        // 
        nudStaggerDelay.Location = new Point(670, 48);
        nudStaggerDelay.Maximum = new decimal(new int[] { 600000, 0, 0, 0 });
        nudStaggerDelay.Name = "nudStaggerDelay";
        nudStaggerDelay.Size = new Size(80, 23);
        nudStaggerDelay.TabIndex = 7;
        // 
        // lblRetryCount
        // 
        lblRetryCount.AutoSize = true;
        lblRetryCount.Location = new Point(12, 86);
        lblRetryCount.Name = "lblRetryCount";
        lblRetryCount.Size = new Size(145, 15);
        lblRetryCount.TabIndex = 8;
        lblRetryCount.Text = "Retry Count Per Channel";
        // 
        // nudRetryCount
        // 
        nudRetryCount.Location = new Point(180, 84);
        nudRetryCount.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
        nudRetryCount.Name = "nudRetryCount";
        nudRetryCount.Size = new Size(80, 23);
        nudRetryCount.TabIndex = 9;
        // 
        // lblFfmpegPath
        // 
        lblFfmpegPath.AutoSize = true;
        lblFfmpegPath.Location = new Point(280, 86);
        lblFfmpegPath.Name = "lblFfmpegPath";
        lblFfmpegPath.Size = new Size(116, 15);
        lblFfmpegPath.TabIndex = 10;
        lblFfmpegPath.Text = "FFmpeg Path (opt.)";
        // 
        // txtFfmpegPath
        // 
        txtFfmpegPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtFfmpegPath.Location = new Point(402, 83);
        txtFfmpegPath.Name = "txtFfmpegPath";
        txtFfmpegPath.Size = new Size(350, 23);
        txtFfmpegPath.TabIndex = 11;
        // 
        // btnBrowseFfmpeg
        // 
        btnBrowseFfmpeg.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowseFfmpeg.Location = new Point(758, 83);
        btnBrowseFfmpeg.Name = "btnBrowseFfmpeg";
        btnBrowseFfmpeg.Size = new Size(62, 23);
        btnBrowseFfmpeg.TabIndex = 12;
        btnBrowseFfmpeg.Text = "Browse";
        btnBrowseFfmpeg.UseVisualStyleBackColor = true;
        btnBrowseFfmpeg.Click += btnBrowseFfmpeg_Click;
        // 
        // btnStart
        // 
        btnStart.Location = new Point(12, 120);
        btnStart.Name = "btnStart";
        btnStart.Size = new Size(96, 30);
        btnStart.TabIndex = 13;
        btnStart.Text = "Start";
        btnStart.UseVisualStyleBackColor = true;
        btnStart.Click += btnStart_Click;
        // 
        // btnStop
        // 
        btnStop.Location = new Point(114, 120);
        btnStop.Name = "btnStop";
        btnStop.Size = new Size(96, 30);
        btnStop.TabIndex = 14;
        btnStop.Text = "Stop";
        btnStop.UseVisualStyleBackColor = true;
        btnStop.Click += btnStop_Click;
        // 
        // btnSaveConfig
        // 
        btnSaveConfig.Location = new Point(216, 120);
        btnSaveConfig.Name = "btnSaveConfig";
        btnSaveConfig.Size = new Size(125, 30);
        btnSaveConfig.TabIndex = 15;
        btnSaveConfig.Text = "Save Config";
        btnSaveConfig.UseVisualStyleBackColor = true;
        btnSaveConfig.Click += btnSaveConfig_Click;
        // 
        // chkAutoStartOnLaunch
        // 
        chkAutoStartOnLaunch.AutoSize = true;
        chkAutoStartOnLaunch.Location = new Point(360, 127);
        chkAutoStartOnLaunch.Name = "chkAutoStartOnLaunch";
        chkAutoStartOnLaunch.Size = new Size(146, 19);
        chkAutoStartOnLaunch.TabIndex = 16;
        chkAutoStartOnLaunch.Text = "Auto Start At Launch";
        chkAutoStartOnLaunch.UseVisualStyleBackColor = true;
        // 
        // chkStartWithWindows
        // 
        chkStartWithWindows.AutoSize = true;
        chkStartWithWindows.Location = new Point(520, 127);
        chkStartWithWindows.Name = "chkStartWithWindows";
        chkStartWithWindows.Size = new Size(161, 19);
        chkStartWithWindows.TabIndex = 17;
        chkStartWithWindows.Text = "Start With Windows Login";
        chkStartWithWindows.UseVisualStyleBackColor = true;
        // 
        // txtLog
        // 
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.BackColor = Color.Black;
        txtLog.ForeColor = Color.Gainsboro;
        txtLog.Location = new Point(12, 205);
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.Size = new Size(808, 333);
        txtLog.TabIndex = 18;
        txtLog.Text = "";
        // 
        // lblLog
        // 
        lblLog.AutoSize = true;
        lblLog.Location = new Point(12, 187);
        lblLog.Name = "lblLog";
        lblLog.Size = new Size(77, 15);
        lblLog.TabIndex = 19;
        lblLog.Text = "Keep-Alive Log";
        // 
        // ofdFfmpeg
        // 
        ofdFfmpeg.DefaultExt = "exe";
        ofdFfmpeg.FileName = "ffmpeg.exe";
        ofdFfmpeg.Filter = "Executable|*.exe|All files|*.*";
        ofdFfmpeg.Title = "Select ffmpeg.exe";
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(832, 550);
        Controls.Add(chkStartWithWindows);
        Controls.Add(chkAutoStartOnLaunch);
        Controls.Add(lblLog);
        Controls.Add(txtLog);
        Controls.Add(btnSaveConfig);
        Controls.Add(btnStop);
        Controls.Add(btnStart);
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
        MinimumSize = new Size(840, 480);
        Name = "Form1";
        Text = "Tunarr Dummy Starter (FFmpeg Keep-Alive)";
        FormClosing += Form1_FormClosing;
        Load += Form1_Load;
        Resize += Form1_Resize;
        ((System.ComponentModel.ISupportInitialize)nudChannelCount).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudStartupDelay).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudStaggerDelay).EndInit();
        ((System.ComponentModel.ISupportInitialize)nudRetryCount).EndInit();
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
    private RichTextBox txtLog;
    private Label lblLog;
    private CheckBox chkAutoStartOnLaunch;
    private CheckBox chkStartWithWindows;
    private OpenFileDialog ofdFfmpeg;
}
