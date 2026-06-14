namespace AR.Iec61850.Osi;

public sealed class TpktFrame
{
    public byte Version { get; init; }
    public int DeclaredLength { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;

    public int TotalLength => Payload.Length + 4;
}

public static class TpktFrameCodec
{
    public const byte SupportedVersion = 0x03;
    public const int HeaderLength = 4;

    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ushort.MaxValue - HeaderLength)
            throw new ArgumentOutOfRangeException(nameof(payload), "TPKT payload is too large.");

        var frame = new byte[payload.Length + HeaderLength];
        frame[0] = SupportedVersion;
        frame[1] = 0x00;
        frame[2] = (byte)(frame.Length >> 8);
        frame[3] = (byte)(frame.Length & 0xFF);
        payload.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }

    public static TpktFrame Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderLength)
        {
            return new TpktFrame
            {
                Version = frame.Length > 0 ? frame[0] : (byte)0,
                DeclaredLength = frame.Length,
                IsValid = false,
                Message = $"TPKT frame is too short ({frame.Length} byte)."
            };
        }

        var version = frame[0];
        var length = (frame[2] << 8) | frame[3];
        if (version != SupportedVersion)
        {
            return new TpktFrame
            {
                Version = version,
                DeclaredLength = length,
                Payload = frame[HeaderLength..].ToArray(),
                IsValid = false,
                Message = $"Unsupported TPKT version 0x{version:X2}."
            };
        }

        if (length < HeaderLength)
        {
            return new TpktFrame
            {
                Version = version,
                DeclaredLength = length,
                Payload = frame[HeaderLength..].ToArray(),
                IsValid = false,
                Message = $"Invalid TPKT declared length {length}."
            };
        }

        if (length != frame.Length)
        {
            return new TpktFrame
            {
                Version = version,
                DeclaredLength = length,
                Payload = frame[HeaderLength..].ToArray(),
                IsValid = false,
                Message = $"TPKT declared length {length} does not match received length {frame.Length}."
            };
        }

        return new TpktFrame
        {
            Version = version,
            DeclaredLength = length,
            Payload = frame[HeaderLength..].ToArray(),
            IsValid = true,
            Message = $"TPKT frame valid. payloadBytes={frame.Length - HeaderLength}."
        };
    }
}
