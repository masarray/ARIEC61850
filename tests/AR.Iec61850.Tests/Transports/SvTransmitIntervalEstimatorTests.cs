using AR.Iec61850.Transports.Npcap;

namespace AR.Iec61850.Tests.Transports;

public sealed class SvTransmitIntervalEstimatorTests
{
    [Fact]
    public void SingleLateObservation_DoesNotBecomePermanentNominalRate()
    {
        var estimator = new SvTransmitIntervalEstimator(20, 5_000, requiredConsistentIntervals: 4);

        estimator.Observe(1_000); // Scheduler-late first interval.
        Assert.Equal(0, estimator.NominalIntervalTicks);

        estimator.Observe(250);
        estimator.Observe(248);
        estimator.Observe(252);
        Assert.Equal(0, estimator.NominalIntervalTicks);

        estimator.Observe(251);

        Assert.InRange(estimator.NominalIntervalTicks, 248, 252);
    }

    [Fact]
    public void InconsistentIntervals_DoNotActivatePacing()
    {
        var estimator = new SvTransmitIntervalEstimator(20, 5_000, requiredConsistentIntervals: 4);

        foreach (var interval in new long[] { 250, 800, 240, 1_200, 260, 700 })
            estimator.Observe(interval);

        Assert.Equal(0, estimator.NominalIntervalTicks);
        Assert.Equal(1, estimator.CandidateCount);
    }

    [Fact]
    public void ActiveNominalRate_IgnoresLongSchedulerStall()
    {
        var estimator = CreateActiveEstimator();
        var nominalBeforeStall = estimator.NominalIntervalTicks;

        estimator.Observe(4_000);

        Assert.Equal(nominalBeforeStall, estimator.NominalIntervalTicks);
    }

    [Fact]
    public void ActiveNominalRate_DoesNotRatchetOnModerateLateness()
    {
        var estimator = CreateActiveEstimator();
        var nominal = estimator.NominalIntervalTicks;

        foreach (var delayedInterval in new long[] { 300, 310, 290, 320, 300, 305, 295, 315 })
            estimator.Observe(delayedInterval);

        Assert.Equal(nominal, estimator.NominalIntervalTicks);
    }

    [Fact]
    public void ActiveNominalRate_DoesNotFeedPacedIntervalsBackIntoEstimator()
    {
        var estimator = CreateActiveEstimator();
        var nominal = estimator.NominalIntervalTicks;

        estimator.Observe(248);
        estimator.Observe(252);
        estimator.Observe(260);

        Assert.Equal(nominal, estimator.NominalIntervalTicks);
    }

    private static SvTransmitIntervalEstimator CreateActiveEstimator()
    {
        var estimator = new SvTransmitIntervalEstimator(20, 5_000, requiredConsistentIntervals: 4);
        foreach (var interval in new long[] { 250, 249, 251, 250 })
            estimator.Observe(interval);
        Assert.InRange(estimator.NominalIntervalTicks, 249, 251);
        return estimator;
    }
}
