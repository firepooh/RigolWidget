using System.Windows;
using WinForms = System.Windows.Forms;

namespace RigolWidget.Services;

/// <summary>
/// 창을 화면 모서리에 자석처럼 달라붙게 한다.
/// 드래그 중 창이 작업영역 가장자리에서 SnapDistance 이내로 접근하면 딱 붙는다.
/// 멀티모니터 지원(창이 걸친 화면 기준).
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
        if (_adjusting) return;                 // 자기 자신이 만든 이동은 무시(재귀 방지)
        if (_window.WindowState != WindowState.Normal) return;

        double w = _window.ActualWidth;
        double h = _window.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // 창 중심이 위치한 모니터의 작업영역(작업표시줄 제외)을 DIP로 변환.
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

    /// <summary>물리 픽셀 좌표를 받아 해당 모니터의 작업영역을 DIP(WPF 좌표)로 반환.</summary>
    private Rect GetWorkAreaDip(double dipX, double dipY)
    {
        var source = PresentationSource.FromVisual(_window);
        double sx = 1, sy = 1;
        if (source?.CompositionTarget != null)
        {
            sx = source.CompositionTarget.TransformToDevice.M11;
            sy = source.CompositionTarget.TransformToDevice.M22;
        }

        // 창 중심의 물리 픽셀 위치로 모니터 선택.
        int px = (int)(dipX * sx);
        int py = (int)(dipY * sy);
        var screen = WinForms.Screen.FromPoint(new System.Drawing.Point(px, py));
        var wa = screen.WorkingArea;   // 물리 픽셀

        return new Rect(wa.Left / sx, wa.Top / sy, wa.Width / sx, wa.Height / sy);
    }
}
