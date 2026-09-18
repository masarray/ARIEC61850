using AR.Iec61850.Asn1;

namespace AR.Iec61850.Acse;

/// <summary>
/// Decoded called-side identity from the exact ISO Session/Presentation/ACSE
/// association request bytes sent by the client. This keeps SCL export evidence
/// bound to the wire profile that the IED actually accepted.
/// </summary>
public sealed class AcseAssociationRequestIdentityEvidence
{
    public byte[] CalledPresentationSelector { get; init; } = Array.Empty<byte>();
    public byte[] CalledSessionSelector { get; init; } = Array.Empty<byte>();
    public byte[] CalledTransportSelector { get; init; } = Array.Empty<byte>();
    public uint[] CalledApTitle { get; init; } = Array.Empty<uint>();
    public int? CalledAeQualifier { get; init; }

    public bool HasCalledApTitle => CalledApTitle.Length >= 2;
    public bool HasQualifiedCalledApplicationIdentity
        => HasCalledApTitle && CalledAeQualifier is >= 0 and <= ushort.MaxValue;

    public string CalledApTitleText
        => HasCalledApTitle ? string.Join(",", CalledApTitle) : string.Empty;
}

/// <summary>
/// Pure decoder for the called-side selectors/AP-title/AE-qualifier embedded in an
/// IEC 61850 MMS association request. No network state or profile-name knowledge is
/// used here: evidence is derived only from the exact request bytes plus the COTP
/// destination TSAP that carried them.
/// </summary>
public static class AcseAssociationRequestIdentityReader
{
    public static AcseAssociationRequestIdentityEvidence Read(
        ReadOnlyMemory<byte> associationRequest,
        byte[]? calledTransportSelector = null)
    {
        var presentationSelector = Array.Empty<byte>();
        var sessionSelector = Array.Empty<byte>();
        var apTitle = Array.Empty<uint>();
        int? aeQualifier = null;

        if (TryReadSessionConnect(
                associationRequest,
                out var parsedSessionSelector,
                out var presentationPayload))
        {
            sessionSelector = parsedSessionSelector;

            try
            {
                if (TryReadPresentationAssociation(
                        presentationPayload,
                        out var parsedPresentationSelector,
                        out var parsedApTitle,
                        out var parsedAeQualifier))
                {
                    presentationSelector = parsedPresentationSelector;
                    apTitle = parsedApTitle;
                    aeQualifier = parsedAeQualifier;
                }
            }
            catch (BerFormatException)
            {
                // Keep any session/COTP evidence already decoded. A safe-connection
                // canonical export will fail closed if AP-title/AE evidence is absent.
            }
        }

        return new AcseAssociationRequestIdentityEvidence
        {
            CalledPresentationSelector = presentationSelector,
            CalledSessionSelector = sessionSelector,
            CalledTransportSelector = calledTransportSelector?.ToArray() ?? Array.Empty<byte>(),
            CalledApTitle = apTitle,
            CalledAeQualifier = aeQualifier
        };
    }

    private static bool TryReadSessionConnect(
        ReadOnlyMemory<byte> request,
        out byte[] calledSessionSelector,
        out ReadOnlyMemory<byte> presentationPayload)
    {
        calledSessionSelector = Array.Empty<byte>();
        presentationPayload = ReadOnlyMemory<byte>.Empty;

        var span = request.Span;
        if (span.Length < 3 || span[0] != 0x0D)
            return false;

        var offset = 1;
        if (!TryReadSessionLength(span, ref offset, out var declaredLength))
            return false;
        if (declaredLength < 0 || offset + declaredLength != span.Length)
            return false;

        var end = offset + declaredLength;
        while (offset < end)
        {
            var code = span[offset++];
            if (!TryReadSessionLength(span[..end], ref offset, out var length))
                return false;
            if (length < 0 || offset + length > end)
                return false;

            if (code == 0x34)
                calledSessionSelector = request.Slice(offset, length).ToArray();
            else if (code == 0xC1)
                presentationPayload = request.Slice(offset, length);

            offset += length;
        }

        return !presentationPayload.IsEmpty;
    }

