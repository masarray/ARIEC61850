using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class IedSimulatorEngineTests
{
    [Fact]
    public void DefaultProfileHasDatasetsAndReportControls()
    {
        var profile = IedSimulatorProfile.CreateDefaultFeederProfile();

        Assert.True(profile.PointCount >= 10);
        Assert.Equal(2, profile.DataSets.Count);
        Assert.Contains(profile.ReportControlBlocks, x => x.Buffered);
        Assert.Contains(profile.ReportControlBlocks, x => !x.Buffered);
    }

    [Fact]
    public void EngineStepProducesDeterministicPointSnapshot()
    {
        var engine = new IedSimulatorEngine(IedSimulatorProfile.CreateDefaultFeederProfile());

        var events = engine.Step(new DateTimeOffset(2026, 6, 14, 0, 0, 0, TimeSpan.Zero));
        var snapshot = engine.CreateSnapshot(new DateTimeOffset(2026, 6, 14, 0, 0, 1, TimeSpan.Zero));

        Assert.Equal(engine.Profile.PointCount, snapshot.Points.Count);
        Assert.NotEmpty(events);
        Assert.Contains(snapshot.Points, x => x.Reference == "MMXU1.PhV.phsA.cVal.mag.f" && x.FunctionalConstraint == "MX");
    }
}
