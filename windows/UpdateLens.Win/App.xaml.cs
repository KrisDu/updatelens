using System.IO;
using System.Windows;
using System.Windows.Threading;
using UpdateLens.Win.Services;

namespace UpdateLens.Win;

public partial class App : System.Windows.Application
{
    /// <summary>True when launched as "UpdateLens.exe --check" (login / scheduled task).</summary>
    public static bool CheckMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        CheckMode = e.Args.Any(a => string.Equals(a, "--check", StringComparison.OrdinalIgnoreCase));
        NotifyService.Ensure();
        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        NotifyService.Shutdown();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        System.Windows.MessageBox.Show("UpdateLens hit an unexpected error and will stay open.\n\n" + e.Exception.Message
            + "\n\nDetails saved to %LocalAppData%\\UpdateLens\\crash.log",
            "UpdateLens — error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // keep the app alive instead of crashing
    }

    private static void OnDomainUnhandled(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) WriteCrashLog(ex);
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpdateLens");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch { }
    }
}
