using System.Diagnostics;

namespace AR.Iec61850.SampledValues;

public enum TxTimingHealthStatus
{
    Idle,
    Good,
    Warning,
    Bad
}

public sealed record TxTimingHealthSnapshot(
    TxTimingHealthStatus Status,
    long FrameCount,
    double TargetFramesPerSecond,
    double ActualFramesPerSecond,
    double TargetIntervalMicroseconds,
    double AverageAbsJitterMicroseconds,
    double MaxAbsJitterMicroseconds,
    long LateFrameCount,
    long MissedScheduleCount,
    double AverageSendDurationMicroseconds,
    double MaxSendDurationMicroseconds,
    double MaxLateByMicroseconds)
{
    public string StatusLabel => Status switch
    {
        TxTimingHealthStatus.Good => "GOOD",
        TxTimingHealthStatus.Warning => "WARN",
        TxTimingHealthStatus.Bad => "BAD",
        _ => "IDLE"
    };
}

/// <summary>
/// Measures publisher-side transmit timing only. This class does not capture or analyze network traffic;
/// it records the schedule, send start, and send completion timestamps from the local SV publisher loop.
/// </summary>
public sealed class TxTimingHealth
{
    private readonly long _timestampFrequency;
    private readonly long _targetIntervalTicks;
    private readonly long _lateThresholdTicks;
    private long _frameCount;
    private long _firstSendStartTicks;
    private long _lastSendStartTicks;
    private double _sumAbsJitterTicks;
    private long _maxAbsJitterTicks;
    private long _intervalCount;
    private long _lateFrameCount;
    private long _missedScheduleCount;
    private long _maxLateByTicks;
    private double _sumSendDurationTicks;
    private long _maxSendDurationTicks;

    public TxTimingHealth(double targetFramesPerSecond)
        : this(targetFramesPerSecond, Stopwatch.Frequency)
    {
    }

    public TxTimingHealth(double targetFramesPerSecond, long timestampFrequency)
    {
        if (targetFramesPerSecond <= 0 || double.IsNaN(targetFramesPerSecond) || double.IsInfinity(targetFramesPerSecond))
            throw new ArgumentOutOfRangeException(nameof(targetFramesPerSecond), "Target frame rate must be a positive finite value.");
        if (timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency), "Timestamp frequency must be positive.");

        TargetFramesPerSecond = targetFramesPerSecond;
        _timestampFrequency = timestampFrequency;
        _targetIntervalTicks = Math.Max(1, (long)Math.Round(timestampFrequency / targetFramesPerSecond));
        _lateThresholdTicks = Math.Max(MicrosecondsToTicks(25), _targetIntervalTicks / 10);
    }

    public double TargetFramesPerSecond { get; }
    public double TargetIntervalMicroseconds => TicksToMicroseconds(_targetIntervalTicks);
    public long FrameCount => _frameCount;

    public void Record(long scheduledTicks, long sendStartTicks, long sendEndTicks)
    {
        if (sendEndTicks < sendStartTicks)
            sendEndTicks = sendStartTicks;

        if (_frameCount == 0)
        {
            _firstSendStartTicks = sendStartTicks;
        }
        else
        {
            var actualIntervalTicks = Math.Max(0, sendStartTicks - _lastSendStartTicks);
            var jitterTicks = Math.Abs(actualIntervalTicks - _targetIntervalTicks);
            _sumAbsJitterTicks += jitterTicks;
            _maxAbsJitterTicks = Math.Max(_maxAbsJitterTicks, jitterTicks);
            _intervalCount++;
        }

        var lateByTicks = sendStartTicks - scheduledTicks;
        if (lateByTicks > 0)
        {
            _maxLateByTicks = Math.Max(_maxLateByTicks, lateByTicks);
            if (lateByTicks > _lateThresholdTicks)
                _lateFrameCount++;
            if (lateByTicks > _targetIntervalTicks)
                _missedScheduleCount += Math.Max(1, lateByTicks / _targetIntervalTicks);
        }

        var sendDurationTicks = Math.Max(0, sendEndTicks - sendStartTicks);
        _sumSendDurationTicks += sendDurationTicks;
        _maxSendDurationTicks = Math.Max(_maxSendDurationTicks, sendDurationTicks);
        _lastSendStartTicks = sendStartTicks;
        _frameCount++;
    }

    public TxTimingHealthSnapshot Snapshot(long nowTicks)
    {
        if (_frameCount == 0)
        {
            return new TxTimingHealthSnapshot(
                TxTimingHealthStatus.Idle,
                0,
                TargetFramesPerSecond,
                0,
                TargetIntervalMicroseconds,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        var elapsedTicks = Math.Max(1, nowTicks - _firstSendStartTicks);
        var elapsedSeconds = elapsedTicks / (double)_timestampFrequency;
        var actualFramesPerSecond = _frameCount / Math.Max(elapsedSeconds, 0.000001);
        var averageAbsJitter = _intervalCount == 0 ? 0 : TicksToMicroseconds(_sumAbsJitterTicks / _intervalCount);
        var maxAbsJitter = TicksToMicroseconds(_maxAbsJitterTicks);
        var averageSendDuration = TicksToMicroseconds(_sumSendDurationTicks / _frameCount);
        var maxSendDuration = TicksToMicroseconds(_maxSendDurationTicks);
        var maxLateBy = TicksToMicroseconds(_maxLateByTicks);
        var status = ResolveStatus(actualFramesPerSecond, maxAbsJitter, _lateFrameCount, _missedScheduleCount);

        return new TxTimingHealthSnapshot(
            status,
            _frameCount,
            TargetFramesPerSecond,
            actualFramesPerSecond,
            TargetIntervalMicroseconds,
            averageAbsJitter,
            maxAbsJitter,
            _lateFrameCount,
            _missedScheduleCount,
            averageSendDuration,
            maxSendDuration,
            maxLateBy);
    }

    private TxTimingHealthStatus ResolveStatus(double actualFramesPerSecond, double maxAbsJitterMicroseconds, long lateFrameCount, long missedScheduleCount)
    {
        if (_frameCount < 8)
            return TxTimingHealthStatus.Good;

        var actualRatio = actualFramesPerSecond / TargetFramesPerSecond;
        var targetIntervalUs = TargetIntervalMicroseconds;
        var lateRatio = lateFrameCount / (double)Math.Max(1, _frameCount);

        if (missedScheduleCount > 0 || actualRatio < 0.98 || maxAbsJitterMicroseconds > targetIntervalUs)
            return TxTimingHealthStatus.Bad;

        if (lateRatio > 0.01 || actualRatio < 0.995 || maxAbsJitterMicroseconds > targetIntervalUs * 0.25)
            return TxTimingHealthStatus.Warning;

        return TxTimingHealthStatus.Good;
    }

    private long MicrosecondsToTicks(double microseconds)
        => Math.Max(1, (long)Math.Round(microseconds * _timestampFrequency / 1_000_000.0));

    private double TicksToMicroseconds(double ticks)
        => ticks * 1_000_000.0 / _timestampFrequency;
}
