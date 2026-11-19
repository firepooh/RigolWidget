using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RigolWidget.Services;
using RigolWidget.Visa;

namespace RigolWidget.Windows;

/// <summary>Edit the per-channel user limits (empty box = no limit beyond the model rating).</summary>
public partial class LimitsWindow : Window
{
    private readonly Dp800Model _model;
    private readonly AppSettings.ChannelLimit[] _original;
    private readonly (StackPanel Row, TextBox V, TextBox A, TextBlock VHint, TextBlock AHint)[] _rows;

    /// <summary>The edited limits (three entries, CH1..CH3) after Save.</summary>
    public AppSettings.ChannelLimit[] Result { get; private set; } = Array.Empty<AppSettings.ChannelLimit>();

    public LimitsWindow(Dp800Model model, AppSettings.ChannelLimit[] current)
    {
        InitializeComponent();
        _model = model;
        _original = AppSettings.NormalizeLimits(current);
        _rows = new[]
        {
            (Row1, V1, A1, V1Hint, A1Hint),
            (Row2, V2, A2, V2Hint, A2Hint),
            (Row3, V3, A3, V3Hint, A3Hint),
        };

        for (int i = 0; i < 3; i++)
        {
            int ch = i + 1;
            var (row, v, a, vHint, aHint) = _rows[i];
            row.Visibility = model.HasChannel(ch) ? Visibility.Visible : Visibility.Collapsed;

            var rating = model.RatingFor(ch);
            vHint.Text = $"V  (max {F(rating.VMax)})";
            aHint.Text = $"A  (max {F(rating.IMax)})";
            v.Text = _original[i].MaxVolts is double mv ? F(mv) : "";
            a.Text = _original[i].MaxAmps is double ma ? F(ma) : "";
        }

        Loaded += (_, _) =>
        {
            V1.Focus();
            V1.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var result = AppSettings.NormalizeLimits(_original);   // copy; hidden channels keep their values
        for (int i = 0; i < 3; i++)
        {
            int ch = i + 1;
            if (!_model.HasChannel(ch)) continue;

            var rating = _model.RatingFor(ch);
            var (_, v, a, _, _) = _rows[i];
            if (!TryReadBox(v, rating.VMax, out double? maxV))
            {
                ShowError($"CH{ch} voltage limit must be a number from 0 to {F(rating.VMax)}, or empty.", v);
                return;
            }
            if (!TryReadBox(a, rating.IMax, out double? maxA))
            {
                ShowError($"CH{ch} current limit must be a number from 0 to {F(rating.IMax)}, or empty.", a);
                return;
            }
            result[i].MaxVolts = maxV;
            result[i].MaxAmps = maxA;
        }

        Result = result;
        DialogResult = true;
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (_, v, a, _, _) in _rows)
        {
            v.Text = "";
            a.Text = "";
        }
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void ShowError(string message, TextBox box)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        box.Focus();
        box.SelectAll();
    }

    /// <summary>Empty = no limit. Otherwise a number between 0 and the rating.</summary>
    private static bool TryReadBox(TextBox box, double max, out double? value)
    {
        value = null;
        string text = box.Text.Trim().TrimEnd('V', 'A', 'v', 'a').Trim();
        if (text.Length == 0) return true;

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out d))
            return false;
        if (!double.IsFinite(d) || d < 0 || d > max + 1e-9) return false;

        value = Math.Round(d, 3);
        return true;
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
