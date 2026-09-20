using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TunarrDummyStart;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ConfigFilePath { get; }

    public ConfigStore(string? customConfigPath = null)
    {
        ConfigFilePath = ResolveConfigPath(customConfigPath);
    }

    public static string ResolveConfigPath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(customPath.Trim()));
        }

        string? envPath = Environment.GetEnvironmentVariable("TUNARR_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(envPath.Trim()));
        }

        // Local directory check
        string localConfig = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        if (File.Exists(localConfig))
        {
            return localConfig;
        }

        string appDirConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        if (File.Exists(appDirConfig))
        {
            return appDirConfig;
        }

        // Standard user configuration directory
        // On Linux: ~/.config/TunarrDummyStart/config.json
        // On Windows: %APPDATA%/TunarrDummyStart/config.json
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(appData, "TunarrDummyStart", "config.json");
    }

    public AppConfig LoadConfig(Action<string>? log = null)
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (loaded != null)
                {
                    EnsureDefaultChannels(loaded);
                    log?.Invoke($"Loaded configuration from {ConfigFilePath}");
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Failed reading {ConfigFilePath}: {ex.Message}. Attempting fallback.");
        }

#if WINDOWS
        // If on Windows and no JSON config exists yet, try migrating from Windows Registry
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var registryStore = new RegistryConfigStore();
                AppConfig regConfig = registryStore.LoadConfig(msg => { });
                if (regConfig != null)
                {
                    SaveConfig(regConfig);
                    log?.Invoke($"Migrated configuration from Windows Registry to {ConfigFilePath}");
                    return regConfig;
                }
            }
            catch
            {
                // Registry migration failed or not found, proceed to default
            }
        }
#endif

        var defaultConfig = AppConfig.CreateDefault();
        EnsureDefaultChannels(defaultConfig);
        try
        {
            SaveConfig(defaultConfig);
            log?.Invoke($"Created default configuration at {ConfigFilePath}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"Could not save default configuration: {ex.Message}");
        }

        return defaultConfig;
    }

    public void SaveConfig(AppConfig config, Action<string>? log = null)
    {
        EnsureDefaultChannels(config);

        string? dir = Path.GetDirectoryName(ConfigFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
        log?.Invoke($"Configuration saved to {ConfigFilePath}");

#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var registryStore = new RegistryConfigStore();
                registryStore.SaveConfig(config);
            }
            catch
            {
                // Non-fatal if registry write fails
            }
        }
#endif
    }

    public void ApplyStartupSetting(bool enabled, bool writeLog, Action<string>? log = null)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var registryStore = new RegistryConfigStore();
                registryStore.ApplyWindowsStartupSetting(enabled, writeLog, log);
                return;
            }
            catch (Exception ex)
            {
                if (writeLog) log?.Invoke($"Failed to set Windows startup: {ex.Message}");
                return;
            }
        }
#endif

        if (OperatingSystem.IsLinux())
        {
            try
            {
                string autostartDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", "autostart");
                string desktopFile = Path.Combine(autostartDir, "tunarr-dummy-start.desktop");

                if (enabled)
                {
                    if (!Directory.Exists(autostartDir))
                    {
                        Directory.CreateDirectory(autostartDir);
                    }

                    string execPath = Environment.ProcessPath ?? "TunarrDummyStart";
                    string content = $"""
[Desktop Entry]
Type=Application
Name=Tunarr Dummy Start
Exec={execPath} --daemon
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
""";
                    File.WriteAllText(desktopFile, content);
                    if (writeLog) log?.Invoke($"Linux autostart entry created at {desktopFile}");
                }
                else
                {
                    if (File.Exists(desktopFile))
                    {
                        File.Delete(desktopFile);
                        if (writeLog) log?.Invoke("Linux autostart entry removed.");
                    }
                }
            }
            catch (Exception ex)
            {
                if (writeLog) log?.Invoke($"Failed to configure Linux autostart: {ex.Message}");
            }
        }
    }

    private static void EnsureDefaultChannels(AppConfig config)
    {
        if (config.Channels == null)
        {
            config.Channels = new List<ChannelConfig>();
        }

        if (config.Channels.Count == 0 && config.ChannelCount > 0)
        {
            for (int i = 1; i <= config.ChannelCount; i++)
            {
                config.Channels.Add(new ChannelConfig { ChannelId = i, Url = string.Empty, Enabled = true });
            }
        }
    }
}

