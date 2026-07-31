namespace AR.Iec61850.VirtualRelayLab.Protection;

public sealed record ProtectionSettings
{
    public double PhaseInstantaneousPickupA { get; init; } = 4.0;
    public TimeSpan PhaseInstantaneousDelay { get; init; } = TimeSpan.FromMilliseconds(60);
    public double PhaseTimePickupA { get; init; } = 1.25;
    public double PhaseTimeMultiplier { get; init; } = 0.12;
    public double EarthInstantaneousPickupA { get; init; } = 0.80;
    public TimeSpan EarthInstantaneousDelay { get; init; } = TimeSpan.FromMilliseconds(100);
    public double EarthTimePickupA { get; init; } = 0.30;
    public double EarthTimeMultiplier { get; init; } = 0.15;
    public double DropoutRatio { get; init; } = 0.95;
}

public sealed record MeasurementFrame(
    DateTimeOffset Timestamp,
    double PhaseA,
    double PhaseB,
    double PhaseC,
    double Residual,
    bool SmvAllowsTrip,
    string SmvHealthReason);

public sealed record ProtectionSnapshot(
    bool PhasePickup,
    bool EarthPickup,
    bool PhaseTrip,
    bool EarthTrip,
    bool TripLatched,
    bool Blocked,
    string ActiveElement,
    string DecisionReason,
    double PhaseTimeProgress,
    double EarthTimeProgress,
    double MaxPhaseCurrent,
    double ResidualCurrent);

public sealed class ProtectionEngine
{
    private readonly ProtectionSettings _settings;
    private TimeSpan _phaseInstantaneousElapsed;
    private TimeSpan _earthInstantaneousElapsed;
    private double _phaseInverseProgress;
    private double _earthInverseProgress;
    private DateTimeOffset? _previousTimestamp;
    private bool _tripLatched;

    public ProtectionEngine(ProtectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public ProtectionSnapshot Evaluate(MeasurementFrame frame)
    {
        var delta = _previousTimestamp is null
            ? TimeSpan.Zero
            : frame.Timestamp - _previousTimestamp.Value;
        _previousTimestamp = frame.Timestamp;

        if (delta < TimeSpan.Zero || delta > TimeSpan.FromSeconds(1))
            delta = TimeSpan.Zero;

        var maxPhase = Math.Max(frame.PhaseA, Math.Max(frame.PhaseB, frame.PhaseC));
        var phase50Pickup = maxPhase >= _settings.PhaseInstantaneousPickupA;
        var earth50Pickup = frame.Residual >= _settings.EarthInstantaneousPickupA;
        var phase51Pickup = maxPhase >= _settings.PhaseTimePickupA;
        var earth51Pickup = frame.Residual >= _settings.EarthTimePickupA;

        _phaseInstantaneousElapsed = AccumulateDefiniteTime(
            _phaseInstantaneousElapsed,
            phase50Pickup,
            delta,
            maxPhase < _settings.PhaseInstantaneousPickupA * _settings.DropoutRatio);

        _earthInstantaneousElapsed = AccumulateDefiniteTime(
            _earthInstantaneousElapsed,
            earth50Pickup,
            delta,
            frame.Residual < _settings.EarthInstantaneousPickupA * _settings.DropoutRatio);

        _phaseInverseProgress = AccumulateInverse(
            _phaseInverseProgress,
            maxPhase,
            _settings.PhaseTimePickupA,
            _settings.PhaseTimeMultiplier,
            delta,
            phase51Pickup);

        _earthInverseProgress = AccumulateInverse(
            _earthInverseProgress,
            frame.Residual,
            _settings.EarthTimePickupA,
            _settings.EarthTimeMultiplier,
            delta,
            earth51Pickup);

        var phase50Operate = _phaseInstantaneousElapsed >= _settings.PhaseInstantaneousDelay;
        var earth50Operate = _earthInstantaneousElapsed >= _settings.EarthInstantaneousDelay;
        var phase51Operate = _phaseInverseProgress >= 1.0;
        var earth51Operate = _earthInverseProgress >= 1.0;

        var phaseOperate = phase50Operate || phase51Operate;
        var earthOperate = earth50Operate || earth51Operate;
        var blocked = (phaseOperate || earthOperate) && !frame.SmvAllowsTrip;

        if ((phaseOperate || earthOperate) && frame.SmvAllowsTrip)
            _tripLatched = true;

        var activeElement = phase50Operate ? "50P-1"
            : earth50Operate ? "50N"
            : phase51Operate ? "51P"
            : earth51Operate ? "51N"
            : phase50Pickup ? "50P-1 PICKUP"
            : earth50Pickup ? "50N PICKUP"
            : phase51Pickup ? "51P START"
            : earth51Pickup ? "51N START"
            : "READY";

        var reason = blocked
            ? $"TRIP BLOCKED · {frame.SmvHealthReason}"
            : _tripLatched
                ? $"TRIP LATCHED · {activeElement}"
                : phase50Pickup || earth50Pickup || phase51Pickup || earth51Pickup
                    ? $"OPERATING · {activeElement}"
                    : "Measurements stable · no pickup";

        return new ProtectionSnapshot(
            phase50Pickup || phase51Pickup,
            earth50Pickup || earth51Pickup,
            phaseOperate && frame.SmvAllowsTrip,
            earthOperate && frame.SmvAllowsTrip,
            _tripLatched,
            blocked,
            activeElement,
            reason,
            Math.Clamp(_phaseInverseProgress, 0, 1),
            Math.Clamp(_earthInverseProgress, 0, 1),
            maxPhase,
            frame.Residual);
    }

    public void Reset()
    {
        _phaseInstantaneousElapsed = TimeSpan.Zero;
        _earthInstantaneousElapsed = TimeSpan.Zero;
        _phaseInverseProgress = 0;
        _earthInverseProgress = 0;
        _previousTimestamp = null;
        _tripLatched = false;
    }

    private static TimeSpan AccumulateDefiniteTime(
        TimeSpan current,
        bool pickup,
        TimeSpan delta,
        bool droppedOut)
    {
        if (pickup)
            return current + delta;
        return droppedOut ? TimeSpan.Zero : current;
    }

    private static double AccumulateInverse(
        double current,
        double measured,
        double pickup,
        double timeMultiplier,
        TimeSpan delta,
        bool started)
    {
        if (!started || pickup <= 0 || measured <= pickup)
            return Math.Max(0, current - delta.TotalSeconds * 2.0);

        var multiple = measured / pickup;
        var denominator = Math.Pow(multiple, 0.02) - 1.0;
        if (denominator <= 0.000001)
            return current;

        var operateSeconds = timeMultiplier * 0.14 / denominator;
        operateSeconds = Math.Clamp(operateSeconds, 0.02, 120.0);
        return current + delta.TotalSeconds / operateSeconds;
    }
}
