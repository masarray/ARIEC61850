using System.Buffers.Binary;
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

        var samples = LoadData(dataPath, configuration);
        var map = ComtradeChannelMapper.CreateDefaultMap(analogChannels);
        return new ComtradeDataset(configurationPath, dataPath, configuration, samples, map);
    }

    private static IReadOnlyList<ComtradeSample> LoadData(string dataPath, ComtradeConfiguration configuration)
    {
        if (string.Equals(configuration.DataFileType, "ASCII", StringComparison.OrdinalIgnoreCase))
            return LoadAsciiData(dataPath, configuration);

        if (string.Equals(configuration.DataFileType, "BINARY", StringComparison.OrdinalIgnoreCase))
            return LoadBinaryData(dataPath, configuration, BinaryAnalogFormat.Int16);

        if (string.Equals(configuration.DataFileType, "BINARY32", StringComparison.OrdinalIgnoreCase))
            return LoadBinaryData(dataPath, configuration, BinaryAnalogFormat.Int32);

        if (string.Equals(configuration.DataFileType, "FLOAT32", StringComparison.OrdinalIgnoreCase))
            return LoadBinaryData(dataPath, configuration, BinaryAnalogFormat.Float32);

        throw new NotSupportedException($"COMTRADE DAT type '{configuration.DataFileType}' is not supported. Supported DAT types: ASCII, BINARY, BINARY32, FLOAT32.");
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
                values[i] = ScaleAnalog(raw, channel, configuration.DataFileType);
            }

            samples.Add(new ComtradeSample(number, timestampSeconds, values));
        }

        if (samples.Count == 0)
            throw new FormatException("COMTRADE DAT file contains no readable ASCII samples.");

        return samples;
    }

    private static IReadOnlyList<ComtradeSample> LoadBinaryData(
        string dataPath,
        ComtradeConfiguration configuration,
        BinaryAnalogFormat analogFormat)
    {
        var bytes = File.ReadAllBytes(dataPath);
        var samples = new List<ComtradeSample>();
        var sampleRate = configuration.SampleRates.FirstOrDefault(r => r.RateHz > 0)?.RateHz ?? 0;
        var timeMultiplier = configuration.TimeMultiplier == 0 ? 1.0 : configuration.TimeMultiplier;
        var analogWidth = analogFormat switch
        {
            BinaryAnalogFormat.Int16 => 2,
            BinaryAnalogFormat.Int32 => 4,
            BinaryAnalogFormat.Float32 => 4,
            _ => 2
        };
        var digitalWordCount = (configuration.DigitalChannelCount + 15) / 16;
        var recordLength = 8 + (configuration.AnalogChannelCount * analogWidth) + (digitalWordCount * 2);

        if (recordLength <= 8)
            throw new FormatException("COMTRADE binary record layout is invalid.");

        if (bytes.Length < recordLength)
            throw new FormatException("COMTRADE binary DAT file contains no complete sample records.");

        var completeRecords = bytes.Length / recordLength;
        for (var sampleIndex = 0; sampleIndex < completeRecords; sampleIndex++)
        {
            var offset = sampleIndex * recordLength;
            var number = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
            offset += 4;

            var rawTimestamp = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
            offset += 4;

            var timestampSeconds = rawTimestamp >= 0
                ? rawTimestamp * timeMultiplier / 1_000_000.0
                : (sampleRate > 0 ? sampleIndex / sampleRate : sampleIndex);

            if (samples.Count > 0 && timestampSeconds <= samples[^1].TimestampSeconds && sampleRate > 0)
                timestampSeconds = sampleIndex / sampleRate;

            var values = new double[configuration.AnalogChannelCount];
            for (var channelIndex = 0; channelIndex < configuration.AnalogChannelCount; channelIndex++)
            {
                var channel = configuration.AnalogChannels[channelIndex];
                var raw = analogFormat switch
                {
                    BinaryAnalogFormat.Int16 => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2)),
                    BinaryAnalogFormat.Int32 => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4)),
                    BinaryAnalogFormat.Float32 => BitConverter.ToSingle(bytes, offset),
                    _ => 0
                };

                values[channelIndex] = ScaleAnalog(raw, channel, configuration.DataFileType);
                offset += analogWidth;
            }

            // Digital channels are packed in 16-bit status words after analog values.
            // N5.44.4.x replays analog channels only, so digital words are intentionally skipped.
            samples.Add(new ComtradeSample(number > 0 ? number : sampleIndex + 1, timestampSeconds, values));
        }

        if (samples.Count == 0)
            throw new FormatException("COMTRADE binary DAT file contains no readable samples.");

        return samples;
    }

    private static double ScaleAnalog(double raw, ComtradeAnalogChannel channel, string dataFileType)
    {
        // COMTRADE BINARY/FLOAT values are handled through the same engineering scaling pipeline.
        // For typical IEEE C37.111 records this gives engineering_value = a * raw + b.
        return (raw * channel.Multiplier) + channel.Offset;
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

    private enum BinaryAnalogFormat
    {
        Int16,
        Int32,
        Float32
    }
}
