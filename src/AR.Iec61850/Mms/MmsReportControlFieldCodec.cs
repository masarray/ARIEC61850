namespace AR.Iec61850.Mms;

/// <summary>
/// Encodes and decodes IEC 61850 RCB bit-string fields from engineer-readable names,
/// the canonical renderer form (for example <c>bits(04, unused=2)</c>), and the
/// exact MMS display form emitted by <see cref="MmsDataCodec.ToDisplayString"/>
/// (for example <c>0204</c>, where the first octet is the BER bit-string unused-bit count).
/// Bit indexes follow IEC 61850-7-2 / IEC 61850-8-1 MMS mapping order (MSB first).
///
/// TrgOps is a six-bit MMS BIT STRING whose bit 0 is reserved. The five standard
/// trigger options therefore occupy bits 1..5: dchg, qchg, dupd, integrity, GI.
/// OptFlds is a ten-bit MMS BIT STRING whose bit 0 is also reserved.
/// </summary>
public static class MmsReportControlFieldCodec
{
    private const int TriggerOptionBitCount = 6;
    private const int OptionalFieldBitCount = 10;

    private static readonly IReadOnlyDictionary<string, int> TriggerOptionBits =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["dchg"] = 1,
            ["data-change"] = 1,
            ["datachange"] = 1,
            ["qchg"] = 2,
            ["quality-change"] = 2,
            ["qualitychange"] = 2,
            ["dupd"] = 3,
            ["data-update"] = 3,
            ["dataupdate"] = 3,
            ["integrity"] = 4,
            ["intg"] = 4,
            ["period"] = 4,
            ["gi"] = 5,
            ["general-interrogation"] = 5,
            ["generalinterrogation"] = 5
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
        => TryEncode(text, TriggerOptionBits, TriggerOptionBitCount, out value);

    public static bool TryEncodeOptionalFields(string? text, out MmsDataValue value)
        => TryEncode(text, OptionalFieldBits, OptionalFieldBitCount, out value);

    public static MmsReportTriggerOptionFlags DecodeTriggerOptions(string? text)
    {
        var bits = DecodeBits(text, TriggerOptionBits, TriggerOptionBitCount);
        return new MmsReportTriggerOptionFlags
        {
            Reserved = bits[0],
            DataChange = bits[1],
            QualityChange = bits[2],
            DataUpdate = bits[3],
            Integrity = bits[4],
            GeneralInterrogation = bits[5],
            // Kept for source/API compatibility with the earlier incorrect mapping.
            // IEC 61850 TrgOps bit 0 is reserved; it is not an application-trigger flag.
            ApplicationTrigger = false
        };
    }

