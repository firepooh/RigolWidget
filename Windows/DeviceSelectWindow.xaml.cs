using System.Windows;
using System.Windows.Input;
using RigolWidget.Visa;

namespace RigolWidget.Windows;

public partial class DeviceSelectWindow : Window
{
    /// <summary>선택되어 확정된 장비 리소스 문자열(연결 성공 시).</summary>
    public string? SelectedResource { get; private set; }

    public DeviceSelectWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshDevices();
    }

    private void RefreshDevices()
    {
        DeviceList.Items.Clear();
        StatusText.Text = "USB 장비 검색 중...";
        EmptyHint.Text = "USB 장비를 찾는 중...";
        EmptyHint.Visibility = Visibility.Visible;

        try
        {
            // 임시 RM으로 목록만 조회(연결 시 App이 별도 RM 생성).
            using var rm = new VisaResourceManager();
            var devices = rm.FindUsbInstruments();

            foreach (var d in devices)
                DeviceList.Items.Add(d);

            if (DeviceList.Items.Count > 0)
            {
                DeviceList.SelectedIndex = 0;
                EmptyHint.Visibility = Visibility.Collapsed;
                StatusText.Text = $"{DeviceList.Items.Count}개 USB 장비 발견";
            }
            else
            {
                EmptyHint.Text = "USB 장비가 없습니다. 연결/전원을 확인 후 새로고침하세요.";
                StatusText.Text = "장비 없음";
            }
        }
        catch (VisaException ex)
        {
            EmptyHint.Text = "VISA 런타임을 사용할 수 없습니다.";
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
            StatusText.Text = "장비를 선택하세요.";
            return;
        }

        // 실제 연결 검증은 메인 창의 RigolConnection이 담당. 여기선 선택만 확정.
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
