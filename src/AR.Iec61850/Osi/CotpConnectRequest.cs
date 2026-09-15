namespace AR.Iec61850.Osi;

public sealed class CotpConnectParameters
{
    public byte[] SourceTsap { get; init; } = [0x00, 0x01];
    public byte[] DestinationTsap { get; init; } = [0x00, 0x01];
    public byte TpduSizeExponent { get; init; } = 0x0A;
}

public static class CotpConnectRequest
{
    public static byte[] BuildDefault()
        => Build(new CotpConnectParameters());

    public static byte[] Build(CotpConnectParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ValidateTsap(parameters.SourceTsap, nameof(parameters.SourceTsap));
        ValidateTsap(parameters.DestinationTsap, nameof(parameters.DestinationTsap));

        if (parameters.TpduSizeExponent is < 0x07 or > 0x10)
            throw new ArgumentOutOfRangeException(nameof(parameters.TpduSizeExponent), "COTP TPDU size exponent must be between 7 and 16.");

        var lengthIndicator = 6 + 3 + 2 + parameters.SourceTsap.Length + 2 + parameters.DestinationTsap.Length;
        if (lengthIndicator > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(parameters), "COTP CR header exceeds one-byte length indicator capacity.");

        var frame = new List<byte>(lengthIndicator + 1)
        {
            (byte)lengthIndicator,
            0xE0,
            0x00, 0x00,
            0x00, 0x01,
            0x00,
            0xC0, 0x01, parameters.TpduSizeExponent,
            0xC1, (byte)parameters.SourceTsap.Length
        };

        frame.AddRange(parameters.SourceTsap);
        frame.Add(0xC2);
        frame.Add((byte)parameters.DestinationTsap.Length);
        frame.AddRange(parameters.DestinationTsap);
        return frame.ToArray();
    }

    private static void ValidateTsap(byte[] selector, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(selector, parameterName);
        if (selector.Length is < 1 or > 16)
            throw new ArgumentOutOfRangeException(parameterName, "COTP TSAP selector must contain 1 to 16 byte(s).");
    }
}
