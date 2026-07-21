using System.Windows;
using RigolWidget.Visa;
using RigolWidget.Windows;

namespace RigolWidget;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1) Show the device selection window first (USB only).
        var select = new DeviceSelectWindow();
        bool? ok = select.ShowDialog();

        if (ok != true || string.IsNullOrEmpty(select.SelectedResource))
        {
            Shutdown();
            return;
        }

        // 2) Open the main widget window with the selected device.
        var main = new MainWindow(select.SelectedResource!);
        MainWindow = main;
        main.Closed += (_, _) => Shutdown();
        main.Show();
    }
}
