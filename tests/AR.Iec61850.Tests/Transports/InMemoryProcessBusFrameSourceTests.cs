using AR.Iec61850.Transports;

namespace AR.Iec61850.Tests.Transports;

public sealed class InMemoryProcessBusFrameSourceTests
{
    [Fact]
    public async Task CaptureAsync_Replays_Frames_In_Order()
    {
        var first = new ProcessBusCapturedFrame
        {
            Timestamp = new DateTimeOffset(2026, 6, 14, 1, 0, 0, TimeSpan.Zero),
            Frame = new byte[] { 0x01, 0x02 },
            Source = "test0"
        };
        var second = new ProcessBusCapturedFrame
        {
            Timestamp = first.Timestamp.AddMilliseconds(1),
            Frame = new byte[] { 0x03, 0x04, 0x05 },
            Source = "test1"
        };
        var source = new InMemoryProcessBusFrameSource([first, second]);
        var frames = new List<ProcessBusCapturedFrame>();

        await foreach (var frame in source.CaptureAsync(new ProcessBusCaptureOptions()))
            frames.Add(frame);

        Assert.Equal([first, second], frames);
    }

    [Fact]
    public async Task CaptureAsync_Stops_When_Cancelled()
    {
        var source = new InMemoryProcessBusFrameSource([
            new ProcessBusCapturedFrame
            {
                Timestamp = DateTimeOffset.UnixEpoch,
                Frame = new byte[] { 0x01 }
            }
        ]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.CaptureAsync(new ProcessBusCaptureOptions(), cancellation.Token))
            {
            }
        });
    }
}
