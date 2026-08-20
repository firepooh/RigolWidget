using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using RigolWidget.Mcp;
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

    // Setpoint popup state
    private ChannelUi? _popCh;
    private char _popField;   // 'V' | 'A'
    private bool _mini;       // Mini (compact) mode

    private Dp800Model _model = Dp800Models.Default;  // Detected device model (ratings)
    private bool _identified;                          // whether *IDN? identification is done

    private readonly AppSettings _settings;           // App settings (MCP, etc.)
    private readonly RigolMcpContext _mcpContext;     // MCP shared context
    private readonly RigolMcpServer _mcpServer;       // Embedded MCP server

    // Wheel debounce: while adjusting, update the SET display only (blink); send to the device when it stops
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
            new ChannelUi
            {
                Channel = 3, Accent = (Brush)FindResource("Ch3Accent"),
                Track = Ch3Track, Knob = Ch3Knob,
                MiniTrack = MiniCh3Track, MiniKnob = MiniCh3Knob,
                MiniMeasV = MiniCh3MeasV, MiniMeasA = MiniCh3MeasA,
                CvChip = Ch3CvChip, CvText = Ch3CvText, CcChip = Ch3CcChip, CcText = Ch3CcText,
                MeasV = Ch3MeasV, MeasA = Ch3MeasA,
                SetVText = Ch3SetVText, SetAText = Ch3SetAText,
                OcpBox = Ch3OcpBox, OcpMark = Ch3OcpMark, OcpLabel = Ch3OcpLabel,
                OcpVal = Ch3OcpVal, OcpTrip = Ch3OcpTrip,
                OcvBox = Ch3OcvBox, OcvMark = Ch3OcvMark, OcvLabel = Ch3OcvLabel,
                OcvVal = Ch3OcvVal, OcvTrip = Ch3OcvTrip,
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

        // Show the assembly version in the right-click menu (strip the build-hash suffix)
        var infoVer = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "?";
        int plus = infoVer.IndexOf('+');
        VersionMenu.Header = $"RigolWidget v{(plus > 0 ? infoVer[..plus] : infoVer)}";

        // Initialize the embedded MCP server (load settings).
        _settings = AppSettings.Load();
        _mcpContext = new RigolMcpContext(_dev)
        {
            ControlAllowed = _settings.McpAllowControl,
            Model = _model,
        };
        _mcpContext.OnCommand = OnMcpCommand;
        _mcpServer = new RigolMcpServer(_mcpContext);
        InitMcpMenu();

        _conn.ConnectionChanged += OnConnectionChanged;
        UpdateConnectionUi(_conn.IsConnected);

        Loaded += async (_, _) =>
        {
            StartPolling();
            if (_settings.McpEnabled)
                await StartMcpAsync();
        };
        Closed += OnClosed;
    }

    // ================= Polling =================

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
                // First time right after (re)connect: identify the model (*IDN?) -> apply title & ratings.
                if (!_identified)
                    IdentifyModel();

                // _pollTick==0 means right after (re)connect -> full sync immediately.
                bool fullSync = (_pollTick % 4) == 0;
                _pollTick++;

                foreach (var c in _channels)
                {
                    if (token.IsCancellationRequested) break;
                    if (!_model.HasChannel(c.Channel)) continue;  // skip channels this model doesn't have
                    if (fullSync) FullSyncChannel(c);
                    else FastPollChannel(c);
                }
            }

            try { await Task.Delay(1000, token); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Identify the model via *IDN? and apply the title, ratings, and channel visibility (once).</summary>
    private void IdentifyModel()
    {
        if (!_dev.TryGetIdentity(out string idn))
            return;   // retry on the next poll if it failed

        var model = Dp800Models.FromIdn(idn);
        _identified = true;
        DebugLog.Write($"Model identified: {model.Name} (IDN: {idn.Trim()})");

        Dispatcher.Invoke(() => ApplyModel(model));
    }

    /// <summary>Apply the detected model to the UI: title label, channel ratings, CH2 visibility.</summary>
    private void ApplyModel(Dp800Model model)
    {
        _model = model;
        _mcpContext.Model = model;   // MCP tools use the same model for rating clamps
        ModelLabel.Text = model.Name;

        _channels[0].Rating = model.Ch1;
        _channels[1].Rating = model.Ch2 ?? model.Ch1;
        _channels[2].Rating = model.Ch3 ?? model.Ch1;

        // Show only the channels this model has (full & mini).
        var ch2Vis = model.HasCh2 ? Visibility.Visible : Visibility.Collapsed;
        Ch2Row.Visibility = ch2Vis;
        RowDivider.Visibility = ch2Vis;
        MiniCh2Cell.Visibility = ch2Vis;

        var ch3Vis = model.HasCh3 ? Visibility.Visible : Visibility.Collapsed;
        Ch3Row.Visibility = ch3Vis;
        RowDivider2.Visibility = ch3Vis;
        MiniCh3Cell.Visibility = ch3Vis;
    }

    /// <summary>Assign only when changed - avoids unnecessary render invalidation from re-assigning the same value.</summary>
    private static void SetTextIfChanged(TextBlock tb, string text)
    {
        if (tb.Text != text) tb.Text = text;
    }

    /// <summary>Update the measured voltage/current text on both the full and mini displays.</summary>
    private static void SetMeasText(ChannelUi c, double v, double a)
    {
        string sv = v.ToString("0.000", CultureInfo.InvariantCulture);
        string sa = a.ToString("0.000", CultureInfo.InvariantCulture);
        SetTextIfChanged(c.MeasV, sv);
        SetTextIfChanged(c.MeasA, sa);
        SetTextIfChanged(c.MiniMeasV, sv);
        SetTextIfChanged(c.MiniMeasA, sa);
    }

    /// <summary>Fast poll: measurements (batched query) + operating mode (CV/CC).</summary>
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
    /// Read the full channel state field by field, applying only the successful items to the UI.
    /// (even if one query fails, the rest still sync normally)
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

            // Right after the user changed a value via wheel/popup, do not revert to the device old value.
            // (2.5s, to cover the 2s wheel debounce plus send latency)
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

            // Sync the protection input boxes only when not being edited (protect user input).
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

    // ================= Connection status UI =================

    private void OnConnectionChanged(bool connected)
    {
        if (connected)
        {
            _pollTick = 0;      // full sync on the next poll right after (re)connect
            _identified = false; // re-identify the model on reconnect
        }
        Dispatcher.InvokeAsync(() => UpdateConnectionUi(connected));
    }

    private void UpdateConnectionUi(bool connected)
    {
        if (connected)
        {
            LiveDot.Fill = (Brush)FindResource("Ch1Accent");
            ConnText.Text = "Connected";
            ConnText.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x70, 0x77));
            _pulse?.Begin(LiveDot, true);
        }
        else
        {
            _pulse?.Stop(LiveDot);
            LiveDot.Opacity = 1;
            LiveDot.Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0x50, 0x50));
            ConnText.Text = "Reconnecting…";
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
        Timeline.SetDesiredFrameRate(anim, 10);   // cap the always-on animation at low FPS to cut idle render load
        Storyboard.SetTarget(anim, LiveDot);
        Storyboard.SetTargetProperty(anim, new PropertyPath("Opacity"));
        _pulse = new Storyboard();
        _pulse.Children.Add(anim);
    }

    // ================= Render helpers =================

    /// <summary>Render the vertical toggle - update both the full and mini switches together.</summary>
    private static void RenderToggle(ChannelUi c, bool animate)
    {
        RenderSwitch(c.Track, c.Knob, c.OutputOn, c.Accent, animate);
        RenderSwitch(c.MiniTrack, c.MiniKnob, c.OutputOn, c.Accent, animate);
    }

    /// <summary>A single vertical switch: ON = accent track+glow+knob up, OFF = gray track+knob down.</summary>
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

        // knob slide (vertical): ON=up(2px), OFF=down(24px), adjusted for the 1px track border.
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

    /// <summary>Render CV/CC mode chips: only the current mode is accent-active; both inactive when UR.</summary>
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

    /// <summary>Update the SET value (setpoint voltage/current) display.</summary>
    private static void RenderSet(ChannelUi c)
    {
        SetTextIfChanged(c.SetVText, c.SetV.ToString("0.000", CultureInfo.InvariantCulture) + " V");
        SetTextIfChanged(c.SetAText, c.SetA.ToString("0.000", CultureInfo.InvariantCulture) + " A");
    }

    /// <summary>Render the OCP/OCV checkbox + label + TRIP badge.</summary>
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

        bool tripped = enabled && fault;   // show only when protection is enabled and an event occurred
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

    // ================= User actions =================

    private void ToggleOutput(ChannelUi c)
    {
        c.OutputOn = !c.OutputOn;
        RenderToggle(c, animate: true);            // optimistic update
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

    /// <summary>Apply setpoint (popup commit): update state & SET display, then send to the device immediately.</summary>
    private void ApplySetpoint(ChannelUi c, char field, double value)
    {
        // cancel any pending wheel debounce for the same item (popup value wins)
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

    /// <summary>Parse a Tag of the form "n,X" -> (channel UI, kind).</summary>
    private bool TryParseTag(object? sender, out ChannelUi c, out string kind)
    {
        c = _channels[0];
        kind = "";
        if (sender is not FrameworkElement fe || fe.Tag is not string tag) return false;
        var parts = tag.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int ch) || ch is < 1 or > 3) return false;
        c = _channels[ch - 1];
        kind = parts[1];
        return true;
    }

    // ---- Segment (VFD) interaction ----

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

        // if the user starts adjusting another channel/item, commit the previous one immediately
        if (_wheelCh != null && (_wheelCh != c || _wheelField != field))
            CommitWheel();

        bool fine = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        double step = field == 'V' ? (fine ? 0.1 : 1.0) : (fine ? 0.01 : 0.1);
        double cur = _wheelCh == c && _wheelField == field
            ? _wheelValue
            : (field == 'V' ? c.SetV : c.SetA);
        double max = field == 'V' ? c.Rating.VMax : c.Rating.IMax;
        double value = Math.Round(Math.Clamp(cur + (e.Delta > 0 ? step : -step), 0, max), 3);

        // Update the SET display + blink only. Send to the device after adjustment stops (timer).
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

    /// <summary>Wheel adjustment ended -> stop blinking and send the last value to the device.</summary>
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
        if (Equals(tb.Tag, "blink")) return;   // already blinking
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

    // ---- Setpoint popup ----

    private void OpenSetPopup(ChannelUi c, char field)
    {
        _popCh = c;
        _popField = field;

        PopTitle.Text = $"CH{c.Channel} · Set {(field == 'V' ? "Voltage" : "Current")}";
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

    // ---- Clear TRIP ----

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

    // ---- Event handlers (XAML bindings) ----

    private void Ch1Dot_Click(object sender, MouseButtonEventArgs e) => ToggleOutput(_channels[0]);
    private void Ch2Dot_Click(object sender, MouseButtonEventArgs e) => ToggleOutput(_channels[1]);
    private void Ch3Dot_Click(object sender, MouseButtonEventArgs e) => ToggleOutput(_channels[2]);
    private void Ch1Ocp_Click(object sender, MouseButtonEventArgs e) => ToggleOcp(_channels[0]);
    private void Ch2Ocp_Click(object sender, MouseButtonEventArgs e) => ToggleOcp(_channels[1]);
    private void Ch3Ocp_Click(object sender, MouseButtonEventArgs e) => ToggleOcp(_channels[2]);
    private void Ch1Ocv_Click(object sender, MouseButtonEventArgs e) => ToggleOcv(_channels[0]);
    private void Ch2Ocv_Click(object sender, MouseButtonEventArgs e) => ToggleOcv(_channels[1]);
    private void Ch3Ocv_Click(object sender, MouseButtonEventArgs e) => ToggleOcv(_channels[2]);

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

    // ================= Window control =================

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)   // double-click -> toggle mini mode
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
        SetPopup.IsOpen = false;   // close any open popup when switching to mini
        FullBody.Visibility = _mini ? Visibility.Collapsed : Visibility.Visible;
        MiniBody.Visibility = _mini ? Visibility.Visible : Visibility.Collapsed;
    }

    // wheel on the title bar -> adjust opacity (0.35 ~ 1.0)
    private void TitleBar_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double step = e.Delta > 0 ? 0.05 : -0.05;
        Opacity = Math.Clamp(Opacity + step, 0.35, 1.0);
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => SetPinned(!Topmost);

    private void PinMenu_Click(object sender, RoutedEventArgs e) => SetPinned(PinMenu.IsChecked);

    private void CloseMenu_Click(object sender, RoutedEventArgs e) => Close();

    // ================= Embedded MCP server =================

    private void InitMcpMenu()
    {
        McpControlMenu.IsChecked = _settings.McpAllowControl;
        McpServerMenu.IsChecked = _settings.McpEnabled;
        UpdateMcpMenuState();
    }

    private void UpdateMcpMenuState()
    {
        bool running = _mcpServer.IsRunning;
        McpServerMenu.IsChecked = running;
        McpServerMenu.Header = running ? $"MCP Server (on · :{_mcpServer.Port})" : "MCP Server";
        McpCopyMenu.IsEnabled = running;
    }

    private async void McpServerMenu_Click(object sender, RoutedEventArgs e)
    {
        if (McpServerMenu.IsChecked)
            await StartMcpAsync();
        else
            await StopMcpAsync();
    }

    private async Task StartMcpAsync()
    {
        var (ok, error) = await _mcpServer.StartAsync(_settings.McpPort);
        if (ok)
        {
            _settings.McpEnabled = true;
            _settings.Save();
        }
        else
        {
            MessageBox.Show($"Cannot start the MCP server (port {_settings.McpPort}).\n{error}",
                "RigolWidget", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        UpdateMcpMenuState();
    }

    private async Task StopMcpAsync()
    {
        await _mcpServer.StopAsync();
        _settings.McpEnabled = false;
        _settings.Save();
        UpdateMcpMenuState();
    }

    private void McpControlMenu_Click(object sender, RoutedEventArgs e)
    {
        bool allow = McpControlMenu.IsChecked;
        _mcpContext.ControlAllowed = allow;
        _settings.McpAllowControl = allow;
        _settings.Save();
        DebugLog.Write($"MCP control allowed = {allow}");
    }

    private void McpCopyMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!_mcpServer.IsRunning) return;
        try
        {
            Clipboard.SetText(_mcpServer.Url);
            McpCopyMenu.Header = "MCP URL copied ✓";
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(1500);
                McpCopyMenu.Header = "Copy MCP URL";
            }));
        }
        catch { /* ignore clipboard access failures */ }
    }

    /// <summary>Callback for MCP-issued write commands (background thread): log + schedule an immediate UI resync.</summary>
    private void OnMcpCommand(string message)
    {
        DebugLog.Write(message);
        _pollTick = 0;   // full sync on the next poll -> reflect in the UI immediately
    }

    private void SetPinned(bool pinned)
    {
        Topmost = pinned;
        PinIcon.Opacity = pinned ? 1.0 : 0.35;
        PinButton.ToolTip = pinned ? "Always on Top (on)" : "Always on Top (off)";
        PinMenu.IsChecked = pinned;
    }

    private void Minimize_Click(object sender, MouseButtonEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, MouseButtonEventArgs e) => Close();

    private async void OnClosed(object? sender, EventArgs e)
    {
        CommitWheel();   // flush any unsent wheel value
        _pollCts?.Cancel();
        await _mcpServer.StopAsync();
        _conn.ConnectionChanged -= OnConnectionChanged;
        _conn.Dispose();
        _rm.Dispose();
    }

    // ================= Channel UI bundle =================

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
        public ChannelRating Rating = new(30, 3, 33, 3.3);  // default DP832 channel ratings
    }
}
