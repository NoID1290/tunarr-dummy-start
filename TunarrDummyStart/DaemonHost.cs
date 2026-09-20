using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TunarrDummyStart;

public static class DaemonHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.11";

        string? customConfig = null;
        int? overridePort = null;
        string? overrideUrl = null;
        bool forceStart = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--help", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp(version);
                return 0;
            }

            if (arg.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--version", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Tunarr Dummy Start v{version}");
                return 0;
            }

            if ((arg.Equals("-c", StringComparison.OrdinalIgnoreCase) ||
                 arg.Equals("--config", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                customConfig = args[++i];
                continue;
            }

            if ((arg.Equals("-p", StringComparison.OrdinalIgnoreCase) ||
                 arg.Equals("--port", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int parsedPort))
                {
                    overridePort = parsedPort;
                }
                continue;
            }

            if (arg.Equals("--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                overrideUrl = args[++i];
                continue;
            }

            if (arg.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--start", StringComparison.OrdinalIgnoreCase))
            {
                forceStart = true;
                continue;
            }
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║   Tunarr Dummy Start v{version,-8} [Linux ARM / Cross-Platform]     ║");
        Console.WriteLine("║   Persistent FFmpeg Keep-Alive Daemon & Web Control Panel         ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToUpper();
        string osDesc = RuntimeInformation.OSDescription;
        Console.WriteLine($"Platform: {osDesc} ({arch})");

        var serverManager = new ServerManager(customConfig);

        // Apply CLI overrides
        if (overridePort.HasValue)
        {
            serverManager.CurrentConfig.WebserverPort = overridePort.Value;
        }
        if (!string.IsNullOrWhiteSpace(overrideUrl))
        {
            serverManager.CurrentConfig.BaseUrl = overrideUrl;
        }

        // Subscribe to live logs
        serverManager.OnLog += logLine =>
        {
            Console.WriteLine(logLine);
        };

        serverManager.Initialize();

        var config = serverManager.CurrentConfig;
        Console.WriteLine($"Config file: {serverManager.ConfigStore.ConfigFilePath}");
        Console.WriteLine($"Tunarr Base URL: {config.BaseUrl}");
        Console.WriteLine($"Channels: {config.Channels.Count} (Enabled: {config.Channels.Count(c => c.Enabled)})");

        if (config.EnableWebserver)
        {
            string proto = string.IsNullOrEmpty(config.WebserverPassword) ? "http" : "https";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Web Control Panel available at:");
            Console.WriteLine($"  -> {proto}://localhost:{config.WebserverPort}/");
            foreach (var ip in GetLocalIpAddresses())
            {
                Console.WriteLine($"  -> {proto}://{ip}:{config.WebserverPort}/");
            }
            Console.ResetColor();
        }

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Console.WriteLine("\n[Shutdown] Received interrupt signal. Stopping gracefully...");
            cts.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) =>
        {
            serverManager.Dispose();
        };

        if (forceStart || config.AutoStartOnLaunch)
        {
            Console.WriteLine("[Daemon] Starting keep-alive runner automatically...");
            _ = serverManager.StartRunnerAsync();
        }

        Console.WriteLine("[Daemon] Daemon is running. Press Ctrl+C to stop.\n");

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected on exit
        }

        Console.WriteLine("[Shutdown] Cleaning up processes and stopping web server...");
        serverManager.Dispose();
        Console.WriteLine("[Shutdown] Tunarr Dummy Start exited cleanly.");

        return 0;
    }

    private static void PrintHelp(string version)
    {
        Console.WriteLine($"""
Tunarr Dummy Start v{version} - Cross-Platform Keep-Alive Daemon

Usage:
  TunarrDummyStart [options]

Options:
  -c, --config <path>    Path to config.json file
  -p, --port <number>    Override web server port (default: 1290)
  --url <url>            Override Tunarr base channels URL
  -s, --start            Start keep-alive immediately on launch
  --daemon, --headless   Run in headless daemon mode
  -v, --version          Show version information
  -h, --help             Show this help information

Environment Variables:
  TUNARR_CONFIG_PATH     Optional override for the configuration file path

Examples:
  ./TunarrDummyStart -s
  ./TunarrDummyStart -c /etc/tunarr/config.json -p 8080 -s
""");
    }

    private static List<string> GetLocalIpAddresses()
    {
        var ips = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ua.Address))
                    {
                        ips.Add(ua.Address.ToString());
                    }
                }
            }
        }
        catch { }
        return ips;
    }
}

