using System.Collections.Concurrent;
using AR.Iec61850.Engineering.Runtime;

namespace AR.Iec61850.Tests.Engineering;

public sealed class LatestOnlyWorkerTests
{
    [Fact]
    public async Task Burst_Coalesces_Pending_Items_And_Delivers_Latest()
    {
        var processed = new ConcurrentQueue<int>();
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var worker = new LatestOnlyWorker<int>(async (value, cancellationToken) =>
        {
            processed.Enqueue(value);
            if (value == 1)
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
        });

        Assert.True(worker.TryPublish(1));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(worker.TryPublish(2));
        Assert.True(worker.TryPublish(3));
        Assert.True(worker.TryPublish(4));

        var whileBlocked = worker.GetStatistics();
        Assert.Equal(4, whileBlocked.Published);
        Assert.Equal(2, whileBlocked.Coalesced);
        Assert.True(whileBlocked.HasPending);

        releaseFirst.TrySetResult(true);
        await worker.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 1, 4 }, processed.ToArray());
        var completed = worker.GetStatistics();
        Assert.Equal(2, completed.Processed);
        Assert.Equal(2, completed.Coalesced);
        Assert.Equal(0, completed.Faulted);
        Assert.Equal(0, completed.TimedOut);
        Assert.False(completed.IsAccepting);
        Assert.False(completed.HasPending);
    }

    [Fact]
    public async Task Handler_Fault_Is_Contained_And_Does_Not_Kill_Worker()
    {
        var firstAttempted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondProcessed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var faultCallbacks = 0;

        await using var worker = new LatestOnlyWorker<int>(
            async (value, _) =>
            {
                if (value == 1)
                {
                    firstAttempted.TrySetResult(true);
                    throw new InvalidOperationException("synthetic worker fault");
                }

                if (value == 2)
                    secondProcessed.TrySetResult(true);

                await ValueTask.CompletedTask;
            },
            _ => Interlocked.Increment(ref faultCallbacks));

        Assert.True(worker.TryPublish(1));
        await firstAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(worker.TryPublish(2));
        await secondProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        var statistics = worker.GetStatistics();
        Assert.Equal(2, statistics.Published);
        Assert.Equal(1, statistics.Processed);
        Assert.Equal(1, statistics.Faulted);
        Assert.Equal(0, statistics.TimedOut);
        Assert.Equal(1, Volatile.Read(ref faultCallbacks));
    }

    [Fact]
    public async Task Cooperative_Handler_Deadline_Is_Contained_And_Worker_Continues()
    {
        var timedOutStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondProcessed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutCallbacks = 0;

        await using var worker = new LatestOnlyWorker<int>(
            async (value, cancellationToken) =>
            {
                if (value == 1)
                {
                    timedOutStarted.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                if (value == 2)
                    secondProcessed.TrySetResult(true);
            },
            TimeSpan.FromMilliseconds(100),
            exception =>
            {
                if (exception is TimeoutException)
                    Interlocked.Increment(ref timeoutCallbacks);
            });

        Assert.True(worker.TryPublish(1));
        await timedOutStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(worker.TryPublish(2));
        await secondProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        var statistics = worker.GetStatistics();
        Assert.Equal(2, statistics.Published);
        Assert.Equal(1, statistics.Processed);
        Assert.Equal(0, statistics.Faulted);
        Assert.Equal(1, statistics.TimedOut);
        Assert.Equal(1, Volatile.Read(ref timeoutCallbacks));
    }

    [Fact]
    public async Task Stop_Rejects_New_Publications_And_Drains_Existing_Latest_Item()
    {
        var processed = new ConcurrentQueue<string>();
        await using var worker = new LatestOnlyWorker<string>((value, _) =>
        {
            processed.Enqueue(value);
            return ValueTask.CompletedTask;
        });

        Assert.True(worker.TryPublish("snapshot-A"));
        await worker.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(worker.TryPublish("snapshot-B"));
        Assert.Contains("snapshot-A", processed);
        Assert.False(worker.GetStatistics().IsAccepting);
    }
}
