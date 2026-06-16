using System.Globalization;

namespace AR.Iec61850.Comtrade;

public sealed class ComtradeReader
{
    public ComtradeDataset Load(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        if (!File.Exists(configurationPath))
            throw new FileNotFoundException("COMTRADE CFG file was not found.", configurationPath);

        var lines = File.ReadAllLines(configurationPath);
        if (lines.Length < 8)
            throw new FormatException("COMTRADE CFG file is too short.");

        var cursor = 0;
        var first = Split(lines[cursor++]);
        var station = Get(first, 0);
        var device = Get(first, 1);
        var revision = ParseInt(Get(first, 2), 1999);

        var counts = Split(lines[cursor++]);
        var totalChannels = ParseChannelCount(Get(counts, 0));
        var analogCount = ParseTypedChannelCount(Get(counts, 1), 'A');
        var digitalCount = ParseTypedChannelCount(Get(counts, 2), 'D');

        var analogChannels = new List<ComtradeAnalogChannel>(analogCount);
        for (var i = 0; i < analogCount; i++, cursor++)
            analogChannels.Add(ParseAnalogChannel(lines[cursor]));

        cursor += digitalCount;
        if (cursor >= lines.Length)
            throw new FormatException("COMTRADE CFG file ended before nominal frequency.");

        var frequency = ParseDouble(lines[cursor++], 0);
        var rateCount = cursor < lines.Length ? ParseInt(lines[cursor++].Trim(), 0) : 0;
        var sampleRates = new List<ComtradeSampleRate>(Math.Max(1, rateCount));
        for (var i = 0; i < rateCount && cursor < lines.Length; i++, cursor++)
        {
            var parts = Split(lines[cursor]);
            sampleRates.Add(new ComtradeSampleRate(ParseDouble(Get(parts, 0), 0), ParseInt(Get(parts, 1), 0)));
        }

        // Start and trigger timestamps are not required for replay timing; keep parser tolerant.
        if (cursor < lines.Length)
            cursor++;
        if (cursor < lines.Length)
            cursor++;

        var dataType = cursor < lines.Length ? Get(Split(lines[cursor++]), 0).Trim() : "ASCII";
        var timeMultiplier = cursor < lines.Length ? ParseDouble(Get(Split(lines[cursor]), 0), 1.0) : 1.0;

        if (!string.Equals(dataType, "ASCII", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"COMTRADE DAT type '{dataType}' is not supported yet. N5.44.4 supports ASCII DAT first.");

        if (sampleRates.Count == 0 && analogChannels.Count > 0)
            sampleRates.Add(new ComtradeSampleRate(0, 0));

        var configuration = new ComtradeConfiguration(
            station,
            device,
            revision,
            totalChannels,
            analogCount,
            digitalCount,
            frequency,
            analogChannels,
            sampleRates,
            dataType,
            timeMultiplier);

        var dataPath = Path.ChangeExtension(configurationPath, ".dat");
        if (!File.Exists(dataPath))
            dataPath = Path.ChangeExtension(configurationPath, ".DAT");
        if (!File.Exists(dataPath))
            throw new FileNotFoundException("COMTRADE DAT file was not found next to the CFG file.", dataPath);

        var samples = LoadAsciiData(dataPath, configuration);
        var map = ComtradeChannelMapper.CreateDefaultMap(analogChannels);
        return new ComtradeDataset(configurationPath, dataPath, configuration, samples, map);
    }

    private static IReadOnlyList<ComtradeSample> LoadAsciiData(string dataPath, ComtradeConfiguration configuration)
    {
        var samples = new List<ComtradeSample>();
        var sampleRate = configuration.SampleRates.FirstOrDefault(r => r.RateHz > 0)?.RateHz ?? 0;
        var timeMultiplier = configuration.TimeMultiplier == 0 ? 1.0 : configuration.TimeMultiplier;

        foreach (var line in File.ReadLines(dataPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = Split(line);
            if (parts.Length < 2 + configuration.AnalogChannelCount)
                continue;

            var number = ParseInt(Get(parts, 0), samples.Count + 1);
            var rawTimestamp = ParseDouble(Get(parts, 1), double.NaN);
            var timestampSeconds = double.IsNaN(rawTimestamp)
                ? (sampleRate > 0 ? samples.Count / sampleRate : 0)
                : rawTimestamp * timeMultiplier / 1_000_000.0;

            if (samples.Count > 0 && timestampSeconds <= samples[^1].TimestampSeconds && sampleRate > 0)
                timestampSeconds = samples.Count / sampleRate;

            var values = new double[configuration.AnalogChannelCount];
            for (var i = 0; i < configuration.AnalogChannelCount; i++)
            {
                var channel = configuration.AnalogChannels[i];
                var raw = ParseDouble(Get(parts, 2 + i), 0);
                values[i] = (raw * channel.Multiplier) + channel.Offset;
            }

            samples.Add(new ComtradeSample(number, timestampSeconds, values));
        }

        if (samples.Count == 0)
            throw new FormatException("COMTRADE DAT file contains no readable ASCII samples.");

        return samples;
    }

    private static ComtradeAnalogChannel ParseAnalogChannel(string line)
    {
        var parts = Split(line);
        return new ComtradeAnalogChannel(
            ParseInt(Get(parts, 0), 0),
            Get(parts, 1),
            Get(parts, 2),
            Get(parts, 3),
            Get(parts, 4),
            ParseDouble(Get(parts, 5), 1.0),
            ParseDouble(Get(parts, 6), 0.0),
            ParseDouble(Get(parts, 7), 0.0),
            ParseDouble(Get(parts, 8), 0.0),
            ParseDouble(Get(parts, 9), 0.0),
            ParseDouble(Get(parts, 10), 1.0),
            ParseDouble(Get(parts, 11), 1.0),
            Get(parts, 12));
    }

    private static string[] Split(string line)
        => line.Split(',').Select(part => part.Trim().Trim('"')).ToArray();

    private static string Get(IReadOnlyList<string> parts, int index)
        => index >= 0 && index < parts.Count ? parts[index] : string.Empty;

    private static int ParseChannelCount(string value)
    {
        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        return ParseInt(digits, 0);
    }

    private static int ParseTypedChannelCount(string value, char suffix)
    {
        var trimmed = value.Trim();
        if (trimmed.EndsWith(suffix.ToString(), StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^1];
        return ParseInt(trimmed, 0);
    }

    private static int ParseInt(string value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static double ParseDouble(string value, double fallback)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}
