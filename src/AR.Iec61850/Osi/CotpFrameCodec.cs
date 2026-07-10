namespace AR.Iec61850.Osi;

public enum CotpTpduKind
{
    Unknown,
    ConnectionRequest,
    ConnectionConfirm,
    Data,
    DisconnectRequest,
    Error
}

public sealed class CotpTpdu
{
    public CotpTpduKind Kind { get; init; }
    public byte TpduCode { get; init; }
    public int HeaderLength { get; init; }
    public ushort DestinationReference { get; init; }
    public ushort SourceReference { get; init; }
    public bool EndOfTransmission { get; init; }
    public byte[] Parameters { get; init; } = Array.Empty<byte>();
    public byte[] UserData { get; init; } = Array.Empty<byte>();
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
}

public static class CotpFrameCodec
{
    public const byte ConnectionRequestCode = 0xE0;
    public const byte ConnectionConfirmCode = 0xD0;
    public const byte DisconnectRequestCode = 0x80;
    public const byte DataCode = 0xF0;
    public const byte ErrorCode = 0x70;

    public static CotpTpdu Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            return new CotpTpdu
            {
                Kind = CotpTpduKind.Unknown,
                IsValid = false,
                Message = $"COTP TPDU is too short ({payload.Length} byte)."
            };
        }

        var headerLength = payload[0];
        if (headerLength < 2 || headerLength + 1 > payload.Length)
        {
            return new CotpTpdu
            {
                HeaderLength = headerLength,
                TpduCode = payload[1],
                Kind = ToKind(payload[1]),
                IsValid = false,
                Message = $"Invalid COTP header length {headerLength} for payload size {payload.Length}."
            };
        }

        var tpduCode = payload[1];
        var kind = ToKind(tpduCode);

        if (kind == CotpTpduKind.Data)
        {
            if (payload.Length < 3)
            {
                return new CotpTpdu
                {
                    Kind = kind,
                    TpduCode = tpduCode,
                    HeaderLength = headerLength,
                    IsValid = false,
                    Message = "COTP Data TPDU is missing EOT/TPDU-NR byte."
                };
            }

            var userDataOffset = headerLength + 1;
            return new CotpTpdu
            {
                Kind = kind,
                TpduCode = tpduCode,
                HeaderLength = headerLength,
                EndOfTransmission = (payload[2] & 0x80) != 0,
                UserData = payload[userDataOffset..].ToArray(),
                IsValid = true,
                Message = $"COTP Data TPDU valid. eot={((payload[2] & 0x80) != 0)} userDataBytes={payload.Length - userDataOffset}."
            };
        }

        ushort destinationReference = 0;
        ushort sourceReference = 0;
        byte[] parameters = Array.Empty<byte>();
        if (payload.Length >= 6)
        {
            destinationReference = (ushort)((payload[2] << 8) | payload[3]);
            sourceReference = (ushort)((payload[4] << 8) | payload[5]);
        }

        var parameterStart = 7;
        var parameterEnd = Math.Min(headerLength + 1, payload.Length);
        if (parameterEnd > parameterStart)
            parameters = payload[parameterStart..parameterEnd].ToArray();

        return new CotpTpdu
        {
            Kind = kind,
            TpduCode = tpduCode,
            HeaderLength = headerLength,
            DestinationReference = destinationReference,
            SourceReference = sourceReference,
            Parameters = parameters,
            EndOfTransmission = true,
            IsValid = kind != CotpTpduKind.Unknown,
            Message = kind == CotpTpduKind.Unknown
                ? $"Unknown COTP TPDU 0x{tpduCode:X2}."
                : $"COTP {kind} TPDU valid. srcRef=0x{sourceReference:X4} dstRef=0x{destinationReference:X4} params={parameters.Length} byte(s)."
        };
    }

    public static byte[] EncodeConnectionConfirm(ushort destinationReference, ushort sourceReference, byte tpduSize = 0x0A)
        => EncodeConnectionConfirm(destinationReference, sourceReference, ReadOnlySpan<byte>.Empty, tpduSize);

    public static byte[] EncodeConnectionConfirm(CotpTpdu connectionRequest, ushort sourceReference, byte tpduSize = 0x0A)
    {
        ArgumentNullException.ThrowIfNull(connectionRequest);

        return EncodeConnectionConfirm(
            connectionRequest.SourceReference,
            sourceReference,
            connectionRequest.Parameters,
            tpduSize);
    }

    private static byte[] EncodeConnectionConfirm(
        ushort destinationReference,
        ushort sourceReference,
        ReadOnlySpan<byte> requestParameters,
        byte tpduSize)
    {
        var parameters = BuildConnectionConfirmParameters(requestParameters, tpduSize);
        var headerLength = checked((byte)(6 + parameters.Length));
        var frame = new byte[headerLength + 1];
        frame[0] = headerLength;
        frame[1] = ConnectionConfirmCode;
        frame[2] = (byte)(destinationReference >> 8);
        frame[3] = (byte)(destinationReference & 0xFF);
        frame[4] = (byte)(sourceReference >> 8);
        frame[5] = (byte)(sourceReference & 0xFF);
        frame[6] = 0x00;
        Buffer.BlockCopy(parameters, 0, frame, 7, parameters.Length);
        return frame;
    }

    public static byte[] EncodeData(ReadOnlySpan<byte> userData, bool endOfTransmission = true)
    {
        var frame = new byte[userData.Length + 3];
        frame[0] = 0x02;
        frame[1] = DataCode;
        frame[2] = endOfTransmission ? (byte)0x80 : (byte)0x00;
        userData.CopyTo(frame.AsSpan(3));
        return frame;
    }

    public static byte[] EncodeDefaultConnectRequest() => CotpConnectRequest.BuildDefault();

    private static byte[] BuildConnectionConfirmParameters(ReadOnlySpan<byte> requestParameters, byte tpduSize)
    {
        var selectedTpduSize = SelectTpduSize(requestParameters, tpduSize);
        var selected = new List<byte[]>
        {
            new byte[] { 0xC0, 0x01, selectedTpduSize }
        };

        var offset = 0;
        while (offset + 2 <= requestParameters.Length)
        {
            var code = requestParameters[offset];
            var length = requestParameters[offset + 1];
            var next = offset + 2 + length;
            if (next > requestParameters.Length)
                break;

            if (code is 0xC1 or 0xC2 && length > 0)
            {
                var parameter = new byte[length + 2];
                requestParameters.Slice(offset, length + 2).CopyTo(parameter);
                parameter[0] = code == 0xC1 ? (byte)0xC2 : (byte)0xC1;
                selected.Add(parameter);
            }

            offset = next;
        }

        return Concat(selected);
    }

    private static byte SelectTpduSize(ReadOnlySpan<byte> requestParameters, byte defaultTpduSize)
    {
        var offset = 0;
        while (offset + 2 <= requestParameters.Length)
        {
            var code = requestParameters[offset];
            var length = requestParameters[offset + 1];
            var next = offset + 2 + length;
            if (next > requestParameters.Length)
                break;

            if (code == 0xC0 && length == 1)
                return requestParameters[offset + 2] < defaultTpduSize ? requestParameters[offset + 2] : defaultTpduSize;

            offset = next;
        }

        return defaultTpduSize;
    }

    private static byte[] Concat(IReadOnlyList<byte[]> parts)
    {
        var length = 0;
        foreach (var part in parts)
            length += part.Length;

        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }

    private static CotpTpduKind ToKind(byte tpduCode)
        => tpduCode switch
        {
            ConnectionRequestCode => CotpTpduKind.ConnectionRequest,
            ConnectionConfirmCode => CotpTpduKind.ConnectionConfirm,
            DataCode => CotpTpduKind.Data,
            DisconnectRequestCode => CotpTpduKind.DisconnectRequest,
            ErrorCode => CotpTpduKind.Error,
            _ => CotpTpduKind.Unknown
        };
}
