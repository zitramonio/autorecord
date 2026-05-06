using System.Windows;
using Forms = System.Windows.Forms;

namespace Autorecord.App.Tray;

public sealed class TrayIconHost : IDisposable
{
    private readonly Window _window;
    private readonly Forms.NotifyIcon _icon;

    public TrayIconHost(Window window)
    {
        _window = window;
        _icon = new Forms.NotifyIcon
        {
            Text = "Autorecord",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _icon.DoubleClick += (_, _) => ShowWindow();
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowWindow());
        menu.Items.Add("Выход", null, (_, _) =>
        {
            if (_window is MainWindow mainWindow)
            {
                mainWindow.AllowClose = true;
            }

            System.Windows.Application.Current.Shutdown();
        });
        return menu;
    }

    private void ShowWindow()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
