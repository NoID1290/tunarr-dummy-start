namespace TunarrDummyStart;

internal sealed class ChannelStatusCard : Panel
{
    private readonly Panel pnlAccent = new();
    private readonly Label lblTitle = new();
    private readonly Label lblState = new();
    private readonly Label lblDetail = new();

    public int ChannelNumber { get; }

    public ChannelStatusCard(int channelNumber)
    {
        ChannelNumber = channelNumber;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.Gainsboro;
        BorderStyle = BorderStyle.None;
        Margin = new Padding(3);
        Padding = new Padding(0);
        Size = new Size(155, 62);

        pnlAccent.Dock = DockStyle.Left;
        pnlAccent.Width = 4;
        pnlAccent.BackColor = Color.Gainsboro;

        lblTitle.AutoSize = false;
        lblTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        lblTitle.Location = new Point(10, 5);
        lblTitle.Size = new Size(141, 16);
        lblTitle.Text = $"Channel {channelNumber}";

        lblState.AutoSize = false;
        lblState.Font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
        lblState.Location = new Point(10, 23);
        lblState.Size = new Size(141, 14);
        lblState.Text = "Idle \u2014 0/0";

        lblDetail.AutoEllipsis = true;
        lblDetail.AutoSize = false;
        lblDetail.Font = new Font("Segoe UI", 7f, FontStyle.Regular);
        lblDetail.Location = new Point(10, 40);
        lblDetail.Size = new Size(141, 16);
        lblDetail.Text = string.Empty;

        Controls.Add(pnlAccent);
        Controls.Add(lblTitle);
        Controls.Add(lblState);
        Controls.Add(lblDetail);

        ApplyTheme(ChannelRunState.Idle);
    }

    public void ApplyStatus(ChannelStatusUpdate update)
    {
        lblTitle.Text = $"Channel {update.Channel}";
        lblState.Text = $"{FullState(update.State)} \u2014 {update.Attempt}/{update.MaxAttempts}";
        lblDetail.Text = update.Message;
        ApplyTheme(update.State);
    }

    private static string FullState(ChannelRunState state) => state switch
    {
        ChannelRunState.Connected      => "Connected",
        ChannelRunState.Connecting     => "Connecting",
        ChannelRunState.Retrying       => "Retrying",
        ChannelRunState.StartFailed    => "Start Failed",
        ChannelRunState.UnexpectedExit => "Disconnected",
        ChannelRunState.Canceled       => "Stopped",
        ChannelRunState.Stopped        => "Max Retries",
        _                              => "Idle"
    };

    private void ApplyTheme(ChannelRunState state)
    {
        Color backColor;
        Color accentColor;

        switch (state)
        {
            case ChannelRunState.Connected:
                backColor = Color.FromArgb(27, 60, 42);
                accentColor = Color.FromArgb(96, 224, 160);
                break;
            case ChannelRunState.Connecting:
                backColor = Color.FromArgb(35, 49, 74);
                accentColor = Color.FromArgb(144, 180, 255);
                break;
            case ChannelRunState.Retrying:
                backColor = Color.FromArgb(79, 58, 18);
                accentColor = Color.FromArgb(255, 211, 117);
                break;
            case ChannelRunState.StartFailed:
            case ChannelRunState.UnexpectedExit:
                backColor = Color.FromArgb(75, 30, 30);
                accentColor = Color.FromArgb(255, 144, 144);
                break;
            case ChannelRunState.Canceled:
            case ChannelRunState.Stopped:
                backColor = Color.FromArgb(45, 45, 45);
                accentColor = Color.FromArgb(188, 188, 188);
                break;
            default:
                backColor = Color.FromArgb(30, 30, 30);
                accentColor = Color.Gainsboro;
                break;
        }

        BackColor = backColor;
        pnlAccent.BackColor = accentColor;
        lblTitle.ForeColor = accentColor;
        lblState.ForeColor = accentColor;
        lblDetail.ForeColor = Color.FromArgb(
            accentColor.R * 70 / 100,
            accentColor.G * 70 / 100,
            accentColor.B * 70 / 100);
    }
}
