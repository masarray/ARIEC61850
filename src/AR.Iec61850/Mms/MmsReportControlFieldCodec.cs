namespace AR.Iec61850.Mms;

/// <summary>
/// Encodes the IEC 61850 RCB bit-string fields from engineer-readable names.
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
            ["sqnum"] = 1,
            ["report-timestamp"] = 2,
            ["reporttimestamp"] = 2,
            ["time-of-entry"] = 2,
            ["timeofentry"] = 2,
            ["reason-for-inclusion"] = 3,
            ["reasonforinclusion"] = 3,
            ["data-set"] = 4,
            ["dataset"] = 4,
            ["data-reference"] = 5,
            ["datareference"] = 5,
            ["buffer-overflow"] = 6,
            ["bufferoverflow"] = 6,
            ["entryid"] = 7,
            ["entry-id"] = 7,
            ["conf-revision"] = 8,
            ["confrevision"] = 8,
            ["confrev"] = 8,
            ["segmentation"] = 9
        };

    public static bool TryEncodeTriggerOptions(string? text, out MmsDataValue value)
        => TryEncode(text, TriggerOptionBits, bitCount: 6, out value);

    public static bool TryEncodeOptionalFields(string? text, out MmsDataValue value)
        => TryEncode(text, OptionalFieldBits, bitCount: 10, out value);

    private static bool TryEncode(
        string? text,
        IReadOnlyDictionary<string, int> map,
        int bitCount,
        out MmsDataValue value)
    {
        value = MmsDataValue.BitString((byte)((8 - bitCount % 8) % 8), ReadOnlySpan<byte>.Empty);
        var bits = Tokenize(text)
            .Select(token => map.TryGetValue(token, out var bit) ? bit : -1)
            .Where(bit => bit >= 0 && bit < bitCount)
            .Distinct()
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

    private static IEnumerable<string> Tokenize(string? text)
        => (text ?? string.Empty)
            .Split(new[] { ' ', ',', ';', '|', '+', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim().Trim('[', ']', '(', ')').ToLowerInvariant());
}
