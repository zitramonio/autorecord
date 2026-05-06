using System.Windows;
using Autorecord.App.Tray;

namespace Autorecord.App.Notifications;

public sealed class WpfNotificationService
{
    private readonly TrayIconHost? _trayIconHost;

    public WpfNotificationService(TrayIconHost? trayIconHost = null)
    {
        _trayIconHost = trayIconHost;
    }

    public void ShowInfo(string title, string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (_trayIconHost is not null)
            {
                _trayIconHost.ShowBalloon(title, message);
                return;
            }

            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }
}
