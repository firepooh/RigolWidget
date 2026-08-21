using System.Windows;
using RigolWidget.Services;
using RigolWidget.Windows;

namespace RigolWidget;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = AppSettings.Load();

        // Hold Shift while launching (or pass --select) to always get the device-select window.
        bool forceSelect =
            System.Windows.Forms.Control.ModifierKeys.HasFlag(System.Windows.Forms.Keys.Shift);
        string? resource = null;

        for (int i = 0; i < e.Args.Length; i++)
        {
            string a = e.Args[i];
            if (a.Equals("--select", StringComparison.OrdinalIgnoreCase))
                forceSelect = true;
            else if (a.StartsWith("--device=", StringComparison.OrdinalIgnoreCase))
                resource = a["--device=".Length..];
            else if (a.Equals("--device", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                resource = e.Args[++i];
        }

        // 1) Auto-connect to the remembered device (skips the select window entirely).
        string? notice = null;
        if (resource == null && !forceSelect && settings.AutoConnect &&
            !string.IsNullOrWhiteSpace(settings.LastResource))
        {
            var found = DeviceResolver.Scan();
            string? match = DeviceResolver.Resolve(settings.LastResource, found);

            if (match != null)
            {
                // Same instrument on a different USB index -> remember the new string.
                if (!string.Equals(match, settings.LastResource, StringComparison.OrdinalIgnoreCase))
                {
                    settings.LastResource = match;
                    settings.Save();
                }
                resource = match;
            }
            else
            {
                // Instrument replaced/absent -> fall back to the select window with an explanation.
                notice = found.Count == 0
                    ? "Saved device not found. Check the USB cable and power, then refresh."
                    : "Saved device not found — it may have been replaced. Select a device.";
            }
        }

        // 2) Otherwise ask the user (first run, device changed, --select, or Shift held).
        if (resource == null)
        {
            var select = new DeviceSelectWindow(settings, notice);
            if (select.ShowDialog() != true || string.IsNullOrEmpty(select.SelectedResource))
            {
                Shutdown();
                return;
            }
            resource = select.SelectedResource;
        }

        // 3) Open the main widget window with the selected device.
        var main = new MainWindow(resource!);
        MainWindow = main;
        main.Closed += (_, _) => Shutdown();
        main.Show();
    }
}
