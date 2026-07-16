using System.Windows;
using RigolWidget.Visa;
using RigolWidget.Windows;

namespace RigolWidget;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1) 장비 선택 창을 먼저 띄운다 (USB 전용).
        var select = new DeviceSelectWindow();
        bool? ok = select.ShowDialog();

        if (ok != true || string.IsNullOrEmpty(select.SelectedResource))
        {
            Shutdown();
            return;
        }

        // 2) 선택된 장비로 메인 위젯 창을 연다.
        var main = new MainWindow(select.SelectedResource!);
        MainWindow = main;
        main.Closed += (_, _) => Shutdown();
        main.Show();
    }
}
