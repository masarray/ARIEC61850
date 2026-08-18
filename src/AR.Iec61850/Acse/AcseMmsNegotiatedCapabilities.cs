using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Acse;

/// <summary>
/// Association-level MMS limits and service bits advertised by the server in
/// InitiateResponse. Nullable service flags are intentional: an association can be
/// accepted even when the InitiateResponse detail is not decodable enough to prove a bit.
/// </summary>
public sealed class AcseMmsNegotiatedCapabilities
{
    public static AcseMmsNegotiatedCapabilities Unknown { get; } = new();

    public bool IsDecoded { get; init; }
    public int? MaxMmsPduSize { get; init; }
    public int? MaxOutstandingCalling { get; init; }
    public int? MaxOutstandingCalled { get; init; }
    public int? DataStructureNestingLevel { get; init; }
    public bool? SupportsWrite { get; init; }
    public bool? SupportsDefineNamedVariableList { get; init; }
    public bool? SupportsDeleteNamedVariableList { get; init; }
    public string ServicesSupportedCalledHex { get; init; } = string.Empty;
    public string Diagnostic { get; init; } = "MMS InitiateResponse capabilities were not decoded.";

    public string Summary =>
        IsDecoded
            ? $"MMS negotiated services: write={Format(SupportsWrite)}, defineNVL={Format(SupportsDefineNamedVariableList)}, deleteNVL={Format(SupportsDeleteNamedVariableList)}, maxPdu={MaxMmsPduSize?.ToString() ?? "?"}."
            : Diagnostic;

    private static string Format(bool? value)
        => value.HasValue ? (value.Value ? "yes" : "no") : "unknown";
}

/// <summary>
/// Conservative decoder for the MMS InitiateResponse embedded in the ISO Session /
/// Presentation / ACSE response. Failure to decode is never treated as lack of support.
/// </summary>
public static class AcseMmsNegotiatedCapabilitiesParser
{
    // ISO 9506 ServiceSupportOptions bit indices (zero based).
    private const int WriteServiceBit = 5;
    private const int DefineNamedVariableListServiceBit = 11;
    private const int DeleteNamedVariableListServiceBit = 13;

