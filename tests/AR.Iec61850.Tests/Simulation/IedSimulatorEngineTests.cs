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

    [Fact]
    public void EngineStep_Leaves_Static_Scl_Points_Readable_But_Unchanged()
    {
        var staticPoint = IedSimulatorPoint.Measurement("MMXU1.Hz.mag.f", "MX", "Hz", 50, 1, 0, isDynamic: false);
        var dynamicPoint = IedSimulatorPoint.Measurement("MMXU1.PhV.phsA.cVal.mag.f", "MX", "V", 230000, 1500, 0);
        var profile = new IedSimulatorProfile
        {
            LogicalDevices =
            [
                new IedSimulatorLogicalDevice
                {
                    Name = "IED1LD0",
                    LogicalNodes =
                    [
                        new IedSimulatorLogicalNode { Name = "MMXU1", LnClass = "MMXU", Points = [staticPoint, dynamicPoint] }
                    ]
                }
            ]
        };
        var engine = new IedSimulatorEngine(profile);
        var before = engine.PointStates.Single(state => state.Reference == staticPoint.Reference).TimestampUtc;

        var events = engine.Step(before.AddSeconds(1));
        var staticState = engine.PointStates.Single(state => state.Reference == staticPoint.Reference);

        Assert.DoesNotContain(events, change => change.Reference == staticPoint.Reference);
        Assert.Equal("50", staticState.Value);
        Assert.Equal(before, staticState.TimestampUtc);
        Assert.Contains(events, change => change.Reference == dynamicPoint.Reference);
    }
}
