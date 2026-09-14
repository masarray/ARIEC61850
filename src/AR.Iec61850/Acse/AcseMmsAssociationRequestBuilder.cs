namespace AR.Iec61850.Acse;

public sealed class AcseEndpointIdentity
{
    public byte[] PresentationSelector { get; init; } = Array.Empty<byte>();
    public byte[] SessionSelector { get; init; } = Array.Empty<byte>();
    public uint[] ApTitle { get; init; } = Array.Empty<uint>();
    public int AeQualifier { get; init; }
}

public sealed class MmsInitiateRequestParameters
{
    public int LocalDetailCalling { get; init; } = 65000;
    public int MaxOutstandingCalling { get; init; } = 10;
    public int MaxOutstandingCalled { get; init; } = 10;
    public int NestingLevel { get; init; } = 5;
}

public sealed class AcseMmsAssociationRequestParameters
{
    public AcseEndpointIdentity Calling { get; init; } = new();
    public AcseEndpointIdentity Called { get; init; } = new();
    public MmsInitiateRequestParameters Initiate { get; init; } = new();
}

public static class AcseMmsAssociationRequestBuilder
{
    private static readonly byte[] PresentationContextDefinitions =
    [
        0x30, 0x0F, 0x02, 0x01, 0x01, 0x06, 0x04, 0x52, 0x01, 0x00, 0x01, 0x30, 0x04, 0x06, 0x02, 0x51, 0x01,
        0x30, 0x10, 0x02, 0x01, 0x03, 0x06, 0x05, 0x28, 0xCA, 0x22, 0x02, 0x01, 0x30, 0x04, 0x06, 0x02, 0x51, 0x01
    ];

    private static readonly byte[] DefaultInitiateDetail =
    [
        0x80, 0x01, 0x01,
        0x81, 0x03, 0x05, 0xF1, 0x00,
        0x82, 0x0C, 0x03, 0xEE, 0x1C, 0x00, 0x00, 0x04, 0x08, 0x00, 0x00, 0x79, 0xEF, 0x18
    ];

    public static byte[] BuildSessionConnect(AcseMmsAssociationRequestParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ValidateEndpoint(parameters.Calling, nameof(parameters.Calling));
        ValidateEndpoint(parameters.Called, nameof(parameters.Called));
        ValidateInitiate(parameters.Initiate);

        var mmsInitiate = Tlv(0xA8, Combine(
            Tlv(0x80, EncodeUnsignedInteger(parameters.Initiate.LocalDetailCalling)),
            Tlv(0x81, EncodeUnsignedInteger(parameters.Initiate.MaxOutstandingCalling)),
            Tlv(0x82, EncodeUnsignedInteger(parameters.Initiate.MaxOutstandingCalled)),
            Tlv(0x83, EncodeUnsignedInteger(parameters.Initiate.NestingLevel)),
            Tlv(0xA4, DefaultInitiateDetail)));

        var external = Tlv(0x28, Combine(
            Tlv(0x06, EncodeObjectIdentifier([2, 1, 1])),
            Tlv(0x02, EncodeUnsignedInteger(3)),
            Tlv(0xA0, mmsInitiate)));

        var userInformation = Tlv(0xBE, external);
        var aarq = Tlv(0x60, Combine(
            Tlv(0xA1, Tlv(0x06, EncodeObjectIdentifier([1, 0, 9506, 2, 3]))),
            Tlv(0xA2, Tlv(0x06, EncodeObjectIdentifier(parameters.Called.ApTitle))),
            Tlv(0xA3, Tlv(0x02, EncodeUnsignedInteger(parameters.Called.AeQualifier))),
            Tlv(0xA6, Tlv(0x06, EncodeObjectIdentifier(parameters.Calling.ApTitle))),
            Tlv(0xA7, Tlv(0x02, EncodeUnsignedInteger(parameters.Calling.AeQualifier))),
            userInformation));

        var presentationUserData = Tlv(0x61, Tlv(0x30, Combine(
            Tlv(0x02, EncodeUnsignedInteger(1)),
            Tlv(0xA0, aarq))));

        var normalModeParameters = Combine(
            Tlv(0x81, parameters.Calling.PresentationSelector),
            Tlv(0x82, parameters.Called.PresentationSelector),
            Tlv(0xA4, PresentationContextDefinitions),
            presentationUserData);

        var presentationConnect = Tlv(0x31, Combine(
            Tlv(0xA0, Tlv(0x80, [0x01])),
            Tlv(0xA2, normalModeParameters)));

        var sessionBody = Combine(
            [0x05, 0x06, 0x13, 0x01, 0x00, 0x16, 0x01, 0x02, 0x14, 0x02, 0x00, 0x02],
            SessionParameter(0x33, parameters.Calling.SessionSelector),
            SessionParameter(0x34, parameters.Called.SessionSelector),
            SessionParameter(0xC1, presentationConnect));

        return Combine([0x0D], EncodeSessionLength(sessionBody.Length), sessionBody);
    }