    public static AcseMmsNegotiatedCapabilities Parse(ReadOnlyMemory<byte> associationResponse)
    {
        if (associationResponse.IsEmpty)
            return AcseMmsNegotiatedCapabilities.Unknown;

        try
        {
            if (!TryFindSessionUserData(associationResponse.Span, out var offset, out var length))
            {
                return new AcseMmsNegotiatedCapabilities
                {
                    Diagnostic = "ISO Session user-data carrying ACSE/MMS InitiateResponse was not found."
                };
            }

            var presentation = associationResponse.Slice(offset, length);
            if (!TryFindConstructedTag(presentation, 0xA9, out var initiateResponse))
            {
                return new AcseMmsNegotiatedCapabilities
                {
                    Diagnostic = "MMS InitiateResponse [9] was not found in the accepted association response."
                };
            }

            var fields = BerReader.ReadChildren(initiateResponse.Value);
            var localDetail = fields.FirstOrDefault(field => field.EncodedTag == 0x80);
            var outstandingCalling = fields.FirstOrDefault(field => field.EncodedTag == 0x81);
            var outstandingCalled = fields.FirstOrDefault(field => field.EncodedTag == 0x82);
            var nesting = fields.FirstOrDefault(field => field.EncodedTag == 0x83);
            var detail = fields.FirstOrDefault(field => field.EncodedTag == 0xA4);

            if (detail.EncodedTag == 0)
            {
                return new AcseMmsNegotiatedCapabilities
                {
                    IsDecoded = true,
                    MaxMmsPduSize = ReadPositiveInt(localDetail),
                    MaxOutstandingCalling = ReadPositiveInt(outstandingCalling),
                    MaxOutstandingCalled = ReadPositiveInt(outstandingCalled),
                    DataStructureNestingLevel = ReadPositiveInt(nesting),
                    Diagnostic = "MMS InitiateResponse limits were decoded, but InitiateResponseDetail [4] was absent. Service support remains unknown."
                };
            }

            var detailFields = BerReader.ReadChildren(detail.Value);
            var services = detailFields.FirstOrDefault(field => field.EncodedTag == 0x82);
            var serviceBytes = DecodeImplicitBitString(services);

            return new AcseMmsNegotiatedCapabilities
            {
                IsDecoded = true,
                MaxMmsPduSize = ReadPositiveInt(localDetail),
                MaxOutstandingCalling = ReadPositiveInt(outstandingCalling),
                MaxOutstandingCalled = ReadPositiveInt(outstandingCalled),
                DataStructureNestingLevel = ReadPositiveInt(nesting),
                SupportsWrite = ReadServiceBit(serviceBytes, WriteServiceBit),
                SupportsDefineNamedVariableList = ReadServiceBit(serviceBytes, DefineNamedVariableListServiceBit),
                SupportsDeleteNamedVariableList = ReadServiceBit(serviceBytes, DeleteNamedVariableListServiceBit),
                ServicesSupportedCalledHex = serviceBytes.Bits.Length == 0
                    ? string.Empty
                    : HexDump.ToCompactString(serviceBytes.Bits, maxBytes: 128),
                Diagnostic = serviceBytes.Bits.Length == 0
                    ? "MMS InitiateResponse was decoded, but servicesSupportedCalled was absent or malformed."
                    : "MMS InitiateResponse and servicesSupportedCalled were decoded."
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException or OverflowException)
        {
            return new AcseMmsNegotiatedCapabilities
            {
                Diagnostic = $"MMS InitiateResponse capability decode failed conservatively: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private static int? ReadPositiveInt(BerTlv tlv)
    {
        if (tlv.EncodedTag == 0)
            return null;

        var value = BerReader.ReadUnsignedInteger(tlv);
        return value.HasValue && value.Value <= int.MaxValue ? (int)value.Value : null;
    }

    private static (byte[] Bits, int UnusedBits) DecodeImplicitBitString(BerTlv tlv)
    {
        if (tlv.EncodedTag == 0 || tlv.Value.Length < 1)
            return (Array.Empty<byte>(), 0);

        var span = tlv.Value.Span;
        var unusedBits = span[0];
        if (unusedBits > 7)
            return (Array.Empty<byte>(), 0);

        return (span[1..].ToArray(), unusedBits);
    }

    private static bool? ReadServiceBit((byte[] Bits, int UnusedBits) bitString, int bitIndex)
    {
        if (bitString.Bits.Length == 0 || bitIndex < 0)
            return null;

        var bitCount = checked(bitString.Bits.Length * 8 - bitString.UnusedBits);
        if (bitIndex >= bitCount)
            return null;

        var octet = bitIndex / 8;
        var bitInOctet = bitIndex % 8;
        return (bitString.Bits[octet] & (0x80 >> bitInOctet)) != 0;
    }

    private static bool TryFindConstructedTag(ReadOnlyMemory<byte> source, byte encodedTag, out BerTlv match)
    {
        match = default;
        var offset = 0;
        while (offset < source.Length)
        {
            if (!BerReader.TryReadTlv(source, ref offset, out var item))
                return false;

            if (item.EncodedTag == encodedTag)
            {
                match = item;
                return true;
            }

            if (item.Constructed && TryFindConstructedTag(item.Value, encodedTag, out match))
                return true;
        }

        return false;
    }

    private static bool TryFindSessionUserData(ReadOnlySpan<byte> response, out int payloadOffset, out int payloadLength)
    {
        payloadOffset = 0;
        payloadLength = 0;
        if (response.Length < 4 || response[0] != 0x0E)
            return false;

        for (var index = 2; index + 2 <= response.Length; index++)
        {
            if (response[index] != 0xC1)
                continue;

            var length = response[index + 1];
            var start = index + 2;
            if (start + length > response.Length)
                continue;

            payloadOffset = start;
            payloadLength = length;
            return true;
        }

        return false;
    }
}
