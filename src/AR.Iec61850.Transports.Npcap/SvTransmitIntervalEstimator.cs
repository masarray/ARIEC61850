namespace AR.Iec61850.Transports.Npcap;

/// <summary>
/// Learns a stable SV transmit interval without allowing one scheduler-late observation
/// to become the permanent wire rate. The estimator activates only after several
/// mutually consistent intervals have been observed.
/// </summary>
internal sealed class SvTransmitIntervalEstimator
{
    private readonly long _minimumIntervalTicks;
    private readonly long _maximumIntervalTicks;
    private readonly int _requiredConsistentIntervals;
    private long _candidateIntervalTicks;
    private int _candidateCount;

    public SvTransmitIntervalEstimator(
        long minimumIntervalTicks,
        long maximumIntervalTicks,
        int requiredConsistentIntervals = 4)
    {
        if (minimumIntervalTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumIntervalTicks));
        if (maximumIntervalTicks < minimumIntervalTicks)
            throw new ArgumentOutOfRangeException(nameof(maximumIntervalTicks));
        if (requiredConsistentIntervals < 2)
            throw new ArgumentOutOfRangeException(nameof(requiredConsistentIntervals));

        _minimumIntervalTicks = minimumIntervalTicks;
        _maximumIntervalTicks = maximumIntervalTicks;
        _requiredConsistentIntervals = requiredConsistentIntervals;
    }

    public long NominalIntervalTicks { get; private set; }
    public int CandidateCount => _candidateCount;

    public void Observe(long intervalTicks)
    {
        if (intervalTicks < _minimumIntervalTicks || intervalTicks > _maximumIntervalTicks)
        {
            if (NominalIntervalTicks == 0)
                ResetCandidate();
            return;
        }

        if (NominalIntervalTicks > 0)
        {
            var minimumAccepted = NominalIntervalTicks * 3 / 4;
            var maximumAccepted = NominalIntervalTicks * 3 / 2;
            if (intervalTicks >= minimumAccepted && intervalTicks <= maximumAccepted)
            {
                NominalIntervalTicks = (long)Math.Round(
                    (NominalIntervalTicks * 0.9) + (intervalTicks * 0.1));
            }

            return;
        }

        if (_candidateCount == 0)
        {
            _candidateIntervalTicks = intervalTicks;
            _candidateCount = 1;
            return;
        }

        var tolerance = Math.Max(
            _minimumIntervalTicks / 2,
            (long)Math.Round(_candidateIntervalTicks * 0.15));
        if (Math.Abs(intervalTicks - _candidateIntervalTicks) > tolerance)
        {
            _candidateIntervalTicks = intervalTicks;
            _candidateCount = 1;
            return;
        }

        _candidateIntervalTicks = (long)Math.Round(
            ((_candidateIntervalTicks * _candidateCount) + intervalTicks) /
            (double)(_candidateCount + 1));
        _candidateCount++;

        if (_candidateCount >= _requiredConsistentIntervals)
            NominalIntervalTicks = _candidateIntervalTicks;
    }

    private void ResetCandidate()
    {
        _candidateIntervalTicks = 0;
        _candidateCount = 0;
    }
}
