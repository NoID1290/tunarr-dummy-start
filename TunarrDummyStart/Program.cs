using System;
using System.Linq;
using System.Threading.Tasks;

namespace TunarrDummyStart;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
#if WINDOWS
    [STAThread]
    static void Main()
    static int Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        bool isHeadless = args.Any(arg =>
            string.Equals(arg, "--headless", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--daemon", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--cli", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--no-gui", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--version", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-v", StringComparison.OrdinalIgnoreCase));

        if (isHeadless)
        {
            return DaemonHost.RunAsync(args).GetAwaiter().GetResult();
        }

        ApplicationConfiguration.Initialize();
        string[] args = Environment.GetCommandLineArgs();
        bool startHidden = args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        bool startHidden = args.Any(arg =>
            string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        Application.Run(new Form1(startHidden));
    }    
        return 0;
    }
#else
    static async Task<int> Main(string[] args)
    {
        return await DaemonHost.RunAsync(args);
    }
#endif
}