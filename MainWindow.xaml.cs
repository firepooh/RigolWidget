using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using RigolWidget.Services;
using RigolWidget.Visa;

namespace RigolWidget;

public partial class MainWindow : Window
{
    private readonly VisaResourceManager _rm;
    private readonly RigolConnection _conn;
    private readonly Dp832 _dev;
    private readonly EdgeSnap _snap;

    private readonly ChannelUi[] _channels;
    private CancellationTokenSource? _pollCts;
    private Storyboard? _pulse;
    private int _pollTick;

    // 설정 팝업 상태
    private ChannelUi? _popCh;
    private char _popField;   // 'V' | 'A'
    private bool _mini;       // 미니창 모드

    private Dp800Model _model = Dp800Models.Default;  // 감지된 장비 모델(정격)
    private bool _identified;                          // *IDN? 식별 완료 여부

    // 휠 조작 디바운스: 조작 중엔 SET 표시만 갱신(깜빡임), 멈추면 장비로 전송
    private readonly DispatcherTimer _wheelTimer;
    private ChannelUi? _wheelCh;
    private char _wheelField;
    private double _wheelValue;

    public MainWindow(string resource)
    {
        InitializeComponent();

        _rm = new VisaResourceManager();
        _conn = new RigolConnection(_rm, resource);
        _dev = new Dp832(_conn);
        _snap = new EdgeSnap(this);

        _channels = new[]
        {
            new ChannelUi
            {
                Channel = 1, Accent = (Brush)FindResource("Ch1Accent"),
                Track = Ch1Track, Knob = Ch1Knob,
                MiniTrack = MiniCh1Track, MiniKnob = MiniCh1Knob,
                MiniMeasV = MiniCh1MeasV, MiniMeasA = MiniCh1MeasA,
                CvChip = Ch1CvChip, CvText = Ch1CvText, CcChip = Ch1CcChip, CcText = Ch1CcText,
                MeasV = Ch1MeasV, MeasA = Ch1MeasA,
                SetVText = Ch1SetVText, SetAText = Ch1SetAText,
                OcpBox = Ch1OcpBox, OcpMark = Ch1OcpMark, OcpLabel = Ch1OcpLabel,
                OcpVal = Ch1OcpVal, OcpTrip = Ch1OcpTrip,
                OcvBox = Ch1OcvBox, OcvMark = Ch1OcvMark, OcvLabel = Ch1OcvLabel,
                OcvVal = Ch1OcvVal, OcvTrip = Ch1OcvTrip,
            },
            new ChannelUi
            {
                Channel = 2, Accent = (Brush)FindResource("Ch2Accent"),
                Track = Ch2Track, Knob = Ch2Knob,
                MiniTrack = MiniCh2Track, MiniKnob = MiniCh2Knob,
                MiniMeasV = MiniCh2MeasV, MiniMeasA = MiniCh2MeasA,
                CvChip = Ch2CvChip, CvText = Ch2CvText, CcChip = Ch2CcChip, CcText = Ch2CcText,
                MeasV = Ch2MeasV, MeasA = Ch2MeasA,
                SetVText = Ch2SetVText, SetAText = Ch2SetAText,
                OcpBox = Ch2OcpBox, OcpMark = Ch2OcpMark, OcpLabel = Ch2OcpLabel,
                OcpVal = Ch2OcpVal, OcpTrip = Ch2OcpTrip,
                OcvBox = Ch2OcvBox, OcvMark = Ch2OcvMark, OcvLabel = Ch2OcvLabel,
                OcvVal = Ch2OcvVal, OcvTrip = Ch2OcvTrip,
            },
        };

        foreach (var c in _channels)
        {
            RenderToggle(c, animate: false);
            RenderMode(c);
            RenderProtection(c);
        }

        BuildPulse();

        _wheelTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
        _wheelTimer.Tick += (_, _) => CommitWheel();

        // 우클릭 메뉴에 어셈블리 버전 표시 (빌드 해시 접미사는 잘라냄)
        var infoVer = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "?";
        int plus = infoVer.IndexOf('+');
        VersionMenu.Header = $"RigolWidget v{(plus > 0 ? infoVer[..plus] : infoVer)}";

        _conn.ConnectionChanged += OnConnectionChanged;
        UpdateConnectionUi(_conn.IsConnected);

        Loaded += (_, _) => StartPolling();
        Closed += OnClosed;
    }

