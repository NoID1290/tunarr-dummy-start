namespace TunarrDummyStart;

public sealed class ChannelConfig
{
    public int ChannelId { get; set; }
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int? RetryCount { get; set; }
}
