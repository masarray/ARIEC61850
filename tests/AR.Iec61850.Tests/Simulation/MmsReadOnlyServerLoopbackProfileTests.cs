using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class MmsReadOnlyServerLoopbackProfileTests
{
    [Fact]
    public async Task LoopbackProfile_Unifies_Model_Association_Ber_Dispatch_And_Write_Guard()
    {
        var profile = await new MmsReadOnlyServerLoopbackProfileBuilder().RunAsync(new MmsReadOnlyServerLoopbackOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        Assert.True(profile.IsReady, string.Join("; ", profile.Findings));
        Assert.True(profile.BoundPort > 0);
        Assert.True(profile.ModelReady);
        Assert.True(profile.AssociationReady);
        Assert.True(profile.NativeBerDispatchReady);
        Assert.True(profile.ReadOnlyGuardReady);
        Assert.True(profile.LogicalDeviceCount > 0);
        Assert.True(profile.LogicalNodeCount > 0);
        Assert.True(profile.PointCount > 0);
        Assert.True(profile.DataSetCount > 0);
        Assert.True(profile.RequestCount >= 5);
        Assert.Equal(profile.RequestCount, profile.ClientDecodeSuccessCount);
        Assert.Contains(profile.Gates, x => x.Name == "write-guard" && x.IsPass);
        Assert.Contains(profile.Operations, x => x.Name == nameof(MmsReadOnlyOperation.Write) && x.Access == "blocked" && x.IsReady);
    }

    [Fact]
    public async Task ToMarkdown_Renders_ReadOnly_Loopback_Evidence()
    {
        var profile = await new MmsReadOnlyServerLoopbackProfileBuilder().RunAsync(new MmsReadOnlyServerLoopbackOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        var markdown = profile.ToMarkdown();

        Assert.Contains("MMS Read-Only Server Loopback Alpha Profile", markdown);
        Assert.Contains("Readiness Gates", markdown);
        Assert.Contains("Service Operations", markdown);
        Assert.Contains("Write", markdown);
    }
}
