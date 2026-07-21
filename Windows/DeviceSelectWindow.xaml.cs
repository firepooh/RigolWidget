using System.Windows;
using System.Windows.Input;
using RigolWidget.Visa;

namespace RigolWidget.Windows;

public partial class DeviceSelectWindow : Window
{
    /// <summary>The selected/confirmed device resource string (on successful connection).</summary>
    public string? SelectedResource { get; private set; }

    public DeviceSelectWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshDevices();
    }

    private void RefreshDevices()
    {
        DeviceList.Items.Clear();
        StatusText.Text = "Searching USB devices...";
        EmptyHint.Text = "Searching for USB devices...";
        EmptyHint.Visibility = Visibility.Visible;

        try
        {
            // Use a temporary RM just to list devices (the App creates its own RM on connect).
            using var rm = new VisaResourceManager();
            var devices = rm.FindUsbInstruments();

            foreach (var d in devices)
                DeviceList.Items.Add(d);

            if (DeviceList.Items.Count > 0)
            {
                DeviceList.SelectedIndex = 0;
                EmptyHint.Visibility = Visibility.Collapsed;
                StatusText.Text = $"{DeviceList.Items.Count} USB device(s) found";
            }
            else
            {
                EmptyHint.Text = "No USB devices. Check the connection/power and refresh.";
                StatusText.Text = "No devices";
            }
        }
        catch (VisaException ex)
        {
            EmptyHint.Text = "VISA runtime is not available.";
            StatusText.Text = ex.Message;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void DeviceList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Connect();

    private void Connect_Click(object sender, RoutedEventArgs e) => Connect();

    private void Connect()
    {
        if (DeviceList.SelectedItem is not string resource)
        {
            StatusText.Text = "Please select a device.";
            return;
        }

        // Actual connection is validated by the main window RigolConnection; here we only confirm the selection.
        SelectedResource = resource;
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
