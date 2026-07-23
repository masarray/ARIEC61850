namespace AR.Iec61850.SampledValues.Measurements;

public enum SvSampleCounterTransitionKind
{
    Initial,
    Continuous,
    NormalWrap,
    Gap,
    Duplicate,
    OutOfOrder,
    Restart
}

public sealed record SvSampleCounterTransition
{
    public SvSampleCounterTransitionKind Kind { get; init; }
    public ushort Actual { get; init; }
    public ushort? Previous { get; init; }
    public ushort? Expected { get; init; }
    public ushort? Wrap { get; init; }
    public int MissingSamples { get; init; }
    public string Detail { get; init; } = string.Empty;
    public bool IsAnomaly => Kind is SvSampleCounterTransitionKind.Gap or
        SvSampleCounterTransitionKind.Duplicate or
        SvSampleCounterTransitionKind.OutOfOrder;
}

/// <summary>
/// Stateful, profile-neutral smpCnt tracker. A known modulus is preferred; unknown traffic falls back to the ushort modulus.
/// Explicit restart hints prevent a publisher restart from being misclassified as out-of-order traffic.
/// </summary>
public sealed class SvSampleCounterTracker
{
    private ushort? _last;
    private ushort? _expected;

    public ushort? Last => _last;
    public ushort? Expected => _expected;

    public void Reset()
    {
        _last = null;
        _expected = null;
    }

    public SvSampleCounterTransition Observe(ushort actual, ushort? wrap, bool restartHint = false)
    {
        if (restartHint && _last.HasValue)
        {
            var previous = _last;
            _last = actual;
            _expected = Next(actual, wrap);
            return new SvSampleCounterTransition
            {
                Kind = SvSampleCounterTransitionKind.Restart,
                Actual = actual,
                Previous = previous,
                Expected = null,
                Wrap = wrap,
                Detail = "The counter state was reset by trusted restart/configuration evidence."
            };
        }

        if (!_last.HasValue)
        {
            _last = actual;
            _expected = Next(actual, wrap);
            return new SvSampleCounterTransition
            {
                Kind = SvSampleCounterTransitionKind.Initial,
                Actual = actual,
                Wrap = wrap,
                Detail = "Initial sample counter observation."
            };
        }

        var previousValue = _last.Value;
        if (actual == previousValue)
        {
            return new SvSampleCounterTransition
            {
                Kind = SvSampleCounterTransitionKind.Duplicate,
                Actual = actual,
                Previous = previousValue,
                Expected = _expected,
                Wrap = wrap,
                Detail = $"Duplicate smpCnt {actual}."
            };
        }

        var expectedValue = _expected ?? Next(previousValue, wrap);
        if (actual == expectedValue)
        {
            var wrapped = actual < previousValue;
            _last = actual;
            _expected = Next(actual, wrap);
            return new SvSampleCounterTransition
            {
                Kind = wrapped ? SvSampleCounterTransitionKind.NormalWrap : SvSampleCounterTransitionKind.Continuous,
                Actual = actual,
                Previous = previousValue,
                Expected = expectedValue,
                Wrap = wrap,
                Detail = wrapped ? "Normal smpCnt wrap." : "Continuous smpCnt transition."
            };
        }

        var modulus = ResolveModulus(wrap);
        var forward = DistanceForward(expectedValue, actual, modulus);
        var backward = DistanceForward(actual, expectedValue, modulus);
        var isForwardGap = forward > 0 && forward < backward;

        _last = actual;
        _expected = Next(actual, wrap);

        if (isForwardGap)
        {
            return new SvSampleCounterTransition
            {
                Kind = SvSampleCounterTransitionKind.Gap,
                Actual = actual,
                Previous = previousValue,
                Expected = expectedValue,
                Wrap = wrap,
                MissingSamples = forward,
                Detail = $"smpCnt advanced from expected {expectedValue} to {actual}; {forward} sample(s) were not observed."
            };
        }

        return new SvSampleCounterTransition
        {
            Kind = SvSampleCounterTransitionKind.OutOfOrder,
            Actual = actual,
            Previous = previousValue,
            Expected = expectedValue,
            Wrap = wrap,
            Detail = $"smpCnt {actual} is behind expected {expectedValue}."
        };
    }

    private static ushort Next(ushort value, ushort? wrap)
    {
        if (wrap is > 1)
            return (ushort)((value + 1) % wrap.Value);
        return unchecked((ushort)(value + 1));
    }

    private static int ResolveModulus(ushort? wrap)
        => wrap is > 1 ? wrap.Value : ushort.MaxValue + 1;

    private static int DistanceForward(ushort from, ushort to, int modulus)
    {
        var distance = ((int)to - from) % modulus;
        return distance < 0 ? distance + modulus : distance;
    }
}
