namespace AR.Iec61850.Goose;

public sealed class GooseRetransmissionSchedule
{
    private readonly int _minTimeMilliseconds;
    private readonly int _maxTimeMilliseconds;
    private int _nextDelayMilliseconds;

    public GooseRetransmissionSchedule(uint minTimeMilliseconds, uint maxTimeMilliseconds)
    {
        _minTimeMilliseconds = NormalizeMinTime(minTimeMilliseconds);
        _maxTimeMilliseconds = NormalizeMaxTime(maxTimeMilliseconds, _minTimeMilliseconds);
        Reset();
    }

    public int MinTimeMilliseconds => _minTimeMilliseconds;
    public int MaxTimeMilliseconds => _maxTimeMilliseconds;

    public int NextDelayMilliseconds()
    {
        var delay = _nextDelayMilliseconds;
        _nextDelayMilliseconds = _nextDelayMilliseconds >= _maxTimeMilliseconds / 2
            ? _maxTimeMilliseconds
            : Math.Min(_maxTimeMilliseconds, _nextDelayMilliseconds * 2);
        return delay;
    }

    public void Reset()
        => _nextDelayMilliseconds = _minTimeMilliseconds;

    private static int NormalizeMinTime(uint value)
        => value == 0 ? 4 : checked((int)Math.Min(value, int.MaxValue));

    private static int NormalizeMaxTime(uint value, int minTimeMilliseconds)
    {
        var maxTime = value == 0 ? 1000 : checked((int)Math.Min(value, int.MaxValue));
        return Math.Max(minTimeMilliseconds, maxTime);
    }
}