    public static MmsReportOptionalFieldFlags DecodeOptionalFields(string? text)
    {
        var bits = DecodeBits(text, OptionalFieldBits, OptionalFieldBitCount);
        return new MmsReportOptionalFieldFlags
        {
            Reserved = bits[0],
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

    /// <summary>
    /// Compares the six significant TrgOps bits while retaining raw BER evidence.
    /// Trailing padding bits declared unused by the BIT STRING are not part of the
    /// IEC value and therefore cannot make semantic restore/readback fail.
    /// </summary>
    public static MmsReportBitStringComparison CompareTriggerOptions(
        MmsDataValue expected,
        MmsDataValue actual)
        => CompareBitString(expected, actual, TriggerOptionBitCount);

    /// <summary>
    /// Compares the ten significant OptFlds bits while retaining raw BER evidence.
    /// </summary>
    public static MmsReportBitStringComparison CompareOptionalFields(
        MmsDataValue expected,
        MmsDataValue actual)
        => CompareBitString(expected, actual, OptionalFieldBitCount);

    private static bool TryEncode(
        string? text,
        IReadOnlyDictionary<string, int> map,
        int bitCount,
        out MmsDataValue value)
    {
        value = MmsDataValue.BitString((byte)((8 - bitCount % 8) % 8), ReadOnlySpan<byte>.Empty);
        var bits = DecodeBits(text, map, bitCount)
            .Select((enabled, index) => (enabled, index))
            .Where(item => item.enabled && item.index != 0)
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

        if (TryParseRenderedBitString(text, out var bytes) ||
            TryParseExactMmsDisplayBitString(text, bitCount, out bytes))
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

    private static MmsReportBitStringComparison CompareBitString(
        MmsDataValue expected,
        MmsDataValue actual,
        int bitCount)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var expectedRaw = expected.RawValue.ToArray();
        var actualRaw = actual.RawValue.ToArray();
        var rawExact = expected.Kind == actual.Kind && expectedRaw.AsSpan().SequenceEqual(actualRaw);

        var payloadBytes = (bitCount + 7) / 8;
        var unusedBits = payloadBytes * 8 - bitCount;
        var expectedShape = expected.Kind == MmsDataKind.BitString &&
                            expectedRaw.Length == payloadBytes + 1 &&
                            expectedRaw[0] == unusedBits;
        var actualShape = actual.Kind == MmsDataKind.BitString &&
                          actualRaw.Length == payloadBytes + 1 &&
                          actualRaw[0] == unusedBits;

        if (!expectedShape || !actualShape)
        {
            return new MmsReportBitStringComparison
            {
                IsComparable = false,
                IsRawExact = rawExact,
                IsSemanticMatch = false,
                PaddingOnlyDifference = false,
                SignificantBitCount = bitCount,
                ExpectedRaw = expectedRaw,
                ActualRaw = actualRaw
            };
        }

        var semanticMatch = true;
        for (var index = 0; index < payloadBytes; index++)
        {
            byte mask = 0xFF;
            if (index == payloadBytes - 1 && unusedBits > 0)
                mask = (byte)(0xFF << unusedBits);

            if ((expectedRaw[index + 1] & mask) != (actualRaw[index + 1] & mask))
            {
                semanticMatch = false;
                break;
            }
        }

        return new MmsReportBitStringComparison
        {
            IsComparable = true,
            IsRawExact = rawExact,
            IsSemanticMatch = semanticMatch,
            PaddingOnlyDifference = semanticMatch && !rawExact,
            SignificantBitCount = bitCount,
            ExpectedRaw = expectedRaw,
            ActualRaw = actualRaw
        };
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

    /// <summary>
    /// Parses the exact display form produced by MmsDataCodec for a BitString.
    /// The first octet is the BER unused-bit count and the remaining octets are
    /// the bit-string payload. This is intentionally strict so an arbitrary
    /// decimal-looking token is never reinterpreted as report-control flags.
    /// </summary>
    private static bool TryParseExactMmsDisplayBitString(string? text, int bitCount, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        var source = (text ?? string.Empty)
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();

        var expectedPayloadBytes = (bitCount + 7) / 8;
        var expectedHexLength = (expectedPayloadBytes + 1) * 2;
        if (source.Length != expectedHexLength || (source.Length & 1) != 0 || !source.All(Uri.IsHexDigit))
            return false;

        try
        {
            var raw = Convert.FromHexString(source);
            if (raw.Length != expectedPayloadBytes + 1)
                return false;

            var expectedUnusedBits = checked((byte)(expectedPayloadBytes * 8 - bitCount));
            if (raw[0] != expectedUnusedBits)
                return false;

            bytes = raw[1..];
            return bytes.Length == expectedPayloadBytes;
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

public sealed class MmsReportBitStringComparison
{
    public bool IsComparable { get; init; }
    public bool IsRawExact { get; init; }
    public bool IsSemanticMatch { get; init; }
    public bool PaddingOnlyDifference { get; init; }
    public int SignificantBitCount { get; init; }
    public IReadOnlyList<byte> ExpectedRaw { get; init; } = Array.Empty<byte>();
    public IReadOnlyList<byte> ActualRaw { get; init; } = Array.Empty<byte>();

    public string ExpectedHex => Convert.ToHexString(ExpectedRaw.ToArray());
    public string ActualHex => Convert.ToHexString(ActualRaw.ToArray());
}

public sealed class MmsReportTriggerOptionFlags
{
    public bool Reserved { get; init; }
    public bool DataChange { get; init; }
    public bool QualityChange { get; init; }
    public bool DataUpdate { get; init; }
    public bool Integrity { get; init; }
    public bool GeneralInterrogation { get; init; }
    public bool ApplicationTrigger { get; init; }
}

public sealed class MmsReportOptionalFieldFlags
{
    public bool Reserved { get; init; }
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
