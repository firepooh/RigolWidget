using System.Windows;
using WinForms = System.Windows.Forms;

namespace RigolWidget.Services;

/// <summary>
/// Makes the window snap to screen edges like a magnet.
/// While dragging, the window snaps when it comes within SnapDistance of a work-area edge.
/// Multi-monitor support (based on the screen the window overlaps).
/// </summary>
public sealed class EdgeSnap
{
    private readonly Window _window;
    private bool _adjusting;

    public double SnapDistance { get; set; } = 18;

    public EdgeSnap(Window window)
    {
        _window = window;
        _window.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_adjusting) return;                 // Ignore moves we caused ourselves (prevent recursion)
        if (_window.WindowState != WindowState.Normal) return;

        double w = _window.ActualWidth;
        double h = _window.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Convert the work area (excluding the taskbar) of the monitor containing the window center to DIP.
        var area = GetWorkAreaDip(_window.Left + w / 2, _window.Top + h / 2);

        double left = _window.Left;
        double top = _window.Top;
        double newLeft = left;
        double newTop = top;

        if (Math.Abs(left - area.Left) <= SnapDistance) newLeft = area.Left;
        else if (Math.Abs(area.Right - (left + w)) <= SnapDistance) newLeft = area.Right - w;

        if (Math.Abs(top - area.Top) <= SnapDistance) newTop = area.Top;
        else if (Math.Abs(area.Bottom - (top + h)) <= SnapDistance) newTop = area.Bottom - h;

        if (newLeft != left || newTop != top)
        {
            _adjusting = true;
            _window.Left = newLeft;
            _window.Top = newTop;
            _adjusting = false;
        }
    }

    /// <summary>Takes physical pixel coordinates and returns that monitor's work area in DIP (WPF coordinates).</summary>
    private Rect GetWorkAreaDip(double dipX, double dipY)
    {
        var source = PresentationSource.FromVisual(_window);
        double sx = 1, sy = 1;
        if (source?.CompositionTarget != null)
        {
            sx = source.CompositionTarget.TransformToDevice.M11;
            sy = source.CompositionTarget.TransformToDevice.M22;
        }

        // Select the monitor by the physical pixel position of the window center.
        int px = (int)(dipX * sx);
        int py = (int)(dipY * sy);
        var screen = WinForms.Screen.FromPoint(new System.Drawing.Point(px, py));
        var wa = screen.WorkingArea;   // physical pixels

        return new Rect(wa.Left / sx, wa.Top / sy, wa.Width / sx, wa.Height / sy);
    }
}
