using System.Diagnostics;
using Microsoft.Win32;

namespace TunarrDummyStart;

internal sealed class FfmpegService
{
    public string? ResolveExecutable(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            if (File.Exists(expandedPath))
            {
                return expandedPath;
            }
        }

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        string[] entries = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string entry in entries)
        {
            string candidate = Path.Combine(entry, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task<List<string>> DetectHwAccelsAsync(string ffmpegExe)
    {
        List<string> result = new();
        HashSet<string> knownUseful = new(StringComparer.OrdinalIgnoreCase)
            { "d3d11va", "dxva2", "cuda", "qsv", "opencl", "vulkan" };

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = ffmpegExe,
                Arguments = "-hide_banner -hwaccels",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process proc = new() { StartInfo = psi };
            proc.Start();
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (knownUseful.Contains(trimmed))
                {
                    result.Add(trimmed);
                }
            }
        }
        catch
        {
            // Detection failure is non-fatal.
        }

        return result;
    }

    public static string BuildHwAccelArgs(string? hwAccel, int? gpuDeviceIndex = null)
    {
        if (string.IsNullOrEmpty(hwAccel) || hwAccel.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string deviceArg = gpuDeviceIndex.HasValue ? $" -hwaccel_device {gpuDeviceIndex.Value}" : string.Empty;

        if (hwAccel.Equals("qsv", StringComparison.OrdinalIgnoreCase))
        {
            return $"-hwaccel qsv -hwaccel_output_format qsv{deviceArg}";
        }

        return $"-hwaccel {hwAccel}{deviceArg}";
    }

    public static List<string> DetectHardwareGpus()
    {
        List<string> gpus = new();
        try
        {
            using RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (baseKey != null)
            {
                foreach (string subkeyName in baseKey.GetSubKeyNames())
                {
                    if (subkeyName.Length == 4 && int.TryParse(subkeyName, out _))
                    {
                        using RegistryKey? subkey = baseKey.OpenSubKey(subkeyName);
                        if (subkey != null)
                        {
                            string? driverDesc = subkey.GetValue("DriverDesc") as string;
                            string? providerName = subkey.GetValue("ProviderName") as string;
                            if (!string.IsNullOrEmpty(driverDesc))
                            {
                                if (driverDesc.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) ||
                                    driverDesc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                                    driverDesc.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
                                    (providerName != null && providerName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) && 
                                     !driverDesc.Contains("Xbox", StringComparison.OrdinalIgnoreCase)))
                                {
                                    continue;
                                }
                                gpus.Add(driverDesc);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore registry read errors, fallback to empty list
        }
        return gpus;
    }

    public static string ResolveHwAccel(string? configHwAccel, List<string> detectedHwAccels, List<string> detectedGpus)
    {
        if (string.IsNullOrEmpty(configHwAccel) || configHwAccel.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return "None";
        }

        if (!configHwAccel.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return configHwAccel;
        }

        // If multiple GPUs are detected, prioritize d3d11va because it supports multi-vendor setups (e.g. Intel + NVIDIA)
        if (detectedGpus.Count > 1 && detectedHwAccels.Contains("d3d11va", StringComparer.OrdinalIgnoreCase))
        {
            return "d3d11va";
        }

        string[] priorities = { "cuda", "d3d11va", "qsv", "dxva2", "opencl", "vulkan" };
        foreach (var candidate in priorities)
        {
            if (detectedHwAccels.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return "None";
    }

    public static string BuildChannelUrl(string baseUrl, int channel)
    {
        return $"{baseUrl.TrimEnd('/')}/{channel}.m3u8";
    }
}