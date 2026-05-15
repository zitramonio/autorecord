using System.Drawing;
using System.IO;

namespace Autorecord.App.Tray;

internal static class AppIconProvider
{
    public static Icon LoadTrayIcon()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            var icon = Icon.ExtractAssociatedIcon(processPath);
            if (icon is not null)
            {
                return icon;
            }
        }

        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/AppIcon.ico"));
        if (resource is not null)
        {
            return new Icon(resource.Stream);
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
