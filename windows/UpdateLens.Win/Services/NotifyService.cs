using System.Windows.Forms;

namespace UpdateLens.Win.Services;

/// <summary>Taskbar balloon notifications. No WinRT dependency.</summary>
public static class NotifyService
{
    private static NotifyIcon? _icon;

    public static void Ensure()
    {
        if (_icon != null) return;
        _icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "UpdateLens",
            Visible = true,
        };
    }

    public static void Balloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try
        {
            Ensure();
            _icon!.ShowBalloonTip(5000, title, text, icon);
        }
        catch { }
    }

    public static void Shutdown()
    {
        try { if (_icon != null) { _icon.Visible = false; _icon.Dispose(); _icon = null; } }
        catch { }
    }
}
