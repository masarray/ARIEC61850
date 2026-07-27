using System.Buffers.Binary;

namespace AR.Iec61850.SampledValues.Analysis;

/// <summary>
/// Describes only the structural position of a 32-bit word in seqOfData.
/// These roles do not assert channel semantics, engineering units, or IEC 61850 quality meaning.
/// </summary>
public enum SvGenericPayloadWordRole
{
    StandaloneWord,
    FirstWordInEightByteGroup,
    SecondWordInEightByteGroup
}

/// <summary>
/// One four-byte big-endian word from an SV sample payload, exposed through multiple numeric views.
/// The views are representations of the same bytes and are not automatic semantic interpretations.
/// </summary>
public sealed record SvGenericPayloadWord
{
    public int Index { get; init; }
    public int ByteOffset { get; init; }
    public SvGenericPayloadWordRole StructuralRole { get; init; }
    public byte[] RawBytes { get; init; } = [];
    public string Hex { get; init; } = string.Empty;
    public int SignedInt32 { get; init; }
    public uint UnsignedInt32 { get; init; }
    public float Float32 { get; init; }
    public bool IsFiniteFloat32 => float.IsFinite(Float32);

    public string GenericLabel => $"Word {Index + 1}";
    public string OffsetLabel => $"+0x{ByteOffset:X2}";
}

/// <summary>
/// Vendor-neutral structural inspection of one ASDU seqOfData payload.
/// It deliberately preserves unknown semantics instead of inventing current/voltage channels.
/// </summary>
public sealed record SvGenericPayloadInspection
{
    public int PayloadLength { get; init; }
    public int CompleteWordCount { get; init; }
    public int TrailingByteCount { get; init; }
    public bool IsFourByteAligned { get; init; }
    public bool HasEightByteGroupShape { get; init; }
    public IReadOnlyList<SvGenericPayloadWord> Words { get; init; }
        = Array.Empty<SvGenericPayloadWord>();
    public byte[] TrailingBytes { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; }
        = Array.Empty<string>();

    public string Summary
    {
        get
        {
            if (PayloadLength == 0)
                return "Empty seqOfData payload";

            var grouping = HasEightByteGroupShape
                ? $"{PayloadLength / 8} structural 8-byte group(s)"
                : $"{CompleteWordCount} complete 32-bit word(s)";
            return TrailingByteCount == 0
                ? $"Raw generic inspection · {PayloadLength} bytes · {grouping}"
                : $"Raw generic inspection · {PayloadLength} bytes · {grouping} · {TrailingByteCount} trailing byte(s)";
        }
    }
}

/// <summary>
/// Generic seqOfData inspector used when no trusted dataset layout is available.
/// It never labels words as phase channels, voltage, current, quality, primary, secondary, A, or V.
/// </summary>
public static class SvGenericPayloadInspector
{
    private const int WordBytes = 4;
    private const int StructuralGroupBytes = 8;

    public static SvGenericPayloadInspection Inspect(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        var completeWordCount = span.Length / WordBytes;
        var trailingByteCount = span.Length % WordBytes;
        var hasEightByteGroupShape = span.Length >= StructuralGroupBytes && span.Length % StructuralGroupBytes == 0;
        var words = new SvGenericPayloadWord[completeWordCount];

        for (var index = 0; index < completeWordCount; index++)
        {
            var byteOffset = index * WordBytes;
            var wordBytes = span.Slice(byteOffset, WordBytes);
            var unsigned = BinaryPrimitives.ReadUInt32BigEndian(wordBytes);
            words[index] = new SvGenericPayloadWord
            {
                Index = index,
                ByteOffset = byteOffset,
                StructuralRole = ResolveRole(index, hasEightByteGroupShape),
                RawBytes = wordBytes.ToArray(),
                Hex = Convert.ToHexString(wordBytes),
                SignedInt32 = unchecked((int)unsigned),
                UnsignedInt32 = unsigned,
                Float32 = BitConverter.Int32BitsToSingle(unchecked((int)unsigned))
            };
        }

        var trailingBytes = trailingByteCount == 0
            ? Array.Empty<byte>()
            : span[^trailingByteCount..].ToArray();
        var diagnostics = BuildDiagnostics(span.Length, completeWordCount, trailingByteCount, hasEightByteGroupShape);

        return new SvGenericPayloadInspection
        {
            PayloadLength = span.Length,
            CompleteWordCount = completeWordCount,
            TrailingByteCount = trailingByteCount,
            IsFourByteAligned = trailingByteCount == 0,
            HasEightByteGroupShape = hasEightByteGroupShape,
            Words = words,
            TrailingBytes = trailingBytes,
            Diagnostics = diagnostics
        };
    }

    private static SvGenericPayloadWordRole ResolveRole(int wordIndex, bool hasEightByteGroupShape)
    {
        if (!hasEightByteGroupShape)
            return SvGenericPayloadWordRole.StandaloneWord;
        return wordIndex % 2 == 0
            ? SvGenericPayloadWordRole.FirstWordInEightByteGroup
            : SvGenericPayloadWordRole.SecondWordInEightByteGroup;
    }

    private static IReadOnlyList<string> BuildDiagnostics(
        int payloadLength,
        int completeWordCount,
        int trailingByteCount,
        bool hasEightByteGroupShape)
    {
        var diagnostics = new List<string>();
        if (payloadLength == 0)
        {
            diagnostics.Add("seqOfData is empty.");
            return diagnostics;
        }

        diagnostics.Add(
            $"Generic inspection exposed {completeWordCount} complete big-endian 32-bit word(s) without assigning channel names or engineering units.");

        if (hasEightByteGroupShape)
        {
            diagnostics.Add(
                "The payload has an 8-byte grouping shape. This is structural evidence only; the second word is not treated as IEC 61850 quality until SCL or an explicit standard layout resolves it.");
        }

        if (trailingByteCount > 0)
        {
            diagnostics.Add(
                $"The payload contains {trailingByteCount} trailing byte(s) after the last complete 32-bit word; those bytes are preserved verbatim.");
        }

        return diagnostics;
    }
}
