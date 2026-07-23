using System.Diagnostics;

namespace AR.Iec61850.TimeSync.Health;

public enum TxTimingHealthStatus
{
    Idle,
    Good,
    Warning,
    Bad
}

/// <summary>
/// Immutable transmitter timing evidence. Values describe the local process that attempted
/// to send frames; they do not prove deterministic wire timing or remote reception.
/// </summary>
public sealed record TxTimingHealthSnapshot
{
    public TxTimingHealthStatus Status { get; init; } = TxTimingHealthStatus.Idle;
    public double TargetFramesPerSecond { get; init; }
    public double ActualFramesPerSecond { get; init; }
    public long FrameCount { get; init; }
    public double AverageAbsJitterMicroseconds { get; init; }
    public double MaxAbsJitterMicroseconds { get; init; }
    public long LateFrameCount { get; init; }
    public long MissedScheduleCount { get; init; }
    public double AverageSendDurationMicroseconds { get; init; }
    public double MaxSendDurationMicroseconds { get; init; }
    public double MaxLateByMicroseconds { get; init; }
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// Thread-safe local scheduling and send-duration monitor for a best-effort publisher.
/// The monitor uses Stopwatch ticks supplied by the caller and keeps cumulative evidence.
/// </summary>
public sealed class TxTimingHealth
{
    private readonly object _gate = new();
    private readonly double _targetFramesPerSecond;
    private readonly double _intervalMicroseconds;
    private long? _firstScheduledTicks;
    private long _frameCount;
    private double _sumAbsJitterMicroseconds;
    private double _maxAbsJitterMicroseconds;
    private long _lateFrameCount;
    private long _missedScheduleCount;
    private double _sumSendDurationMicroseconds;
    private double _maxSendDurationMicroseconds;
    private double _maxLateByMicroseconds;

    public TxTimingHealth(double targetFramesPerSecond)
    {
        if (!double.IsFinite(targetFramesPerSecond) || targetFramesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetFramesPerSecond), "Target frame rate must be positive and finite.");

        _targetFramesPerSecond = targetFramesPerSecond;
        _intervalMicroseconds = 1_000_000.0 / targetFramesPerSecond;
    }

    public void Record(long scheduledTicks, long sendStartTicks, long sendEndTicks)
    {
        if (sendEndTicks < sendStartTicks)
            throw new ArgumentOutOfRangeException(nameof(sendEndTicks), "Send end must not precede send start.");

        var jitterMicroseconds = ToMicroseconds(sendStartTicks - scheduledTicks);
        var absJitterMicroseconds = Math.Abs(jitterMicroseconds);
        var sendDurationMicroseconds = ToMicroseconds(sendEndTicks - sendStartTicks);
        var lateByMicroseconds = Math.Max(0, jitterMicroseconds);

        lock (_gate)
        {
            _firstScheduledTicks ??= scheduledTicks;
            _frameCount++;
            _sumAbsJitterMicroseconds += absJitterMicroseconds;
            _maxAbsJitterMicroseconds = Math.Max(_maxAbsJitterMicroseconds, absJitterMicroseconds);
            _sumSendDurationMicroseconds += sendDurationMicroseconds;
            _maxSendDurationMicroseconds = Math.Max(_maxSendDurationMicroseconds, sendDurationMicroseconds);
            _maxLateByMicroseconds = Math.Max(_maxLateByMicroseconds, lateByMicroseconds);

            if (lateByMicroseconds > _intervalMicroseconds * 0.10)
                _lateFrameCount++;

            if (lateByMicroseconds >= _intervalMicroseconds)
            {
                var missed = (long)Math.Floor(lateByMicroseconds / _intervalMicroseconds);
                _missedScheduleCount += Math.Max(1, missed);
            }
        }
    }

