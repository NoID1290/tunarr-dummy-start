using System.Diagnostics;

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

    public static string BuildHwAccelArgs(string? hwAccel)
    {
        if (string.IsNullOrEmpty(hwAccel) || hwAccel.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (hwAccel.Equals("qsv", StringComparison.OrdinalIgnoreCase))
        {
            return "-hwaccel qsv -hwaccel_output_format qsv";
        }

        return $"-hwaccel {hwAccel}";
    }

    public static string BuildChannelUrl(string baseUrl, int channel)
    {
        return $"{baseUrl.TrimEnd('/')}/{channel}.m3u8";
    }
}