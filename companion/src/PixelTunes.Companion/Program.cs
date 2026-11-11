namespace PixelTunes.Companion;

internal static class Program
{
    public const string Version = "0.4.4";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\PixelTunes.Companion", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("PixelTunes is already running. Look for its icon in the system tray (bottom-right, near the clock).",
                "PixelTunes", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Physical-pixel coordinates are essential: the strip is measured in real screen pixels.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => Log.Error("UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Unobserved task exception", e.Exception); e.SetObserved(); };

        Log.Info($"PixelTunes companion {Version} starting on {Environment.OSVersion}");
        bool openDiagnostics = args.Any(a => a.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase));
        bool openPlacement = args.Any(a => a.Equals("--move-popup", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayApp(openDiagnostics, openPlacement));
        Log.Info("PixelTunes companion exiting");
    }
}
