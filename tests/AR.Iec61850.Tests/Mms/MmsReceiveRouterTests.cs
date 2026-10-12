using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReceiveRouterTests
{
    [Fact]
    public void DecodeEnvelope_ClassifiesConfirmedResponseWithInvokeId()
    {
        var payload = BuildConfirmedReadResponse(invokeId: 7);

        var envelope = MmsPduEnvelope.Decode(payload);

        Assert.Equal(MmsPduKind.ConfirmedResponse, envelope.Kind);
        Assert.Equal(7, envelope.InvokeId);
        Assert.True(envelope.IsConfirmedServiceResult);
        Assert.False(envelope.IsInformationReport);
    }

    [Fact]
    public void DecodeEnvelope_ClassifiesInformationReport()
    {
        var payload = BuildInformationReport();

        var envelope = MmsPduEnvelope.Decode(payload);

        Assert.Equal(MmsPduKind.Unconfirmed, envelope.Kind);
        Assert.True(envelope.IsInformationReport);
        Assert.Null(envelope.InvokeId);
    }

    [Fact]
    public void DecodeEnvelope_ClassifiesConfirmedErrorWithContextInvokeId()
    {
        var serviceError = BerWriter.EncodeTlv(0xA2, BerWriter.EncodeTlv(0x80, [0x01]));
        var mms = BerWriter.EncodeTlv(
            0xA2,
            BerWriter.EncodeTlv(0x80, [0x09])
                .Concat(serviceError)
                .ToArray());
        var payload = MmsPresentation.WrapIsoPresentationPData(mms);

        var envelope = MmsPduEnvelope.Decode(payload);

        Assert.Equal(MmsPduKind.ConfirmedError, envelope.Kind);
        Assert.Equal(9, envelope.InvokeId);
        Assert.True(envelope.IsConfirmedServiceResult);
    }

    [Fact]
    public void Route_QueuesInformationReportSeparatelyFromConfirmedResponse()
    {
        var router = new MmsReceiveRouter();

        var reportRoute = router.Route(BuildInformationReport());
        var responseRoute = router.Route(BuildConfirmedReadResponse(invokeId: 12));

        Assert.Equal(MmsReceiveRouteAction.QueuedInformationReport, reportRoute.Action);
        Assert.Equal(MmsReceiveRouteAction.QueuedConfirmedResult, responseRoute.Action);
        Assert.Equal(1, router.QueuedInformationReportCount);
        Assert.Equal(1, router.QueuedConfirmedResultCount);

        Assert.True(router.TryDequeueInformationReport(out var report));
        Assert.True(report.IsInformationReport);

        Assert.True(router.TryDequeueConfirmedResult(12, out var response));
        Assert.Equal(MmsPduKind.ConfirmedResponse, response.Kind);
        Assert.Equal(12, response.InvokeId);
    }

    [Fact]
    public void Route_KeepsOtherInvokeUntilOwnerRequestsIt()
    {
        var router = new MmsReceiveRouter();

        router.Route(BuildConfirmedReadResponse(invokeId: 22));

        Assert.False(router.TryDequeueConfirmedResult(21, out _));
        Assert.True(router.TryDequeueConfirmedResult(22, out var response));
        Assert.Equal(22, response.InvokeId);
    }

    [Fact]
    public async Task Route_FansOutInformationReportWithoutStealingLegacyQueue()
    {
        var router = new MmsReceiveRouter();
        await using var first = router.SubscribeInformationReports();
        await using var second = router.SubscribeInformationReports();

        router.Route(BuildInformationReport());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var firstEnvelope = await first.Reader.ReadAsync(timeout.Token);
        var secondEnvelope = await second.Reader.ReadAsync(timeout.Token);
        Assert.True(firstEnvelope.IsInformationReport);
        Assert.True(secondEnvelope.IsInformationReport);
        Assert.True(router.TryDequeueInformationReport(out var queued));
        Assert.True(queued.IsInformationReport);
    }

    [Fact]
    public async Task SubscriberBurstOverCapacity_FaultsWithoutSilentDrop_WhileCanonicalFifoRetainsEveryFrame()
    {
        var router = new MmsReceiveRouter();
        await using var slow = router.SubscribeInformationReports(capacity: 8);
        var sent = Enumerable.Range(1, 80).Select(BuildInformationReport).ToArray();

        foreach (var bytes in sent)
            Assert.Equal(MmsReceiveRouteAction.QueuedInformationReport, router.Route(bytes).Action);

        // The bounded observer has *all eight original* frames, not the eight
        // newest; its terminal error explicitly declares the missing 72.
        for (var i = 0; i < 8; i++)
        {
            Assert.True(slow.Reader.TryRead(out var envelope));
            Assert.Equal(sent[i], envelope.PresentationPayload);
        }

        Assert.False(slow.Reader.TryRead(out _));
        var overflow = await Assert.ThrowsAsync<IOException>(async () =>
            await slow.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("exceeded its bounded capacity", overflow.Message);

        // The actual static-monitor source of truth MUST NEVER lose those
        // frames when an optional observer has fallen behind.
        Assert.Equal(80, router.QueuedInformationReportCount);
        for (var i = 0; i < sent.Length; i++)
        {
            Assert.True(router.TryDequeueInformationReport(out var queued));
            Assert.Equal(sent[i], queued.PresentationPayload);
        }
        Assert.False(router.TryDequeueInformationReport(out _));
    }

    [Fact]
    public async Task SubscriberOverflow_IsolatedFromFastSubscriberAndConfirmedMmsResponses()
    {
        var router = new MmsReceiveRouter();
        await using var slow = router.SubscribeInformationReports(capacity: 1);
        await using var fast = router.SubscribeInformationReports(capacity: 1);

        for (var i = 1; i <= 16; i++)
        {
            var frame = BuildInformationReport(i);
            router.Route(frame);
            Assert.True(fast.Reader.TryRead(out var received));
            Assert.Equal(frame, received.PresentationPayload);
        }

        router.Route(BuildConfirmedReadResponse(invokeId: 42));
        Assert.True(router.TryDequeueConfirmedResult(42, out var confirmation));
        Assert.Equal(42, confirmation.InvokeId);
        Assert.False(fast.Reader.Completion.IsCompleted);

        Assert.True(slow.Reader.TryRead(out var original));
        Assert.Equal(BuildInformationReport(1), original.PresentationPayload);
        await Assert.ThrowsAsync<IOException>(async () =>
            await slow.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(2)));

        // Faulting one slow observer cannot corrupt or drop canonical report
        // order, even while confirmed-service responses arrive.
        for (var i = 1; i <= 16; i++)
        {
            Assert.True(router.TryDequeueInformationReport(out var queued));
            Assert.Equal(BuildInformationReport(i), queued.PresentationPayload);
        }
    }

    [Fact]
    public async Task SubscriberAtCapacity_DoesNotFalseFail_AndCanReceiveAfterDrain()
    {
        var router = new MmsReceiveRouter();
        await using var sub = router.SubscribeInformationReports(capacity: 2);
        var a = BuildInformationReport(1);
        var b = BuildInformationReport(2);
        var c = BuildInformationReport(3);
        router.Route(a);
        router.Route(b);

        Assert.False(sub.Reader.Completion.IsCompleted);
        Assert.True(sub.Reader.TryRead(out var first));
        Assert.Equal(a, first.PresentationPayload);
        router.Route(c);

        Assert.True(sub.Reader.TryRead(out var second));
        Assert.True(sub.Reader.TryRead(out var third));
        Assert.Equal(b, second.PresentationPayload);
        Assert.Equal(c, third.PresentationPayload);
        Assert.False(sub.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task WaitForInformationReportAsync_ReturnsImmediately_WhenQueueAlreadyHasReport()
    {
        var router = new MmsReceiveRouter();
        router.Route(BuildInformationReport());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var available = await router.WaitForInformationReportAsync(
            TimeSpan.FromSeconds(1),
            timeout.Token);

        Assert.True(available);
        Assert.True(router.TryDequeueInformationReport(out var queued));
        Assert.True(queued.IsInformationReport);
    }

    [Fact]
    public async Task WaitForInformationReportAsync_WakesFromFutureReport_WithoutPolling()
    {
        var router = new MmsReceiveRouter();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var wait = router.WaitForInformationReportAsync(
            TimeSpan.FromSeconds(1),
            timeout.Token);

        await Task.Yield();
        Assert.False(wait.IsCompleted);

        router.Route(BuildInformationReport());

        Assert.True(await wait);
        Assert.True(router.TryDequeueInformationReport(out var queued));
        Assert.True(queued.IsInformationReport);
    }

    [Fact]
    public async Task WaitForInformationReportAsync_CoalescesBurstIntoSingleWake()
    {
        var router = new MmsReceiveRouter();
        router.Route(BuildInformationReport());
        router.Route(BuildInformationReport());

        Assert.True(await router.WaitForInformationReportAsync(TimeSpan.FromSeconds(1)));
        Assert.True(router.TryDequeueInformationReport(out _));
        Assert.True(router.TryDequeueInformationReport(out _));

        var staleWake = await router.WaitForInformationReportAsync(TimeSpan.FromMilliseconds(20));
        Assert.False(staleWake);
    }

    [Fact]
    public async Task WaitForInformationReportAsync_TimesOut_WhenNoReportArrives()
    {
        var router = new MmsReceiveRouter();

        var available = await router.WaitForInformationReportAsync(
            TimeSpan.FromMilliseconds(20));

        Assert.False(available);
        Assert.Equal(0, router.QueuedInformationReportCount);
    }

    [Fact]
    public async Task Clear_FaultsActiveInformationReportSubscribers()
    {
        var router = new MmsReceiveRouter();
        await using var subscription = router.SubscribeInformationReports();

        router.Clear();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<IOException>(async () =>
            await subscription.Reader.Completion.WaitAsync(timeout.Token));
    }

    [Fact]
    public void CanonicalCapacityExceeded_PreservesOriginalFifoAndFailsClosedUntilClear()
    {
        var router = new MmsReceiveRouter(canonicalReportCapacity: 3);
        var original = Enumerable.Range(1, 3).Select(BuildInformationReport).ToArray();
        foreach (var report in original)
            router.Route(report);

        var failure = Assert.Throws<IOException>(() => router.Route(BuildInformationReport(4)));
        Assert.Contains("bounded frame/byte capacity", failure.Message);
        Assert.Equal(3, router.QueuedInformationReportCount);

        for (var i = 0; i < original.Length; i++)
        {
            Assert.True(router.TryDequeueInformationReport(out var report));
            Assert.Equal(original[i], report.PresentationPayload);
        }

        Assert.Throws<IOException>(() => router.TryDequeueInformationReport(out _));
        Assert.Throws<IOException>(() => router.Route(BuildConfirmedReadResponse(42)));
        router.Clear();

        Assert.Equal(0, router.QueuedInformationReportCount);
        router.Route(BuildInformationReport(5));
        Assert.True(router.TryDequeueInformationReport(out _));
        Assert.False(router.TryDequeueInformationReport(out _));
    }

    [Fact]
    public void CanonicalByteCapacity_RejectsThirdReportWithoutUnboundedRetention()
    {
        var report = BuildInformationReport(1);
        var router = new MmsReceiveRouter(
            canonicalReportCapacity: 10,
            canonicalReportByteCapacity: 2L * report.Length);
        router.Route(report);
        router.Route(report);

        Assert.Throws<IOException>(() => router.Route(report));
        Assert.Equal(2, router.QueuedInformationReportCount);
        Assert.True(router.TryDequeueInformationReport(out _));
        Assert.True(router.TryDequeueInformationReport(out _));
        Assert.Throws<IOException>(() => router.TryDequeueInformationReport(out _));
    }

    [Fact]
    public async Task CanonicalTerminalFault_WakesWaitingMonitorWithoutFalseTimeout()
    {
        var router = new MmsReceiveRouter();
        var waiting = router.WaitForInformationReportAsync(TimeSpan.FromSeconds(2));
        router.FaultCanonicalInformationReports(new IOException("scripted association failure"));

        var failure = await Assert.ThrowsAsync<IOException>(() => waiting);
        Assert.Contains("continuity is unverified", failure.Message);
        Assert.Contains("scripted association failure", failure.InnerException?.Message);
    }

    private static byte[] BuildConfirmedReadResponse(int invokeId)
    {
        var data = MmsDataCodec.Encode(MmsDataValue.Boolean(true));
        var listOfAccessResult = BerWriter.EncodeTlv(0xA1, data);
        var readService = BerWriter.EncodeTlv(0xA4, listOfAccessResult);
        var mms = BerWriter.EncodeTlv(
            0xA1,
            Integer(invokeId)
                .Concat(readService)
                .ToArray());

        return MmsPresentation.WrapIsoPresentationPData(mms);
    }

    private static byte[] BuildInformationReport(int sequence = 0)
    {
        var variableAccessSpecification = BerWriter.EncodeTlv(
            0xA1,
            BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii("LD0"))
                .Concat(BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii(sequence == 0 ? "LLN0$Events" : $"LLN0$Events{sequence:D3}")))
                .ToArray());
        var listOfAccessResult = BerWriter.EncodeTlv(0xA0, MmsDataCodec.Encode(MmsDataValue.Boolean(true)));
        var informationReport = BerWriter.EncodeTlv(
            0xA0,
            variableAccessSpecification
                .Concat(listOfAccessResult)
                .ToArray());
        var mms = BerWriter.EncodeTlv(0xA3, informationReport);

        return MmsPresentation.WrapIsoPresentationPData(mms);
    }

    private static byte[] Integer(int value)
    {
        if (value <= 0x7F)
            return [0x02, 0x01, (byte)value];

        if (value <= 0xFF)
            return [0x02, 0x02, 0x00, (byte)value];

        return [0x02, 0x02, (byte)(value >> 8), (byte)value];
    }
}
