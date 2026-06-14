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
        if (payload.Length >= 6)
        {
            destinationReference = (ushort)((payload[2] << 8) | payload[3]);
            sourceReference = (ushort)((payload[4] << 8) | payload[5]);
        }

        return new CotpTpdu
        {
            Kind = kind,
            TpduCode = tpduCode,
            HeaderLength = headerLength,
            DestinationReference = destinationReference,
            SourceReference = sourceReference,
            EndOfTransmission = true,
            IsValid = kind != CotpTpduKind.Unknown,
            Message = kind == CotpTpduKind.Unknown
                ? $"Unknown COTP TPDU 0x{tpduCode:X2}."
                : $"COTP {kind} TPDU valid. srcRef=0x{sourceReference:X4} dstRef=0x{destinationReference:X4}."
        };
    }

    public static byte[] EncodeConnectionConfirm(ushort destinationReference, ushort sourceReference, byte tpduSize = 0x0A)
    {
        return
        [
            0x09,
            ConnectionConfirmCode,
            (byte)(destinationReference >> 8), (byte)(destinationReference & 0xFF),
            (byte)(sourceReference >> 8), (byte)(sourceReference & 0xFF),
            0x00,
            0xC0, 0x01, tpduSize
        ];
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
