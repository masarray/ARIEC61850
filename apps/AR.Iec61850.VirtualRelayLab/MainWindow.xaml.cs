using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AR.Iec61850.VirtualRelayLab.Protection;

namespace AR.Iec61850.VirtualRelayLab;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly ProtectionEngine _protectionEngine = new(new ProtectionSettings());
    private bool _running;
    private bool _faultActive;
    private bool _smvHealthy = true;
    private int _sampleCounter;
    private DateTimeOffset _startedAt;
    private bool _pickupLogged;
    private bool _tripLogged;

    public MainWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        _timer.Tick += Timer_Tick;
        SetHealthyPresentation();
        UpdateMeasurements(1.00, 1.00, 1.00, 0.00);
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        _running = !_running;
        if (_running)
        {
            _startedAt = DateTimeOffset.UtcNow;
            _timer.Start();
            RunButton.Content = "Pause lab";
            FooterStatusText.Text = "Protection pipeline running from deterministic SMV sample frames.";
            EventTraceText.Text = "RUN         Measurement pipeline started\nHEALTHY     SMV trust gate permits trip";
        }
        else
        {
            _timer.Stop();
            RunButton.Content = "Run lab";
            FooterStatusText.Text = "Laboratory paused. The two-cycle oscilloscope snapshot remains available for inspection.";
        }
    }

    private void InjectFault_Click(object sender, RoutedEventArgs e)
    {
        EnsureRunning();
        _faultActive = true;
        FooterStatusText.Text = "A-G fault injected. Observe IA and 3I0, protection pickup, operate delay, then virtual trip.";
        AppendEvent("FAULT       A-G inception");
    }

    private void DegradeSmv_Click(object sender, RoutedEventArgs e)
    {
        EnsureRunning();
        _smvHealthy = !_smvHealthy;
        if (_smvHealthy)
        {
            AppendEvent("SMV         continuity restored");
            FooterStatusText.Text = "SMV continuity restored. Trip permission is available after healthy measurement refresh.";
        }
        else
        {
            AppendEvent("SMV BLOCK   smpCnt discontinuity");
            FooterStatusText.Text = "SMV degraded. Protection remains visible, but the trip trust gate is blocked.";
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _faultActive = false;
        _smvHealthy = true;
        _sampleCounter = 0;
        _pickupLogged = false;
        _tripLogged = false;
        _protectionEngine.Reset();
        SmvScope.PickupPosition = double.NaN;
        SmvScope.TripPosition = double.NaN;
        EventTraceText.Text = "RESET       Trip latch and timers cleared\nHEALTHY     SMV trust gate permits trip";
        FooterStatusText.Text = "Relay reset complete. The laboratory is ready for another deterministic test.";
        UpdateMeasurements(1.00, 1.00, 1.00, 0.00);
        UpdateProtectionPresentation(new ProtectionSnapshot(
            false, false, false, false, false, false,
            "READY", "Measurements stable · no pickup", 0, 0, 1, 0));
    }

    private void OpenAlgorithmEditor_Click(object sender, RoutedEventArgs e)
    {
        var editor = new AlgorithmEditorWindow
        {
            Owner = this
        };
        editor.ShowDialog();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _sampleCounter = (_sampleCounter + 80) % 4000;
        var elapsed = (DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
        var breathing = Math.Sin(elapsed * 1.7) * 0.008;

        var ia = _faultActive ? 6.40 + Math.Sin(elapsed * 2.3) * 0.03 : 1.00 + breathing;
        var ib = _faultActive ? 1.04 + breathing : 1.00 - breathing * 0.6;
        var ic = _faultActive ? 1.01 - breathing : 1.00 + breathing * 0.4;
        var residual = _faultActive ? 5.36 + Math.Sin(elapsed * 2.1) * 0.02 : 0.01;

        var frame = new MeasurementFrame(
            DateTimeOffset.UtcNow,
            ia,
            ib,
            ic,
            residual,
            _smvHealthy,
            _smvHealthy ? "SMV HEALTHY" : "SMPCNT GAP / STREAM UNTRUSTED");

        var snapshot = _protectionEngine.Evaluate(frame);
        UpdateMeasurements(ia, ib, ic, residual);
        UpdateProtectionPresentation(snapshot);

        if (snapshot.PhasePickup || snapshot.EarthPickup)
        {
            if (!_pickupLogged)
            {
                _pickupLogged = true;
                SmvScope.PickupPosition = 0.56;
                AppendEvent($"PICKUP      {snapshot.ActiveElement}");
            }
        }

        if (snapshot.TripLatched && !_tripLogged)
        {
            _tripLogged = true;
            SmvScope.TripPosition = 0.72;
            AppendEvent($"TRIP        {snapshot.ActiveElement}");
        }
    }

    private void EnsureRunning()
    {
        if (_running)
            return;

        _running = true;
        _startedAt = DateTimeOffset.UtcNow;
        _timer.Start();
        RunButton.Content = "Pause lab";
    }

    private void UpdateMeasurements(double ia, double ib, double ic, double residual)
    {
        SmvScope.PhaseA = ia;
        SmvScope.PhaseB = ib;
        SmvScope.PhaseC = ic;
        SmvScope.Residual = residual;

        IaValueText.Text = $"{ia:0.00} A";
        IbValueText.Text = $"{ib:0.00} A";
        IcValueText.Text = $"{ic:0.00} A";
        ResidualValueText.Text = $"{residual:0.00} A";
        LcdIaText.Text = $"{ia,6:0.00} A";
        LcdIbText.Text = $"{ib,6:0.00} A";
        LcdIcText.Text = $"{ic,6:0.00} A";
        LcdResidualText.Text = $"{residual,6:0.00} A";
        SampleCounterText.Text = $"  ·  smpCnt {_sampleCounter:0000}";
    }

    private void UpdateProtectionPresentation(ProtectionSnapshot snapshot)
    {
        ProtectionReasonText.Text = $"  ·  {snapshot.DecisionReason}";
        ActiveElementText.Text = snapshot.ActiveElement;

        var pickup = snapshot.PhasePickup || snapshot.EarthPickup;
        SetLed(PickupLed, pickup ? Indicator.Warning : Indicator.Off);
        SetLed(TripLed, snapshot.TripLatched ? Indicator.Trip : Indicator.Off);
        SetLed(PhaseALed, snapshot.PhasePickup ? Indicator.Warning : Indicator.Off);
        SetLed(PhaseBLed, Indicator.Off);
        SetLed(PhaseCLed, Indicator.Off);
        SetLed(EarthLed, snapshot.EarthPickup ? Indicator.Warning : Indicator.Off);
        SetLed(BlockedLed, snapshot.Blocked || !_smvHealthy ? Indicator.Warning : Indicator.Off);

        Phase50StateText.Text = snapshot.PhaseTrip ? "OPERATED" : snapshot.PhasePickup ? "PICKUP" : "READY";
        Phase51StateText.Text = snapshot.PhaseTimeProgress >= 1 ? "OPERATED" : snapshot.PhaseTimeProgress > 0 ? $"TIMING {snapshot.PhaseTimeProgress:P0}" : "READY";
        Earth50StateText.Text = snapshot.EarthTrip ? "OPERATED" : snapshot.EarthPickup ? "PICKUP" : "READY";
        Earth51StateText.Text = snapshot.EarthTimeProgress >= 1 ? "OPERATED" : snapshot.EarthTimeProgress > 0 ? $"TIMING {snapshot.EarthTimeProgress:P0}" : "READY";

        Phase50Progress.Value = snapshot.PhasePickup ? snapshot.PhaseTrip ? 100 : 70 : 0;
        Phase51Progress.Value = snapshot.PhaseTimeProgress * 100;
        Earth50Progress.Value = snapshot.EarthPickup ? snapshot.EarthTrip ? 100 : 70 : 0;
        Earth51Progress.Value = snapshot.EarthTimeProgress * 100;

        var stateBrush = snapshot.TripLatched
            ? FindBrush("TripBrush")
            : pickup
                ? FindBrush("WarningBrush")
                : FindBrush("MutedBrush");
        Phase50StateText.Foreground = stateBrush;
        Phase51StateText.Foreground = stateBrush;
        Earth50StateText.Foreground = stateBrush;
        Earth51StateText.Foreground = stateBrush;

        if (_smvHealthy)
        {
            SetHealthyPresentation();
            PermissionBadge.Background = new SolidColorBrush(Color.FromRgb(234, 245, 236));
            PermissionBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(185, 216, 191));
            PermissionText.Text = "TRIP PERMITTED";
            PermissionText.Foreground = FindBrush("HealthyBrush");
            TrustStateText.Text = "HEALTHY";
            TrustStateText.Foreground = new SolidColorBrush(Color.FromRgb(49, 94, 64));
            TrustDetailText.Text = "continuous · mapped\nsmpSynch 2 · fresh";
        }
        else
        {
            SetLed(HealthyLed, Indicator.Warning);
            SetLed(TopHealthLed, Indicator.Warning);
            TopHealthText.Text = "SMV DEGRADED";
            PermissionBadge.Background = new SolidColorBrush(Color.FromRgb(250, 242, 225));
            PermissionBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(224, 195, 136));
            PermissionText.Text = "TRIP BLOCKED";
            PermissionText.Foreground = FindBrush("WarningBrush");
            TrustStateText.Text = "BLOCKED";
            TrustStateText.Foreground = FindBrush("WarningBrush");
            TrustDetailText.Text = "smpCnt discontinuity\nmeasurement visible only";
        }

        LcdStatusText.Text = snapshot.Blocked
            ? "BLOCKED · SMV UNTRUSTED"
            : snapshot.TripLatched
                ? $"TRIP · {snapshot.ActiveElement}"
                : pickup
                    ? $"PICKUP · {snapshot.ActiveElement}"
                    : "READY · SMV HEALTHY";
    }

    private void SetHealthyPresentation()
    {
        SetLed(HealthyLed, Indicator.Healthy);
        SetLed(TopHealthLed, Indicator.Healthy);
        TopHealthText.Text = "LAB READY";
    }

    private void AppendEvent(string line)
    {
        var lines = EventTraceText.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(3)
            .ToList();
        lines.Add(line);
        EventTraceText.Text = string.Join(Environment.NewLine, lines.TakeLast(4));
    }

    private Brush FindBrush(string resourceKey)
    {
        return (Brush)FindResource(resourceKey);
    }

    private static void SetLed(Ellipse led, Indicator indicator)
    {
        led.Fill = indicator switch
        {
            Indicator.Healthy => new SolidColorBrush(Color.FromRgb(84, 174, 94)),
            Indicator.Warning => new SolidColorBrush(Color.FromRgb(218, 157, 48)),
            Indicator.Trip => new SolidColorBrush(Color.FromRgb(214, 70, 66)),
            _ => new SolidColorBrush(Color.FromRgb(83, 97, 107))
        };
        led.Stroke = indicator == Indicator.Off
            ? new SolidColorBrush(Color.FromRgb(107, 123, 134))
            : new SolidColorBrush(Color.FromRgb(238, 245, 239));
    }

    private enum Indicator
    {
        Off,
        Healthy,
        Warning,
        Trip
    }
}
