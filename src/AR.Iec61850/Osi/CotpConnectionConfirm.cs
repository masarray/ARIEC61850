namespace AR.Iec61850.Osi;

public sealed class CotpConnectionConfirm
{
    public bool IsAccepted { get; init; }
    public byte TpduCode { get; init; }
    public ushort DestinationReference { get; init; }
    public ushort SourceReference { get; init; }
    public string Message { get; init; } = string.Empty;

    public static CotpConnectionConfirm Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 7)
        {
            return new CotpConnectionConfirm
            {
                IsAccepted = false,
                Message = $"COTP response is too short ({payload.Length} byte)."
            };
        }

        var headerLength = payload[0];
        if (headerLength + 1 > payload.Length)
        {
            return new CotpConnectionConfirm
            {
                IsAccepted = false,
                Message = $"COTP header length {headerLength} exceeds received payload {payload.Length}."
            };
        }

        var tpduCode = payload[1];
        var destinationReference = (ushort)((payload[2] << 8) | payload[3]);
        var sourceReference = (ushort)((payload[4] << 8) | payload[5]);

        return new CotpConnectionConfirm
        {
            IsAccepted = tpduCode == 0xD0,
            TpduCode = tpduCode,
            DestinationReference = destinationReference,
            SourceReference = sourceReference,
            Message = tpduCode == 0xD0
                ? $"COTP connection confirmed. SrcRef=0x{sourceReference:X4}, DstRef=0x{destinationReference:X4}."
                : $"COTP connection was not confirmed. TPDU=0x{tpduCode:X2}."
        };
    }
}
