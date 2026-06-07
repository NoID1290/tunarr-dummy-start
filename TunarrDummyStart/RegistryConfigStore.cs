using Microsoft.Win32;

namespace TunarrDummyStart;

public sealed class RegistryConfigStore
{
    private const string StartupRegistryValueName = "TunarrDummyStart";
    private const string ConfigRegistryPath = @"Software\NoID Softwork\TunarrDummyStart";

    public AppConfig LoadConfig(Action<string>? log = null)
    {
        AppConfig config = AppConfig.CreateDefault();

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ConfigRegistryPath, writable: false);
            if (key is null)
            {
                SaveConfig(config);
                log?.Invoke($"Created default config in Windows Registry (HKCU\\{ConfigRegistryPath})");
                return config;
            }

            config = new AppConfig
            {
                BaseUrl = ReadString(key, nameof(AppConfig.BaseUrl), config.BaseUrl),
                ChannelCount = ReadInt(key, nameof(AppConfig.ChannelCount), config.ChannelCount),
                StartupDelaySeconds = ReadInt(key, nameof(AppConfig.StartupDelaySeconds), config.StartupDelaySeconds),
                StaggerDelayMs = ReadInt(key, nameof(AppConfig.StaggerDelayMs), config.StaggerDelayMs),
                RetryCount = ReadInt(key, nameof(AppConfig.RetryCount), config.RetryCount),
                RetryDelayMs = ReadInt(key, nameof(AppConfig.RetryDelayMs), config.RetryDelayMs),
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
            log?.Invoke($"Failed to load registry config; using defaults ({ex.Message})");
        }

        log?.Invoke($"Loaded config from Windows Registry (HKCU\\{ConfigRegistryPath})");
        return config;
    }

    public void SaveConfig(AppConfig config)
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
        key.SetValue(nameof(AppConfig.RetryDelayMs), config.RetryDelayMs, RegistryValueKind.DWord);
        key.SetValue(nameof(AppConfig.FfmpegPath), config.FfmpegPath, RegistryValueKind.String);
        key.SetValue(nameof(AppConfig.AutoStartOnLaunch), config.AutoStartOnLaunch ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(nameof(AppConfig.StartWithWindows), config.StartWithWindows ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(nameof(AppConfig.HwAccel), config.HwAccel, RegistryValueKind.String);
        key.SetValue(nameof(AppConfig.ThreadsPerProcess), config.ThreadsPerProcess, RegistryValueKind.DWord);
    }

    public void ApplyWindowsStartupSetting(bool enabled, bool writeLog, Action<string>? log = null)
    {
        try
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (runKey is null)
            {
                if (writeLog)
                {
                    log?.Invoke("Could not access Windows startup registry key.");
                }

                return;
            }

            if (enabled)
            {
                string command = $"\"{Application.ExecutablePath}\" --startup";
                runKey.SetValue(StartupRegistryValueName, command, RegistryValueKind.String);

                if (writeLog)
                {
                    log?.Invoke("Windows startup enabled.");
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
                    log?.Invoke("Windows startup disabled.");
                }
            }
        }
        catch (Exception ex)
        {
            if (writeLog)
            {
                log?.Invoke($"Failed to update Windows startup setting: {ex.Message}");
            }
        }
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
}