    private static void ValidateEndpoint(AcseEndpointIdentity endpoint, string name)
    {
        ArgumentNullException.ThrowIfNull(endpoint, name);
        if (endpoint.PresentationSelector.Length is < 1 or > 16)
            throw new ArgumentOutOfRangeException(name, "Presentation selector must contain 1 to 16 byte(s).");
        if (endpoint.SessionSelector.Length is < 1 or > 16)
            throw new ArgumentOutOfRangeException(name, "Session selector must contain 1 to 16 byte(s).");
        ValidateObjectIdentifier(endpoint.ApTitle, name);
        if (endpoint.AeQualifier is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(name, "AE qualifier must be between 0 and 65535.");
    }

    private static void ValidateInitiate(MmsInitiateRequestParameters initiate)
    {
        ArgumentNullException.ThrowIfNull(initiate);
        if (initiate.LocalDetailCalling <= 0) throw new ArgumentOutOfRangeException(nameof(initiate.LocalDetailCalling));
        if (initiate.MaxOutstandingCalling <= 0) throw new ArgumentOutOfRangeException(nameof(initiate.MaxOutstandingCalling));
        if (initiate.MaxOutstandingCalled <= 0) throw new ArgumentOutOfRangeException(nameof(initiate.MaxOutstandingCalled));
        if (initiate.NestingLevel <= 0) throw new ArgumentOutOfRangeException(nameof(initiate.NestingLevel));
    }

    private static byte[] SessionParameter(byte code, byte[] value) => Combine([code], EncodeSessionLength(value.Length), value);

    private static byte[] EncodeSessionLength(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (length <= 254) return [(byte)length];
        if (length <= ushort.MaxValue) return [0xFF, (byte)(length >> 8), (byte)length];
        throw new ArgumentOutOfRangeException(nameof(length), "ISO Session parameter exceeds 65535 byte(s).");
    }

    private static byte[] Tlv(byte tag, byte[] value) => Combine([tag], EncodeBerLength(value.Length), value);

    private static byte[] EncodeBerLength(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (length < 0x80) return [(byte)length];
        Span<byte> scratch = stackalloc byte[4];
        var value = (uint)length;
        var index = scratch.Length;
        while (value > 0) { scratch[--index] = (byte)value; value >>= 8; }
        var count = scratch.Length - index;
        var result = new byte[count + 1];
        result[0] = (byte)(0x80 | count);
        scratch[index..].CopyTo(result.AsSpan(1));
        return result;
    }

    private static byte[] EncodeUnsignedInteger(int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == 0) return [0x00];
        Span<byte> scratch = stackalloc byte[5];
        var unsigned = (uint)value;
        var index = scratch.Length;
        while (unsigned > 0) { scratch[--index] = (byte)unsigned; unsigned >>= 8; }
        if ((scratch[index] & 0x80) != 0) scratch[--index] = 0x00;
        return scratch[index..].ToArray();
    }

    private static byte[] EncodeObjectIdentifier(IReadOnlyList<uint> arcs)
    {
        ValidateObjectIdentifier(arcs, nameof(arcs));
        var result = new List<byte>();
        AppendBase128(result, ((ulong)arcs[0] * 40UL) + arcs[1]);
        for (var i = 2; i < arcs.Count; i++) AppendBase128(result, arcs[i]);
        return result.ToArray();
    }

    private static void ValidateObjectIdentifier(IReadOnlyList<uint> arcs, string name)
    {
        ArgumentNullException.ThrowIfNull(arcs, name);
        if (arcs.Count < 2) throw new ArgumentException("AP-title OID requires at least two arcs.", name);
        if (arcs[0] > 2) throw new ArgumentException("AP-title OID first arc must be 0, 1, or 2.", name);
        if (arcs[0] < 2 && arcs[1] > 39) throw new ArgumentException("AP-title OID second arc must be <= 39 when first arc is 0 or 1.", name);
    }

    private static void AppendBase128(ICollection<byte> output, ulong value)
    {
        Span<byte> scratch = stackalloc byte[10];
        var index = scratch.Length;
        scratch[--index] = (byte)(value & 0x7F);
        value >>= 7;
        while (value > 0) { scratch[--index] = (byte)(0x80 | (value & 0x7F)); value >>= 7; }
        for (; index < scratch.Length; index++) output.Add(scratch[index]);
    }

    private static byte[] Combine(params byte[][] parts)
    {
        var length = parts.Sum(part => part.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts) { part.CopyTo(result, offset); offset += part.Length; }
        return result;
    }
}
