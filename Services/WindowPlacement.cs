using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace RigolWidget.Services;

/// <summary>
/// Remembers where the widget was and puts it back there on the next start.
/// The position is saved shortly after every move (not only on close), so it survives a killed
/// process or a Windows shutdown. If the saved spot is no longer on any monitor (monitor removed,
/// layout changed), the widget is centered on the primary monitor instead.
/// </summary>
public sealed class WindowPlacement
{
    private readonly Window _window;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer;

    public WindowPlacement(Window window, AppSettings settings)
    {
        _window = window;
        _settings = settings;

        // Must run before the window is shown.
        if (settings.WindowLeft is double left && settings.WindowTop is double top)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
        }

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };

        window.Loaded += (_, _) => EnsureVisible();
        window.LocationChanged += (_, _) =>
        {
            _saveTimer.Stop();   // debounce: save once the window stops moving
            _saveTimer.Start();
        };
        window.Closing += (_, _) => Save();
    }

    private void Save()
    {
        if (_window.WindowState != WindowState.Normal) return;
        if (!double.IsFinite(_window.Left) || !double.IsFinite(_window.Top)) return;

        double left = Math.Round(_window.Left, 1);
        double top = Math.Round(_window.Top, 1);
        if (_settings.WindowLeft == left && _settings.WindowTop == top) return;

        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.Save();
    }

    private void EnsureVisible()
    {
        double width = _window.ActualWidth > 0 ? _window.ActualWidth : 200;
        double height = _window.ActualHeight > 0 ? _window.ActualHeight : 100;

        // The title bar (drag handle) must be reachable; the rest may hang off an edge.
        var titleBar = new Rect(_window.Left, _window.Top, Math.Min(width, 120), 20);
        if (IsReachable(titleBar, WorkAreasDip())) return;

        var wa = SystemParameters.WorkArea;   // primary monitor, DIP
        _window.Left = wa.Left + (wa.Width - width) / 2;
        _window.Top = wa.Top + (wa.Height - height) / 2;
    }

    /// <summary>
    /// True when enough of the title bar lies on one work area to grab it with the mouse.
    /// Pure function so it can be tested without real monitors.
    /// </summary>
    public static bool IsReachable(Rect titleBar, IEnumerable<Rect> workAreas)
    {
        foreach (var area in workAreas)
        {
            var hit = Rect.Intersect(titleBar, area);
            if (!hit.IsEmpty && hit.Width >= 40 && hit.Height >= 10) return true;
        }
        return false;
    }

    /// <summary>Every monitor's work area in WPF units (same conversion as EdgeSnap).</summary>
    private IEnumerable<Rect> WorkAreasDip()
    {
        var source = PresentationSource.FromVisual(_window);
        double sx = 1, sy = 1;
        if (source?.CompositionTarget != null)
        {
            sx = source.CompositionTarget.TransformToDevice.M11;
            sy = source.CompositionTarget.TransformToDevice.M22;
        }
        return WinForms.Screen.AllScreens
            .Select(s => s.WorkingArea)
            .Select(wa => new Rect(wa.Left / sx, wa.Top / sy, wa.Width / sx, wa.Height / sy))
            .ToArray();
    }
}
