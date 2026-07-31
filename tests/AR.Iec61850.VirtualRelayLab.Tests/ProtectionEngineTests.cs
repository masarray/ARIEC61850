using AR.Iec61850.VirtualRelayLab.Protection;

namespace AR.Iec61850.VirtualRelayLab.Tests;

public sealed class ProtectionEngineTests
{
    [Fact]
    public void NormalLoad_DoesNotPickupOrTrip()
    {
        var engine = new ProtectionEngine(new ProtectionSettings());
        var start = DateTimeOffset.Parse("2026-07-31T00:00:00Z");

        var snapshot = EvaluateFor(
            engine,
            start,
            TimeSpan.FromMilliseconds(500),
            phaseA: 1.0,
            phaseB: 1.0,
            phaseC: 1.0,
            residual: 0.01,
            allowsTrip: true);

        Assert.False(snapshot.PhasePickup);
        Assert.False(snapshot.EarthPickup);
        Assert.False(snapshot.TripLatched);
        Assert.False(snapshot.Blocked);
    }

    [Fact]
    public void PhaseInstantaneousFault_TripsAfterConfiguredDelay()
    {
        var engine = new ProtectionEngine(new ProtectionSettings
        {
            PhaseInstantaneousPickupA = 4.0,
            PhaseInstantaneousDelay = TimeSpan.FromMilliseconds(60)
        });
        var start = DateTimeOffset.Parse("2026-07-31T00:00:00Z");

        var beforeDelay = EvaluateFor(
            engine,
            start,
            TimeSpan.FromMilliseconds(40),
            phaseA: 6.0,
            phaseB: 1.0,
            phaseC: 1.0,
            residual: 0.0,
            allowsTrip: true);

        var afterDelay = EvaluateFor(
            engine,
            start.AddMilliseconds(60),
            TimeSpan.FromMilliseconds(80),
            phaseA: 6.0,
            phaseB: 1.0,
            phaseC: 1.0,
            residual: 0.0,
            allowsTrip: true);

        Assert.True(beforeDelay.PhasePickup);
        Assert.False(beforeDelay.TripLatched);
        Assert.True(afterDelay.PhaseTrip);
        Assert.True(afterDelay.TripLatched);
        Assert.Equal("50P-1", afterDelay.ActiveElement);
    }

    [Fact]
    public void OperateCondition_IsBlockedWhenSmvTrustDeniesTrip()
    {
        var engine = new ProtectionEngine(new ProtectionSettings
        {
            PhaseInstantaneousPickupA = 4.0,
            PhaseInstantaneousDelay = TimeSpan.FromMilliseconds(40)
        });
        var start = DateTimeOffset.Parse("2026-07-31T00:00:00Z");

        var snapshot = EvaluateFor(
            engine,
            start,
            TimeSpan.FromMilliseconds(120),
            phaseA: 6.0,
            phaseB: 1.0,
            phaseC: 1.0,
            residual: 0.0,
            allowsTrip: false,
            healthReason: "SMPCNT GAP");

        Assert.True(snapshot.PhasePickup);
        Assert.True(snapshot.Blocked);
        Assert.False(snapshot.PhaseTrip);
        Assert.False(snapshot.TripLatched);
        Assert.Contains("SMPCNT GAP", snapshot.DecisionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void EarthFault_OperatesIndependentEarthElement()
    {
        var engine = new ProtectionEngine(new ProtectionSettings
        {
            EarthInstantaneousPickupA = 0.8,
            EarthInstantaneousDelay = TimeSpan.FromMilliseconds(80)
        });
        var start = DateTimeOffset.Parse("2026-07-31T00:00:00Z");

        var snapshot = EvaluateFor(
            engine,
            start,
            TimeSpan.FromMilliseconds(160),
            phaseA: 1.1,
            phaseB: 1.0,
            phaseC: 0.9,
            residual: 1.2,
            allowsTrip: true);

        Assert.True(snapshot.EarthPickup);
        Assert.True(snapshot.EarthTrip);
        Assert.True(snapshot.TripLatched);
        Assert.Equal("50N", snapshot.ActiveElement);
    }

    [Fact]
    public void PhaseInverseElement_AccumulatesAndTripsBelowInstantaneousPickup()
    {
        var engine = new ProtectionEngine(new ProtectionSettings
        {
            PhaseInstantaneousPickupA = 10.0,
            PhaseTimePickupA = 1.25,
            PhaseTimeMultiplier = 0.12
        });
        var start = DateTimeOffset.Parse("2026-07-31T00:00:00Z");

        var snapshot = EvaluateFor(
            engine,
            start,
            TimeSpan.FromSeconds(2.5),
            phaseA: 2.0,
            phaseB: 1.0,
            phaseC: 1.0,
            residual: 0.0,
            allowsTrip: true);

        Assert.True(snapshot.PhasePickup);
        Assert.True(snapshot.PhaseTrip);
        Assert.True(snapshot.TripLatched);
        Assert.Equal("51P", snapshot.ActiveElement);
        Assert.Equal(1.0, snapshot.PhaseTimeProgress, 6);
    }

    [Fact]
    public void Reset_ClearsLatchedTripAndTimingState()
    {
        var engine = new ProtectionEngine(new ProtectionSettings
        {
            PhaseInstantaneousDelay = TimeSpan.FromMilliseconds(40)
        });
        var start = DateTimeOffset.Parse("2026-07-31T00:00:00Z");

        var operated = EvaluateFor(
            engine,
            start,
            TimeSpan.FromMilliseconds(100),
            phaseA: 6.0,
            phaseB: 1.0,
            phaseC: 1.0,
            residual: 0.0,
            allowsTrip: true);
        Assert.True(operated.TripLatched);

        engine.Reset();
        var resetSnapshot = engine.Evaluate(new MeasurementFrame(
            start.AddSeconds(1),
            1.0,
            1.0,
            1.0,
            0.0,
            true,
            "SMV HEALTHY"));

        Assert.False(resetSnapshot.PhasePickup);
        Assert.False(resetSnapshot.TripLatched);
        Assert.Equal(0.0, resetSnapshot.PhaseTimeProgress, 6);
    }

    private static ProtectionSnapshot EvaluateFor(
        ProtectionEngine engine,
        DateTimeOffset start,
        TimeSpan duration,
        double phaseA,
        double phaseB,
        double phaseC,
        double residual,
        bool allowsTrip,
        string healthReason = "SMV HEALTHY")
    {
        var snapshot = engine.Evaluate(new MeasurementFrame(
            start,
            phaseA,
            phaseB,
            phaseC,
            residual,
            allowsTrip,
            healthReason));

        const int stepMilliseconds = 20;
        for (var elapsed = stepMilliseconds; elapsed <= duration.TotalMilliseconds; elapsed += stepMilliseconds)
        {
            snapshot = engine.Evaluate(new MeasurementFrame(
                start.AddMilliseconds(elapsed),
                phaseA,
                phaseB,
                phaseC,
                residual,
                allowsTrip,
                healthReason));
        }

        return snapshot;
    }
}
