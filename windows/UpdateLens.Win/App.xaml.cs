using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace UpdateLens.Win;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        MessageBox.Show("UpdateLens hit an unexpected error and will stay open.\n\n" + e.Exception.Message
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
