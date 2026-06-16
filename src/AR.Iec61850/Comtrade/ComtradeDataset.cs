namespace AR.Iec61850.Comtrade;

public sealed class ComtradeDataset
{
    public ComtradeDataset(
        string configurationPath,
        string dataPath,
        ComtradeConfiguration configuration,
        IReadOnlyList<ComtradeSample> samples,
        IReadOnlyDictionary<string, int> defaultChannelMap)
    {
        ConfigurationPath = configurationPath;
        DataPath = dataPath;
        Configuration = configuration;
        Samples = samples;
        DefaultChannelMap = defaultChannelMap;
    }

    public string ConfigurationPath { get; }
    public string DataPath { get; }
    public ComtradeConfiguration Configuration { get; }
    public IReadOnlyList<ComtradeSample> Samples { get; }
    public IReadOnlyDictionary<string, int> DefaultChannelMap { get; }

    public int SampleCount => Samples.Count;

    public double DurationSeconds => Samples.Count switch
    {
        0 => 0,
        1 => 0,
        _ => Math.Max(0, Samples[^1].TimestampSeconds - Samples[0].TimestampSeconds)
    };

    public double NominalSampleRateHz
    {
        get
        {
            var rate = Configuration.SampleRates.FirstOrDefault(r => r.RateHz > 0)?.RateHz ?? 0;
            if (rate > 0)
                return rate;

            if (Samples.Count < 2 || DurationSeconds <= 0)
                return 0;

            return (Samples.Count - 1) / DurationSeconds;
        }
    }

    public ComtradeSample GetSampleByIndex(long sampleIndex, bool loop)
    {
        if (Samples.Count == 0)
            throw new InvalidOperationException("COMTRADE dataset contains no samples.");

        if (loop)
        {
            var wrapped = (int)(sampleIndex % Samples.Count);
            if (wrapped < 0)
                wrapped += Samples.Count;
            return Samples[wrapped];
        }

        var index = (int)Math.Clamp(sampleIndex, 0, Samples.Count - 1);
        return Samples[index];
    }

    public string Summary
    {
        get
        {
            var rate = NominalSampleRateHz > 0 ? $"{NominalSampleRateHz:0.###} fps" : "sample rate -";
            return $"{Configuration.StationName}  A={Configuration.AnalogChannelCount} D={Configuration.DigitalChannelCount}  samples={SampleCount}  {rate}";
        }
    }
}
