namespace AR.Iec61850.Mms;

/// <summary>
/// Encodes and decodes IEC 61850 RCB bit-string fields from engineer-readable names
/// and from the canonical renderer form, for example <c>bits(08, unused=2)</c>.
/// Bit indexes follow IEC 61850-7-2 ordering (MSB first in the MMS bit-string).
/// </summary>
public static class MmsReportControlFieldCodec
{
    private static readonly IReadOnlyDictionary<string, int> TriggerOptionBits =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["dchg"] = 0,
            ["data-change"] = 0,
            ["datachange"] = 0,
            ["qchg"] = 1,
            ["quality-change"] = 1,
            ["qualitychange"] = 1,
            ["dupd"] = 2,
            ["data-update"] = 2,
            ["dataupdate"] = 2,
            ["integrity"] = 3,
            ["intg"] = 3,
            ["period"] = 3,
            ["gi"] = 4,
            ["general-interrogation"] = 4,
            ["generalinterrogation"] = 4,
            ["application-trigger"] = 5,
            ["applicationtrigger"] = 5
        };

    private static readonly IReadOnlyDictionary<string, int> OptionalFieldBits =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["sequence-number"] = 1,
            ["sequencenumber"] = 1,
            ["seqnum"] = 1,
            ["report-timestamp"] = 2,
            ["reporttimestamp"] = 2,
            ["time-of-entry"] = 2,
            ["timeofentry"] = 2,
            ["timestamp"] = 2,
            ["reason-for-inclusion"] = 3,
            ["reasonforinclusion"] = 3,
            ["reason"] = 3,
            ["data-set"] = 4,
            ["data-set-name"] = 4,
            ["dataset"] = 4,
            ["data-reference"] = 5,
            ["datareference"] = 5,
            ["dataref"] = 5,
            ["buffer-overflow"] = 6,
            ["bufferoverflow"] = 6,
            ["bufovfl"] = 6,
            ["entryid"] = 7,
            ["entry-id"] = 7,
            ["conf-revision"] = 8,
            ["confrevision"] = 8,
            ["confrev"] = 8,
            ["configref"] = 8,
            ["segmentation"] = 9
        };

    public static bool TryEncodeTriggerOptions(string? text, out MmsDataValue value)
        => TryEncode(text, TriggerOptionBits, bitCount: 6, out value);

    public static bool TryEncodeOptionalFields(string? text, out MmsDataValue value)
        => TryEncode(text, OptionalFieldBits, bitCount: 10, out value);

    public static MmsReportTriggerOptionFlags DecodeTriggerOptions(string? text)
    {
        var bits = DecodeBits(text, TriggerOptionBits, bitCount: 6);
        return new MmsReportTriggerOptionFlags
        {
            DataChange = bits[0],
            QualityChange = bits[1],
            DataUpdate = bits[2],
            Integrity = bits[3],
            GeneralInterrogation = bits[4],
            ApplicationTrigger = bits[5]
        };
    }

    public static MmsReportOptionalFieldFlags DecodeOptionalFields(string? text)
    {
        var bits = DecodeBits(text, OptionalFieldBits, bitCount: 10);
        return new MmsReportOptionalFieldFlags
        {
            SequenceNumber = bits[1],
            ReportTimestamp = bits[2],
            ReasonForInclusion = bits[3],
            DataSetName = bits[4],
            DataReference = bits[5],
            BufferOverflow = bits[6],
            EntryId = bits[7],
            ConfigurationRevision = bits[8],
            Segmentation = bits[9]
        };
    }

    private static bool TryEncode(
        string? text,
        IReadOnlyDictionary<string, int> map,
        int bitCount,
        out MmsDataValue value)
    {
        value = MmsDataValue.BitString((byte)((8 - bitCount % 8) % 8), ReadOnlySpan<byte>.Empty);
        var bits = DecodeBits(text, map, bitCount)
            .Select((enabled, index) => (enabled, index))
            .Where(item => item.enabled)
            .Select(item => item.index)
            .ToArray();
        if (bits.Length == 0)
            return false;

        var bytes = new byte[(bitCount + 7) / 8];
        foreach (var bit in bits)
            bytes[bit / 8] |= (byte)(0x80 >> (bit % 8));

        var unusedBits = checked((byte)(bytes.Length * 8 - bitCount));
        value = MmsDataValue.BitString(unusedBits, bytes);
        return true;
    }

    private static bool[] DecodeBits(
        string? text,
        IReadOnlyDictionary<string, int> map,
        int bitCount)
    {
        var enabled = new bool[bitCount];
        foreach (var token in Tokenize(text))
        {
            if (map.TryGetValue(token, out var bit) && bit >= 0 && bit < bitCount)
                enabled[bit] = true;
        }

        if (TryParseRenderedBitString(text, out var bytes))
        {
            for (var bit = 0; bit < bitCount; bit++)
            {
                var byteIndex = bit / 8;
                if (byteIndex < bytes.Length && (bytes[byteIndex] & (0x80 >> (bit % 8))) != 0)
                    enabled[bit] = true;
            }
        }

        return enabled;
    }

    private static bool TryParseRenderedBitString(string? text, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        var source = text ?? string.Empty;
        var marker = source.IndexOf("bits(", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return false;

        var valueStart = marker + 5;
        var valueEnd = source.IndexOfAny([',', ')'], valueStart);
        if (valueEnd <= valueStart)
            return false;

        var hex = source[valueStart..valueEnd]
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (hex.Length == 0 || (hex.Length & 1) != 0 || !hex.All(Uri.IsHexDigit))
            return false;

        try
        {
            bytes = Convert.FromHexString(hex);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IEnumerable<string> Tokenize(string? text)
        => (text ?? string.Empty)
            .Split(new[] { ' ', ',', ';', '|', '+', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim().Trim('[', ']', '(', ')').ToLowerInvariant());
}

public sealed class MmsReportTriggerOptionFlags
{
    public bool DataChange { get; init; }
    public bool QualityChange { get; init; }
    public bool DataUpdate { get; init; }
    public bool Integrity { get; init; }
    public bool GeneralInterrogation { get; init; }
    public bool ApplicationTrigger { get; init; }
}

public sealed class MmsReportOptionalFieldFlags
{
    public bool SequenceNumber { get; init; }
    public bool ReportTimestamp { get; init; }
    public bool ReasonForInclusion { get; init; }
    public bool DataSetName { get; init; }
    public bool DataReference { get; init; }
    public bool BufferOverflow { get; init; }
    public bool EntryId { get; init; }
    public bool ConfigurationRevision { get; init; }
    public bool Segmentation { get; init; }
}