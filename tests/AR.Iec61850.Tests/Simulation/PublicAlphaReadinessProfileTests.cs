using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class PublicAlphaReadinessProfileTests
{
    [Fact]
    public async Task PublicAlphaReadinessProfile_Passes_Engine_Alpha_Gates()
    {
        var profile = await new PublicAlphaReadinessProfileBuilder().RunAsync(new PublicAlphaReadinessOptions
        {
            SclPath = MinimalStationPath(),
            Port = 0,
            ProbeTimeoutMilliseconds = 5000,
            SimulationSteps = 4
        });

        Assert.True(profile.IsReady, string.Join("; ", profile.Findings.Select(f => $"{f.Code}:{f.Message}")));
        Assert.True(profile.PassedGateCount >= 6);
        Assert.Equal(profile.GateCount, profile.PassedGateCount);
        Assert.Equal(0, profile.BlockingFindingCount);
        Assert.True(profile.SclEngineering.Ieds.Count > 0);
        Assert.True(profile.SclEngineering.DataSetCount > 0);
        Assert.True(profile.SclEngineering.ReportControlCount > 0);
        Assert.True(profile.ProcessBusBinding.IsReady);
        Assert.True(profile.GooseDiagnostics.IsHealthy);
        Assert.True(profile.SampledValuesDiagnostics.IsHealthy);
        Assert.True(profile.ReadOnlyMmsLoopback.IsReady);
        Assert.Contains(profile.Gates, x => x.Name == "mms-readonly-loopback" && x.IsPass);
    }

    [Fact]
    public async Task ToMarkdown_Renders_Public_Alpha_Readiness_Evidence()
    {
        var profile = await new PublicAlphaReadinessProfileBuilder().RunAsync(new PublicAlphaReadinessOptions
        {
            SclPath = MinimalStationPath(),
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        var markdown = profile.ToMarkdown();

        Assert.Contains("Public Alpha Readiness Profile", markdown);
        Assert.Contains("Public Alpha Gates", markdown);
        Assert.Contains("Capability Snapshot", markdown);
        Assert.Contains("Scope Boundary", markdown);
    }

    private static string MinimalStationPath()
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Scl", "minimal-station.scd");
}