    public TxTimingHealthSnapshot Snapshot(long nowTicks)
    {
        lock (_gate)
        {
            if (_frameCount == 0 || !_firstScheduledTicks.HasValue)
            {
                return new TxTimingHealthSnapshot
                {
                    TargetFramesPerSecond = _targetFramesPerSecond,
                    Detail = "No transmission timing samples have been recorded."
                };
            }

            var elapsedSeconds = Math.Max(
                1.0 / _targetFramesPerSecond,
                (nowTicks - _firstScheduledTicks.Value) / (double)Stopwatch.Frequency);
            var actualFramesPerSecond = _frameCount / elapsedSeconds;
            var averageJitter = _sumAbsJitterMicroseconds / _frameCount;
            var averageSend = _sumSendDurationMicroseconds / _frameCount;
            var status = ResolveStatus(
                elapsedSeconds,
                actualFramesPerSecond,
                averageJitter,
                _maxAbsJitterMicroseconds,
                _lateFrameCount,
                _missedScheduleCount,
                averageSend,
                _maxSendDurationMicroseconds);

            return new TxTimingHealthSnapshot
            {
                Status = status,
                TargetFramesPerSecond = _targetFramesPerSecond,
                ActualFramesPerSecond = actualFramesPerSecond,
                FrameCount = _frameCount,
                AverageAbsJitterMicroseconds = averageJitter,
                MaxAbsJitterMicroseconds = _maxAbsJitterMicroseconds,
                LateFrameCount = _lateFrameCount,
                MissedScheduleCount = _missedScheduleCount,
                AverageSendDurationMicroseconds = averageSend,
                MaxSendDurationMicroseconds = _maxSendDurationMicroseconds,
                MaxLateByMicroseconds = _maxLateByMicroseconds,
                Detail = BuildDetail(status, actualFramesPerSecond)
            };
        }
    }

    private TxTimingHealthStatus ResolveStatus(
        double elapsedSeconds,
        double actualFramesPerSecond,
        double averageJitterMicroseconds,
        double maxJitterMicroseconds,
        long lateFrameCount,
        long missedScheduleCount,
        double averageSendMicroseconds,
        double maxSendMicroseconds)
    {
        var rateRatio = actualFramesPerSecond / _targetFramesPerSecond;
        var hasMatureRateWindow = elapsedSeconds >= Math.Max(0.25, 8.0 / _targetFramesPerSecond);

        if (missedScheduleCount > 0 ||
            maxJitterMicroseconds >= _intervalMicroseconds ||
            maxSendMicroseconds >= _intervalMicroseconds ||
            (hasMatureRateWindow && rateRatio < 0.80))
            return TxTimingHealthStatus.Bad;

        if (lateFrameCount > 0 ||
            averageJitterMicroseconds > _intervalMicroseconds * 0.10 ||
            maxJitterMicroseconds > _intervalMicroseconds * 0.25 ||
            averageSendMicroseconds > _intervalMicroseconds * 0.25 ||
            maxSendMicroseconds > _intervalMicroseconds * 0.50 ||
            (hasMatureRateWindow && rateRatio < 0.95))
            return TxTimingHealthStatus.Warning;

        return TxTimingHealthStatus.Good;
    }

    private string BuildDetail(TxTimingHealthStatus status, double actualFramesPerSecond)
        => status switch
        {
            TxTimingHealthStatus.Good => "Local publisher scheduling and send duration are within the engineering monitor thresholds.",
            TxTimingHealthStatus.Warning => $"Local publisher timing requires review; actual rate is {actualFramesPerSecond:0.###}/{_targetFramesPerSecond:0.###} frame/s.",
            TxTimingHealthStatus.Bad => $"Local publisher timing missed a schedule or exceeded one target frame interval; actual rate is {actualFramesPerSecond:0.###}/{_targetFramesPerSecond:0.###} frame/s.",
            _ => "No transmission timing samples have been recorded."
        };

    private static double ToMicroseconds(long stopwatchTicks)
        => stopwatchTicks * 1_000_000.0 / Stopwatch.Frequency;
}
