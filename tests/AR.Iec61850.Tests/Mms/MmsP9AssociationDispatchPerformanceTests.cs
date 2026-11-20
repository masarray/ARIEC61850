using System.Diagnostics;
using AR.Iec61850.Mms;
using Xunit.Abstractions;

namespace AR.Iec61850.Tests.Mms;

/// <summary>
/// Deterministic synthetic workload with *observational* timing/allocation
/// evidence. Never gate CI against machine-specific throughput thresholds.
/// No physical IED, network or installed driver is required.
/// </summary>
public sealed class MmsP9AssociationDispatchPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public MmsP9AssociationDispatchPerformanceTests(ITestOutputHelper output)
        => _output = output;

    [Fact]
    public async Task StableRegistryLookup_DoesNotAllocatePerReport()
    {
        await using var client = new MmsClientSession();
        var sessions = MmsP9MultiRcbDispatchTests.FourSessions();
        foreach (var session in sessions)
            client.RegisterPersistentReportMonitor(session);

        var expected = client.SnapshotPersistentReportMonitors();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 16384; i++)
        {
            if (!ReferenceEquals(expected, client.SnapshotPersistentReportMonitors()))
                throw new InvalidOperationException("Stable RCB membership unexpectedly republished.");
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        _output.WriteLine($"P9 registry: calls=16384, allocatedBytes={bytes}, " +
            $"bytesPerLookup={(double)bytes / 16384:0.0000}");
        Assert.InRange(bytes, 0, 256);
        Assert.Equal(4, expected.Length);
    }

    [Fact]
    public async Task ConcurrentShutdown_PreventsPostResetPendingFrameAdmission()
    {
        await using var client = new MmsClientSession();
        var sessions = MmsP9MultiRcbDispatchTests.FourSessions(pendingCapacity: 128);
        foreach (var session in sessions)
            client.RegisterPersistentReportMonitor(session);

        var frame = new MmsReportFrame();
        using var start = new ManualResetEventSlim(false);
        var writers = Enumerable.Range(0, 4).Select(index => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 4096; i++)
                sessions[index].TryQueuePendingReport(frame);
        })).ToArray();

        start.Set();
        // Concurrent shutdown, not a continuation that waits for all writers.
        client.ClearPersistentReportMonitors();
        await Task.WhenAll(writers);

        Assert.Empty(client.SnapshotPersistentReportMonitors());
        foreach (var session in sessions)
        {
            Assert.True(session.IsStopped);
            Assert.Equal(0, session.PendingReportCount);
            Assert.False(session.TryQueuePendingReport(frame));
        }
    }

    [Fact]
    public async Task FourRcbBerDispatch_RecordsLatencyAndAllocationsWithoutFlakySpeedGate()
    {
        await using var client = new MmsClientSession();
        var sessions = MmsP9MultiRcbDispatchTests.FourSessions();
        foreach (var session in sessions)
            client.RegisterPersistentReportMonitor(session);

        var pdus = sessions.Select(s => MmsP9MultiRcbDispatchTests.BuildReport(
            s.ReportControl.ReportId, s.Plan.DataSetReference.Replace('.', '$'), true)).ToArray();

        // Same measured path as production (decode + identity + projection +
        // bounded FIFO), but prebuild wire bytes so fixture generation is not
        // falsely counted as decoder/dispatch cost.
        for (var warmup = 0; warmup < 8; warmup++)
            RouteAndDrainOneRound(client, sessions, pdus);

        const int rounds = 128;
        const int frameCount = rounds * 4;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < rounds; i++)
            RouteAndDrainOneRound(client, sessions, pdus);
        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocations = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        _output.WriteLine(
            $"P9 four-RCB BER dispatch: frames={frameCount}, durationMs={elapsed.TotalMilliseconds:0.###}, " +
            $"framesPerSecond={frameCount / Math.Max(elapsed.TotalSeconds, 1e-9):0.##}, " +
            $"allocatedBytes={allocations}, bytesPerFrame={(double)allocations / frameCount:0.##}. " +
            "Synthetic diagnostic workload, not a field/production SLA.");
        foreach (var session in sessions)
        {
            Assert.Equal(rounds + 8, session.RoutedReportCount);
            Assert.Equal(0, session.PendingReportCount);
            Assert.Equal(0, session.PendingDeliveryOverflowCount);
        }
        Assert.Equal(0, client.UnroutedPersistentReportCount);
        Assert.True(elapsed >= TimeSpan.Zero);
    }

    private static void RouteAndDrainOneRound(
        MmsClientSession client,
        MmsPersistentReportMonitorSession[] sessions,
        byte[][] pdus)
    {
        for (var index = 0; index < sessions.Length; index++)
        {
            client.RoutePersistentInformationReport(pdus[index]);
            if (!sessions[index].TryDequeuePendingReport(out _))
                throw new InvalidOperationException("No exact RCB delivery during benchmark workload.");
        }
    }
}
