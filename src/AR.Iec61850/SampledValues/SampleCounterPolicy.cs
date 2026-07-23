namespace AR.Iec61850.SampledValues;

public enum SampleCounterMode
{
    FreeRun,
    SecondAligned
}

public static class SampleCounterPolicy
{
    public static ushort InitialSampleCount(DateTimeOffset timestamp, double sampleRateHz, ushort? wrap, SampleCounterMode mode)
    {
        if (mode == SampleCounterMode.FreeRun || sampleRateHz <= 0)
            return 0;

        var samplesPerSecond = wrap is > 1 ? wrap.Value : sampleRateHz;
        if (samplesPerSecond <= 0)
            return 0;

        var seconds = timestamp.TimeOfDay.TotalSeconds;
        var fraction = seconds - Math.Floor(seconds);
        var sample = (long)Math.Floor(fraction * samplesPerSecond);
        var modulo = wrap is > 1 ? wrap.Value : ushort.MaxValue + 1L;
        sample %= modulo;
        if (sample < 0)
            sample += modulo;
        return (ushort)sample;
    }

    public static ushort Increment(ushort current, ushort? wrap, int step = 1)
    {
        if (step <= 0)
            return current;

        var modulo = wrap is > 1 ? wrap.Value : ushort.MaxValue + 1;
        return (ushort)((current + step) % modulo);
    }
}