    private static bool TryReadPresentationAssociation(
        ReadOnlyMemory<byte> presentationPayload,
        out byte[] calledPresentationSelector,
        out uint[] calledApTitle,
        out int? calledAeQualifier)
    {
        calledPresentationSelector = Array.Empty<byte>();
        calledApTitle = Array.Empty<uint>();
        calledAeQualifier = null;

        var rootOffset = 0;
        if (!BerReader.TryReadTlv(presentationPayload, ref rootOffset, out var cpPpdu) ||
            cpPpdu.EncodedTag != 0x31 ||
            rootOffset != presentationPayload.Length)
        {
            return false;
        }

        if (!TryFindDirectChild(cpPpdu.Value, 0xA2, out var normalMode))
            return false;

        if (TryFindDirectChild(normalMode.Value, 0x82, out var calledPsel))
            calledPresentationSelector = calledPsel.Value.ToArray();

        if (!TryFindDirectChild(normalMode.Value, 0x61, out var fullyEncodedData) ||
            !TryFindDirectChild(fullyEncodedData.Value, 0x30, out var pdvList) ||
            !TryFindDirectChild(pdvList.Value, 0xA0, out var singleAsn1Type) ||
            !TryFindDirectChild(singleAsn1Type.Value, 0x60, out var aarq))
        {
            return false;
        }

        if (TryFindDirectChild(aarq.Value, 0xA2, out var calledApWrapper) &&
            TryFindDirectChild(calledApWrapper.Value, 0x06, out var oid) &&
            TryDecodeObjectIdentifier(oid.Value.Span, out var parsedApTitle))
        {
            calledApTitle = parsedApTitle;
        }

        if (TryFindDirectChild(aarq.Value, 0xA3, out var calledAeWrapper) &&
            TryFindDirectChild(calledAeWrapper.Value, 0x02, out var integer))
        {
            var parsed = BerReader.ReadSignedInteger(integer);
            if (parsed is >= 0 and <= ushort.MaxValue)
                calledAeQualifier = checked((int)parsed.Value);
        }

        return calledPresentationSelector.Length > 0 ||
               calledApTitle.Length > 0 ||
               calledAeQualifier.HasValue;
    }

    private static bool TryFindDirectChild(
        ReadOnlyMemory<byte> source,
        byte encodedTag,
        out BerTlv child)
    {
        foreach (var candidate in BerReader.ReadChildren(source))
        {
            if (candidate.EncodedTag == encodedTag)
            {
                child = candidate;
                return true;
            }
        }

        child = default;
        return false;
    }

    private static bool TryReadSessionLength(
        ReadOnlySpan<byte> source,
        ref int offset,
        out int length)
    {
        length = 0;
        if (offset < 0 || offset >= source.Length)
            return false;

        var first = source[offset++];
        if (first != 0xFF)
        {
            length = first;
            return true;
        }

        if (offset + 2 > source.Length)
            return false;

        length = (source[offset] << 8) | source[offset + 1];
        offset += 2;
        return true;
    }

    private static bool TryDecodeObjectIdentifier(
        ReadOnlySpan<byte> encoded,
        out uint[] arcs)
    {
        arcs = Array.Empty<uint>();
        if (encoded.IsEmpty)
            return false;

        var offset = 0;
        if (!TryReadBase128(encoded, ref offset, out var firstSubIdentifier))
            return false;

        var decoded = new List<uint>();
        if (firstSubIdentifier < 40)
        {
            decoded.Add(0);
            decoded.Add(checked((uint)firstSubIdentifier));
        }
        else if (firstSubIdentifier < 80)
        {
            decoded.Add(1);
            decoded.Add(checked((uint)(firstSubIdentifier - 40)));
        }
        else
        {
            decoded.Add(2);
            var second = firstSubIdentifier - 80;
            if (second > uint.MaxValue)
                return false;
            decoded.Add((uint)second);
        }

        while (offset < encoded.Length)
        {
            if (!TryReadBase128(encoded, ref offset, out var value) || value > uint.MaxValue)
                return false;
            decoded.Add((uint)value);
        }

        arcs = decoded.ToArray();
        return arcs.Length >= 2;
    }

    private static bool TryReadBase128(
        ReadOnlySpan<byte> encoded,
        ref int offset,
        out ulong value)
    {
        value = 0;
        var count = 0;

        while (offset < encoded.Length)
        {
            var current = encoded[offset++];
            if (value > (ulong.MaxValue >> 7))
                return false;

            value = (value << 7) | (uint)(current & 0x7F);
            count++;
            if ((current & 0x80) == 0)
                return count <= 10;
            if (count >= 10)
                return false;
        }

        return false;
    }
}