    // ================= 폴링 =================

    private void StartPolling()
    {
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _ = Task.Run(() => PollLoop(token), token);
    }

    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_dev.IsConnected)
            {
                // (재)접속 직후 최초 1회: 모델 식별(*IDN?) → 타이틀·정격 반영.
                if (!_identified)
                    IdentifyModel();

                // _pollTick==0 은 (재)접속 직후 → 즉시 전체 동기화.
                bool fullSync = (_pollTick % 4) == 0;
                _pollTick++;

                foreach (var c in _channels)
                {
                    if (token.IsCancellationRequested) break;
                    if (c.Channel == 2 && !_model.HasCh2) continue;  // 단일 채널 모델은 CH2 폴링 생략
                    if (fullSync) FullSyncChannel(c);
                    else FastPollChannel(c);
                }
            }

            try { await Task.Delay(1000, token); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>*IDN?로 모델을 식별해 타이틀·정격·채널 표시를 반영한다(최초 1회).</summary>
    private void IdentifyModel()
    {
        if (!_dev.TryGetIdentity(out string idn))
            return;   // 실패 시 다음 폴링에서 재시도

        var model = Dp800Models.FromIdn(idn);
        _identified = true;
        DebugLog.Write($"모델 식별: {model.Name} (IDN: {idn.Trim()})");

        Dispatcher.Invoke(() => ApplyModel(model));
    }

    /// <summary>감지된 모델을 UI에 반영: 타이틀 라벨, 채널 정격, CH2 표시 여부.</summary>
    private void ApplyModel(Dp800Model model)
    {
        _model = model;
        ModelLabel.Text = model.Name;

        _channels[0].Rating = model.Ch1;
        _channels[1].Rating = model.Ch2 ?? model.Ch1;

        // 단일 채널 모델은 CH2 관련 UI 숨김(전체·미니).
        var ch2Vis = model.HasCh2 ? Visibility.Visible : Visibility.Collapsed;
        Ch2Row.Visibility = ch2Vis;
        RowDivider.Visibility = ch2Vis;
        MiniCh2Cell.Visibility = ch2Vis;
    }

    /// <summary>변경됐을 때만 대입 — 동일 값 재대입으로 인한 불필요한 렌더 무효화 방지.</summary>
    private static void SetTextIfChanged(TextBlock tb, string text)
    {
        if (tb.Text != text) tb.Text = text;
    }

    /// <summary>측정 전압/전류 텍스트를 전체·미니 표시 양쪽에 갱신.</summary>
    private static void SetMeasText(ChannelUi c, double v, double a)
    {
        string sv = v.ToString("0.000", CultureInfo.InvariantCulture);
        string sa = a.ToString("0.000", CultureInfo.InvariantCulture);
        SetTextIfChanged(c.MeasV, sv);
        SetTextIfChanged(c.MeasA, sa);
        SetTextIfChanged(c.MiniMeasV, sv);
        SetTextIfChanged(c.MiniMeasA, sa);
    }

    /// <summary>빠른 폴링: 측정값(묶음 질의) + 동작 모드(CV/CC).</summary>
    private void FastPollChannel(ChannelUi c)
    {
        bool okMeas = _dev.TryReadMeasurementAll(c.Channel, out double v, out double a, out _);
        bool okM = _dev.TryGetMode(c.Channel, out string mode);

        Dispatcher.InvokeAsync(() =>
        {
            if (okMeas) SetMeasText(c, v, a);
            if (okM && c.Mode != mode)
            {
                c.Mode = mode;
                RenderMode(c);
            }
        });
    }

    /// <summary>
    /// 채널 전체 상태를 필드별로 읽어, 성공한 항목만 UI에 반영한다.
    /// (질의 하나가 실패해도 나머지 값은 정상 동기화)
    /// </summary>
    private void FullSyncChannel(ChannelUi c)
    {
        bool okMeas = _dev.TryReadMeasurementAll(c.Channel, out double measV, out double measA, out _);
        bool okMode = _dev.TryGetMode(c.Channel, out string mode);
        bool okSet = _dev.TryGetApplied(c.Channel, out double setV, out double setA);
        bool okOut = _dev.TryGetOutputState(c.Channel, out bool outOn);
        bool okOcp = _dev.TryGetOcpState(c.Channel, out bool ocpOn);
        bool okOcpV = _dev.TryGetOcpValue(c.Channel, out double ocpVal);
        bool okOcpT = _dev.TryGetOcpAlarm(c.Channel, out bool ocpTrip);
        bool okOcv = _dev.TryGetOvpState(c.Channel, out bool ocvOn);
        bool okOcvV = _dev.TryGetOvpValue(c.Channel, out double ocvVal);
        bool okOcvT = _dev.TryGetOvpAlarm(c.Channel, out bool ocvTrip);

        Dispatcher.InvokeAsync(() =>
        {
            if (okMeas) SetMeasText(c, measV, measA);

            if (okMode && c.Mode != mode)
            {
                c.Mode = mode;
                RenderMode(c);
            }

            if (okOut && c.OutputOn != outOn)
            {
                c.OutputOn = outOn;
                RenderToggle(c, animate: true);
            }

            // 사용자가 방금 휠/팝업으로 바꾼 직후엔 장비의 예전 값으로 되돌리지 않는다.
            // (휠 디바운스 2초 + 전송 여유를 덮도록 2.5초)
            bool recentLocal = (DateTime.UtcNow - c.LastLocalSet).TotalMilliseconds < 2500;
            if (okSet && !recentLocal)
            {
                c.SetV = setV;
                c.SetA = setA;
                RenderSet(c);
            }

            bool protChanged = false;
            if (okOcp && c.OcpOn != ocpOn) { c.OcpOn = ocpOn; protChanged = true; }
            if (okOcv && c.OcvOn != ocvOn) { c.OcvOn = ocvOn; protChanged = true; }
            if (okOcpT && c.TripOcp != ocpTrip) { c.TripOcp = ocpTrip; protChanged = true; }
            if (okOcvT && c.TripOcv != ocvTrip) { c.TripOcv = ocvTrip; protChanged = true; }
            if (protChanged) RenderProtection(c);

            // 편집 중이 아닐 때만 보호값 입력칸 동기화(사용자 입력 보호).
            if (okOcpV && !c.OcpVal.IsFocused)
            {
                string s = ocpVal.ToString("0.000", CultureInfo.InvariantCulture);
                if (c.OcpVal.Text != s) c.OcpVal.Text = s;
            }
            if (okOcvV && !c.OcvVal.IsFocused)
            {
                string s = ocvVal.ToString("0.00", CultureInfo.InvariantCulture);
                if (c.OcvVal.Text != s) c.OcvVal.Text = s;
            }
        });
    }

    // ================= 연결 상태 UI =================

    private void OnConnectionChanged(bool connected)
    {
        if (connected)
        {
            _pollTick = 0;      // (재)접속 직후 다음 폴링에서 즉시 전체 동기화
            _identified = false; // 재접속 시 모델 재식별
        }
        Dispatcher.InvokeAsync(() => UpdateConnectionUi(connected));
    }

    private void UpdateConnectionUi(bool connected)
    {
        if (connected)
        {
            LiveDot.Fill = (Brush)FindResource("Ch1Accent");
            ConnText.Text = "연결됨";
            ConnText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x70, 0x77));
            _pulse?.Begin(LiveDot, true);
        }
        else
        {
            _pulse?.Stop(LiveDot);
            LiveDot.Opacity = 1;
            LiveDot.Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0x50, 0x50));
            ConnText.Text = "재접속 중…";
            ConnText.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x50, 0x50));
        }
    }

    private void BuildPulse()
    {
        var anim = new DoubleAnimation(1.0, 0.35, new Duration(TimeSpan.FromSeconds(0.9)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Timeline.SetDesiredFrameRate(anim, 10);   // 상시 애니메이션 → 저프레임으로 유휴 렌더 부하 절감
        Storyboard.SetTarget(anim, LiveDot);
        Storyboard.SetTargetProperty(anim, new PropertyPath("Opacity"));
        _pulse = new Storyboard();
        _pulse.Children.Add(anim);
    }

    // ================= 렌더 헬퍼 =================

    /// <summary>세로 토글 렌더 — 전체·미니 두 스위치를 함께 갱신.</summary>
    private static void RenderToggle(ChannelUi c, bool animate)
    {
        RenderSwitch(c.Track, c.Knob, c.OutputOn, c.Accent, animate);
        RenderSwitch(c.MiniTrack, c.MiniKnob, c.OutputOn, c.Accent, animate);
    }

    /// <summary>세로 스위치 하나: ON = accent 트랙+글로우+knob 위, OFF = 회색 트랙+knob 아래.</summary>
    private static void RenderSwitch(Border track, Ellipse knob, bool on, Brush accent, bool animate)
    {
        if (on)
        {
            track.Background = accent;
            track.BorderBrush = accent;
            track.BorderThickness = new Thickness(1);
            var col = ((SolidColorBrush)accent).Color;
            track.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = col,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.55
            };
        }
        else
        {
            track.Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
            track.BorderBrush = new SolidColorBrush(Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF));
            track.BorderThickness = new Thickness(1);
            track.Effect = null;
        }

        // knob 슬라이드(세로): ON=위(2px), OFF=아래(24px). 트랙 1px 테두리 보정.
        double target = on ? 2 : 24;
        if (animate)
        {
            var slide = new DoubleAnimation(target, new Duration(TimeSpan.FromSeconds(0.15)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            knob.BeginAnimation(Canvas.TopProperty, slide);
        }
        else
        {
            knob.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetTop(knob, target);
        }
        Canvas.SetLeft(knob, 2);
    }

    /// <summary>CV/CC 모드 칩 렌더: 현재 모드만 accent 활성. UR이면 둘 다 비활성.</summary>
    private static void RenderMode(ChannelUi c)
    {
        StyleModeChip(c.CvChip, c.CvText, c.Mode == "CV", c.Accent);
        StyleModeChip(c.CcChip, c.CcText, c.Mode == "CC", c.Accent);
    }

    private static void StyleModeChip(Border chip, TextBlock text, bool active, Brush accent)
    {
        if (active)
        {
            chip.Background = accent;
            chip.BorderBrush = accent;
            text.Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x14, 0x10));
            var col = ((SolidColorBrush)accent).Color;
            chip.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = col,
                BlurRadius = 7,
                ShadowDepth = 0,
                Opacity = 0.65
            };
        }
        else
        {
            chip.Background = Brushes.Transparent;
            chip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
            text.Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x68));
            chip.Effect = null;
        }
    }

    /// <summary>SET 값(설정 전압/전류) 표시 갱신.</summary>
    private static void RenderSet(ChannelUi c)
    {
        SetTextIfChanged(c.SetVText, c.SetV.ToString("0.000", CultureInfo.InvariantCulture) + " V");
        SetTextIfChanged(c.SetAText, c.SetA.ToString("0.000", CultureInfo.InvariantCulture) + " A");
    }

    /// <summary>OCP/OCV 체크박스 + 라벨 + TRIP 배지 렌더.</summary>
    private void RenderProtection(ChannelUi c)
    {
        RenderProtRow(c, c.OcpOn, c.TripOcp, c.OcpBox, c.OcpMark, c.OcpLabel, c.OcpTrip);
        RenderProtRow(c, c.OcvOn, c.TripOcv, c.OcvBox, c.OcvMark, c.OcvLabel, c.OcvTrip);
    }

    private void RenderProtRow(ChannelUi c, bool enabled, bool fault,
        Border box, TextBlock mark, TextBlock label, Border tripBadge)
    {
        if (enabled)
        {
            box.Background = c.Accent;
            box.BorderBrush = c.Accent;
            mark.Visibility = Visibility.Visible;
        }
        else
        {
            box.Background = Brushes.Transparent;
            box.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            mark.Visibility = Visibility.Collapsed;
        }

        bool tripped = enabled && fault;   // 보호 활성 + 이벤트 발생 시에만 표시
        label.Foreground = tripped
            ? (Brush)FindResource("TripLabel")
            : enabled ? c.Accent : (Brush)FindResource("SubText2");

        if (tripped && tripBadge.Visibility != Visibility.Visible)
        {
            tripBadge.Visibility = Visibility.Visible;
            var blink = new DoubleAnimation(1.0, 0.25, new Duration(TimeSpan.FromSeconds(0.35)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Timeline.SetDesiredFrameRate(blink, 10);
            tripBadge.BeginAnimation(OpacityProperty, blink);
        }
        else if (!tripped && tripBadge.Visibility == Visibility.Visible)
        {
            tripBadge.BeginAnimation(OpacityProperty, null);
            tripBadge.Opacity = 1;
            tripBadge.Visibility = Visibility.Collapsed;
        }
    }

    // ================= 사용자 조작 =================

    private void ToggleOutput(ChannelUi c)
    {
        c.OutputOn = !c.OutputOn;
        RenderToggle(c, animate: true);            // 낙관적 업데이트
        bool target = c.OutputOn;
        Task.Run(() => _dev.SetOutput(c.Channel, target));
    }

    private void ToggleOcp(ChannelUi c)
    {
        c.OcpOn = !c.OcpOn;
        RenderProtection(c);
        bool target = c.OcpOn;
        Task.Run(() => _dev.SetOcp(c.Channel, target));
    }

    private void ToggleOcv(ChannelUi c)
    {
        c.OcvOn = !c.OcvOn;
        RenderProtection(c);
        bool target = c.OcvOn;
        Task.Run(() => _dev.SetOvp(c.Channel, target));
    }

    /// <summary>setpoint 적용(팝업 커밋): 상태·SET 표시 갱신 후 즉시 장비 전송.</summary>
    private void ApplySetpoint(ChannelUi c, char field, double value)
    {
        // 같은 항목의 휠 디바운스 대기가 있으면 취소(팝업 값이 우선)
        if (_wheelCh == c && _wheelField == field)
        {
            _wheelTimer.Stop();
            StopSetBlink(c, field);
            _wheelCh = null;
        }

        double max = field == 'V' ? c.Rating.VMax : c.Rating.IMax;
        value = Math.Round(Math.Clamp(value, 0, max), 3);
        c.LastLocalSet = DateTime.UtcNow;

        if (field == 'V') c.SetV = value;
        else c.SetA = value;
        RenderSet(c);

        int ch = c.Channel;
        if (field == 'V') Task.Run(() => _dev.SetVoltage(ch, value));
        else Task.Run(() => _dev.SetCurrent(ch, value));
    }

    private void CommitProtValue(ChannelUi c, string kind, TextBox box)
    {
        if (!TryParseNum(box.Text, out double v)) return;

        if (kind == "OCP")
        {
            v = Math.Clamp(v, 0, c.Rating.OcpMax);
            box.Text = v.ToString("0.000", CultureInfo.InvariantCulture);
            Task.Run(() => _dev.SetOcpValue(c.Channel, v));
        }
        else
        {
            v = Math.Clamp(v, 0.01, c.Rating.OvpMax);
            box.Text = v.ToString("0.00", CultureInfo.InvariantCulture);
            Task.Run(() => _dev.SetOvpValue(c.Channel, v));
        }
    }

    private static bool TryParseNum(string text, out double value)
    {
        text = text.Trim().TrimEnd('V', 'A', 'v', 'a', ' ');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    /// <summary>Tag "n,X" 형식 파싱 → (채널UI, 종류).</summary>
    private bool TryParseTag(object? sender, out ChannelUi c, out string kind)
    {
        c = _channels[0];
        kind = "";
        if (sender is not FrameworkElement fe || fe.Tag is not string tag) return false;
        var parts = tag.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int ch) || ch is < 1 or > 2) return false;
        c = _channels[ch - 1];
        kind = parts[1];
        return true;
    }

    // ---- 세그먼트(VFD) 인터랙션 ----

    private void Vfd_Click(object sender, MouseButtonEventArgs e)
    {
        if (!TryParseTag(sender, out var c, out string kind)) return;
        OpenSetPopup(c, kind == "V" ? 'V' : 'A');
    }

    private void Vfd_Wheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (!TryParseTag(sender, out var c, out string kind)) return;

        char field = kind == "V" ? 'V' : 'A';

        // 다른 채널/항목을 조작하기 시작하면 이전 항목은 즉시 커밋
        if (_wheelCh != null && (_wheelCh != c || _wheelField != field))
            CommitWheel();

        bool fine = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        double step = field == 'V' ? (fine ? 0.1 : 1.0) : (fine ? 0.01 : 0.1);
        double cur = _wheelCh == c && _wheelField == field
            ? _wheelValue
            : (field == 'V' ? c.SetV : c.SetA);
        double max = field == 'V' ? c.Rating.VMax : c.Rating.IMax;
        double value = Math.Round(Math.Clamp(cur + (e.Delta > 0 ? step : -step), 0, max), 3);

        // SET 표시만 갱신 + 깜빡임. 장비 전송은 조작이 멈춘 뒤(타이머).
        _wheelCh = c;
        _wheelField = field;
        _wheelValue = value;
        c.LastLocalSet = DateTime.UtcNow;

        if (field == 'V') c.SetV = value;
        else c.SetA = value;
        RenderSet(c);
        StartSetBlink(c, field);

        _wheelTimer.Stop();
        _wheelTimer.Start();
    }

    /// <summary>휠 조작 종료 → 깜빡임 정지, 마지막 값을 장비로 전송.</summary>
    private void CommitWheel()
    {
        _wheelTimer.Stop();
        if (_wheelCh == null) return;

        var c = _wheelCh;
        char field = _wheelField;
        double value = _wheelValue;
        _wheelCh = null;

        StopSetBlink(c, field);
        c.LastLocalSet = DateTime.UtcNow;

        int ch = c.Channel;
        if (field == 'V') Task.Run(() => _dev.SetVoltage(ch, value));
        else Task.Run(() => _dev.SetCurrent(ch, value));
    }

    private static void StartSetBlink(ChannelUi c, char field)
    {
        var tb = field == 'V' ? c.SetVText : c.SetAText;
        if (Equals(tb.Tag, "blink")) return;   // 이미 깜빡이는 중
        tb.Tag = "blink";
        var blink = new DoubleAnimation(1.0, 0.25, new Duration(TimeSpan.FromSeconds(0.3)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Timeline.SetDesiredFrameRate(blink, 10);
        tb.BeginAnimation(OpacityProperty, blink);
    }

    private static void StopSetBlink(ChannelUi c, char field)
    {
        var tb = field == 'V' ? c.SetVText : c.SetAText;
        tb.Tag = null;
        tb.BeginAnimation(OpacityProperty, null);
        tb.Opacity = 1;
    }

    // ---- 설정 팝업 ----

    private void OpenSetPopup(ChannelUi c, char field)
    {
        _popCh = c;
        _popField = field;

        PopTitle.Text = $"CH{c.Channel} · {(field == 'V' ? "전압" : "전류")} 설정";
        PopTitle.Foreground = c.Accent;
        PopUnit.Text = field.ToString();
        PopApply.Background = c.Accent;

        if (field == 'V')
        {
            PopMCoarse.Content = "−1"; PopMFine.Content = "−0.1";
            PopPFine.Content = "+0.1"; PopPCoarse.Content = "+1";
        }
        else
        {
            PopMCoarse.Content = "−0.1"; PopMFine.Content = "−0.01";
            PopPFine.Content = "+0.01"; PopPCoarse.Content = "+0.1";
        }

        PopInput.Text = (field == 'V' ? c.SetV : c.SetA).ToString("0.000", CultureInfo.InvariantCulture);
        SetPopup.IsOpen = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            PopInput.Focus();
            PopInput.SelectAll();
        });
    }

    private void PopStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string tag || _popField == default) return;

        double coarse = _popField == 'V' ? 1.0 : 0.1;
        double fine = _popField == 'V' ? 0.1 : 0.01;
        double step = tag[1] == 'C' ? coarse : fine;
        if (tag[0] == '-') step = -step;

        if (!TryParseNum(PopInput.Text, out double v)) v = 0;
        v = Math.Round(Math.Max(0, v + step), 3);
        PopInput.Text = v.ToString("0.000", CultureInfo.InvariantCulture);
    }

    private void PopApply_Click(object sender, RoutedEventArgs e) => CommitPopup();

    private void PopInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitPopup();
        else if (e.Key == Key.Escape) SetPopup.IsOpen = false;
    }

    private void CommitPopup()
    {
        if (_popCh != null && TryParseNum(PopInput.Text, out double v))
            ApplySetpoint(_popCh, _popField, v);
        SetPopup.IsOpen = false;
    }

    // ---- TRIP 해제 ----

    private void Trip_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!TryParseTag(sender, out var c, out string kind)) return;

        if (kind == "OCP")
        {
            c.TripOcp = false;
            Task.Run(() => _dev.ClearOcpAlarm(c.Channel));
        }
        else
        {
            c.TripOcv = false;
            Task.Run(() => _dev.ClearOvpAlarm(c.Channel));
        }
        RenderProtection(c);
    }

    // ---- 이벤트 핸들러 (XAML 바인딩) ----

    private void Ch1Dot_Click(object sender, MouseButtonEventArgs e) => ToggleOutput(_channels[0]);
    private void Ch2Dot_Click(object sender, MouseButtonEventArgs e) => ToggleOutput(_channels[1]);
    private void Ch1Ocp_Click(object sender, MouseButtonEventArgs e) => ToggleOcp(_channels[0]);
    private void Ch2Ocp_Click(object sender, MouseButtonEventArgs e) => ToggleOcp(_channels[1]);
    private void Ch1Ocv_Click(object sender, MouseButtonEventArgs e) => ToggleOcv(_channels[0]);
    private void Ch2Ocv_Click(object sender, MouseButtonEventArgs e) => ToggleOcv(_channels[1]);

    private void ProtVal_Commit(object sender, RoutedEventArgs e)
    {
        if (TryParseTag(sender, out var c, out string kind) && sender is TextBox box)
            CommitProtValue(c, kind, box);
    }

    private void ProtVal_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && TryParseTag(sender, out var c, out string kind) && sender is TextBox box)
        {
            CommitProtValue(c, kind, box);
            Keyboard.ClearFocus();
        }
    }

    // ================= 창 제어 =================

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)   // 더블클릭 → 미니창 모드 토글
        {
            ToggleMini();
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void ToggleMini()
    {
        _mini = !_mini;
        SetPopup.IsOpen = false;   // 미니 전환 시 열린 팝업 닫기
        FullBody.Visibility = _mini ? Visibility.Collapsed : Visibility.Visible;
        MiniBody.Visibility = _mini ? Visibility.Visible : Visibility.Collapsed;
    }

    // 타이틀바에서 휠 → 투명도 조절 (0.35 ~ 1.0)
    private void TitleBar_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double step = e.Delta > 0 ? 0.05 : -0.05;
        Opacity = Math.Clamp(Opacity + step, 0.35, 1.0);
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => SetPinned(!Topmost);

    private void PinMenu_Click(object sender, RoutedEventArgs e) => SetPinned(PinMenu.IsChecked);

    private void CloseMenu_Click(object sender, RoutedEventArgs e) => Close();

    private void SetPinned(bool pinned)
    {
        Topmost = pinned;
        PinIcon.Opacity = pinned ? 1.0 : 0.35;
        PinButton.ToolTip = pinned ? "항상 위에 고정 (켜짐)" : "항상 위에 고정 (꺼짐)";
        PinMenu.IsChecked = pinned;
    }

    private void Minimize_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, MouseButtonEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        CommitWheel();   // 미전송 휠 조작값 플러시
        _pollCts?.Cancel();
        _conn.ConnectionChanged -= OnConnectionChanged;
        _conn.Dispose();
        _rm.Dispose();
    }

    // ================= 채널 UI 묶음 =================

    private sealed class ChannelUi
    {
        public int Channel;
        public required Brush Accent;

        public required Border Track;
        public required Ellipse Knob;
        public required Border MiniTrack;
        public required Ellipse MiniKnob;
        public required TextBlock MiniMeasV;
        public required TextBlock MiniMeasA;
        public required Border CvChip;
        public required TextBlock CvText;
        public required Border CcChip;
        public required TextBlock CcText;
        public required TextBlock MeasV;
        public required TextBlock MeasA;
        public required TextBlock SetVText;
        public required TextBlock SetAText;
        public required Border OcpBox;
        public required TextBlock OcpMark;
        public required TextBlock OcpLabel;
        public required TextBox OcpVal;
        public required Border OcpTrip;
        public required Border OcvBox;
        public required TextBlock OcvMark;
        public required TextBlock OcvLabel;
        public required TextBox OcvVal;
        public required Border OcvTrip;

        public bool OutputOn;
        public bool OcpOn;
        public bool OcvOn;
        public bool TripOcp;
        public bool TripOcv;
        public string Mode = "";
        public double SetV;
        public double SetA;
        public DateTime LastLocalSet = DateTime.MinValue;
        public ChannelRating Rating = new(30, 3, 33, 3.3);  // 기본 DP832 CH 정격
    }
}
