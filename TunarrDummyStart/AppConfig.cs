using System;
using System.Collections.Generic;

namespace TunarrDummyStart;

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

    public string HwAccel { get; set; } = "Auto";

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
            HwAccel = "Auto",
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

