using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

#if WINDOWS
using Microsoft.Win32;
#endif

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

        string exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        if (!string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
            string[] entries = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string entry in entries)
            {
                string candidate = Path.Combine(entry, exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        string[] entries = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string entry in entries)
        // Check common Linux / Unix system paths
        if (!OperatingSystem.IsWindows())
        {
            string candidate = Path.Combine(entry, "ffmpeg.exe");
            if (File.Exists(candidate))
            string[] linuxCandidates =
            {
                return candidate;
                "/usr/bin/ffmpeg",
                "/usr/local/bin/ffmpeg",
                "/usr/lib/jellyfin-ffmpeg/ffmpeg",
                "/opt/ffmpeg/bin/ffmpeg",
                "/snap/bin/ffmpeg"
            };

            foreach (string candidate in linuxCandidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public async Task<List<string>> DetectHwAccelsAsync(string ffmpegExe)
    {
        List<string> result = new();
        HashSet<string> knownUseful = new(StringComparer.OrdinalIgnoreCase)
            { "d3d11va", "dxva2", "cuda", "qsv", "opencl", "vulkan" };
        {
            "v4l2m2m", "drm", "vaapi", "rkmpp", "cuda", "d3d11va", "dxva2", "qsv", "opencl", "vulkan"
        };

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

        if (hwAccel.Equals("vaapi", StringComparison.OrdinalIgnoreCase))
        {
            string dev = gpuDeviceIndex.HasValue
                ? $"/dev/dri/renderD{128 + gpuDeviceIndex.Value}"
                : (File.Exists("/dev/dri/renderD128") ? "/dev/dri/renderD128" : string.Empty);
            string devArg = !string.IsNullOrEmpty(dev) ? $" -hwaccel_device {dev}" : string.Empty;
            return $"-hwaccel vaapi{devArg} -hwaccel_output_format vaapi";
        }

        if (hwAccel.Equals("v4l2m2m", StringComparison.OrdinalIgnoreCase))
        {
            return "-hwaccel v4l2m2m";
        }

        if (hwAccel.Equals("rkmpp", StringComparison.OrdinalIgnoreCase))
        {
            return "-hwaccel rkmpp";
        }

        if (hwAccel.Equals("drm", StringComparison.OrdinalIgnoreCase))
        {
            return "-hwaccel drm";
        }

        return $"-hwaccel {hwAccel}{deviceArg}";
    }

    public static List<string> DetectHardwareGpus()
    {
        List<string> gpus = new();
        try

#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            using RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (baseKey != null)
            try
            {
                foreach (string subkeyName in baseKey.GetSubKeyNames())
                using RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (baseKey != null)
                {
                    if (subkeyName.Length == 4 && int.TryParse(subkeyName, out _))
                    foreach (string subkeyName in baseKey.GetSubKeyNames())
                    {
                        using RegistryKey? subkey = baseKey.OpenSubKey(subkeyName);
                        if (subkey != null)
                        if (subkeyName.Length == 4 && int.TryParse(subkeyName, out _))
                        {
                            string? driverDesc = subkey.GetValue("DriverDesc") as string;
                            string? providerName = subkey.GetValue("ProviderName") as string;
                            if (!string.IsNullOrEmpty(driverDesc))
                            using RegistryKey? subkey = baseKey.OpenSubKey(subkeyName);
                            if (subkey != null)
                            {
                                if (driverDesc.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) ||
                                    driverDesc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                                    driverDesc.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
                                    (providerName != null && providerName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) && 
                                     !driverDesc.Contains("Xbox", StringComparison.OrdinalIgnoreCase)))
                                string? driverDesc = subkey.GetValue("DriverDesc") as string;
                                string? providerName = subkey.GetValue("ProviderName") as string;
                                if (!string.IsNullOrEmpty(driverDesc))
                                {
                                    continue;
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
                                gpus.Add(driverDesc);
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
        catch
#endif

        if (OperatingSystem.IsLinux())
        {
            // Ignore registry read errors, fallback to empty list
            try
            {
                // Detect ARM board model (e.g., Raspberry Pi 4/5, Orange Pi 5, Rock 5B)
                if (File.Exists("/proc/device-tree/model"))
                {
                    string model = File.ReadAllText("/proc/device-tree/model").Trim('\0', ' ', '\n', '\r');
                    if (!string.IsNullOrEmpty(model))
                    {
                        gpus.Add($"ARM Platform: {model}");
                    }
                }

                // Detect DRM Render nodes
                if (Directory.Exists("/dev/dri"))
                {
                    string[] renderNodes = Directory.GetFiles("/dev/dri", "renderD*");
                    foreach (string node in renderNodes)
                    {
                        gpus.Add($"DRM Render Node: {node}");
                    }
                }

                // Detect V4L2 M2M hardware encoders/decoders
                // Detect V4L2 M2M hardware devices
                if (Directory.Exists("/dev"))
                {
                    string[] videoNodes = Directory.GetFiles("/dev", "video*");
                    if (videoNodes.Length > 0)
                    {
                        gpus.Add($"V4L2 Video Devices ({videoNodes.Length} found)");
                    }
                }

                // Detect NVIDIA
                if (File.Exists("/dev/nvidia0"))
                {
                    gpus.Add("NVIDIA GPU (/dev/nvidia0)");
                }
            }
            catch
            {
                // Non-fatal fallback
            }
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
        // Multi-GPU check for Windows D3D11VA
        if (detectedGpus.Count > 1 && detectedHwAccels.Contains("d3d11va", StringComparer.OrdinalIgnoreCase))
        {
            return "d3d11va";
        }

        string[] priorities = { "cuda", "d3d11va", "qsv", "dxva2", "opencl", "vulkan" };
        // Prioritize hardware backends. Linux ARM: v4l2m2m, rkmpp, vaapi, cuda. Windows: cuda, d3d11va, qsv.
        string[] priorities = { "v4l2m2m", "rkmpp", "vaapi", "cuda", "d3d11va", "qsv", "dxva2", "drm", "opencl", "vulkan" };
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