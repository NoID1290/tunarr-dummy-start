namespace TunarrDummyStart;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        string[] args = Environment.GetCommandLineArgs();
        bool startHidden = args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
        Application.Run(new Form1(startHidden));
    }    
}