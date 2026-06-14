using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class MmsReadOnlyListenerSkeletonTests
{
    [Fact]
    public async Task Listener_SelfProbe_Exercises_Directory_Read_DataSet_And_Write_Guard()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var listener = new MmsReadOnlyListenerSkeleton(serverProfile);

        var profile = await listener.RunSelfProbeAsync(new MmsReadOnlyListenerSkeletonOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        Assert.True(profile.IsReady, string.Join("; ", profile.Diagnostics.Select(x => x.Message)));
        Assert.True(profile.BoundPort > 0);
        Assert.Equal(1, profile.AcceptedConnectionCount);
        Assert.True(profile.RequestCount >= 5);
        Assert.True(profile.SuccessfulResponseCount >= 4);
        Assert.True(profile.FailedResponseCount >= 1);
        Assert.True(profile.WriteGuardVerified);
        Assert.Contains(profile.ProbeSteps, x => x.Operation == nameof(MmsReadOnlyOperation.Read) && x.IsServerSuccess);
        Assert.Contains(profile.ProbeSteps, x => x.Operation == nameof(MmsReadOnlyOperation.ReadDataSet) && x.IsServerSuccess);
        Assert.Contains(profile.ProbeSteps, x => x.Operation == nameof(MmsReadOnlyOperation.Write) && !x.IsServerSuccess);
    }

    [Fact]
    public async Task Listener_SelfProbe_Reports_Invalid_Target_As_Server_Failure_But_Transport_Success()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var listener = new MmsReadOnlyListenerSkeleton(serverProfile);
        var requests = new[]
        {
            new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Read, Target = "IED1LD0/MISSING.stVal" },
            new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Write, Target = "IED1LD0/XCBR1.Pos.stVal", Value = "open" }
        };

        var profile = await listener.RunSelfProbeAsync(new MmsReadOnlyListenerSkeletonOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        }, requests);

        Assert.False(profile.IsReady);
        Assert.Equal(2, profile.RequestCount);
        Assert.Equal(0, profile.SuccessfulResponseCount);
        Assert.Equal(2, profile.FailedResponseCount);
        Assert.True(profile.WriteGuardVerified);
        Assert.Contains(profile.ProbeSteps, x => x.Operation == nameof(MmsReadOnlyOperation.Read) && x.IsTransportSuccess && !x.IsServerSuccess);
    }

    [Fact]
    public async Task Markdown_Contains_Listener_Evidence()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var listener = new MmsReadOnlyListenerSkeleton(serverProfile);

        var profile = await listener.RunSelfProbeAsync(new MmsReadOnlyListenerSkeletonOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });
        var markdown = profile.ToMarkdown();

        Assert.Contains("# MMS Listener Skeleton Profile", markdown);
        Assert.Contains("loopback probe", markdown);
        Assert.Contains("Write guard verified", markdown);
        Assert.Contains("ReadDataSet", markdown);
    }
}
