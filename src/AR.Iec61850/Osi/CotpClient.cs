namespace AR.Iec61850.Osi;

public sealed class CotpClient
{
    private readonly TpktClient _tpkt;

    public CotpClient(TpktClient tpkt)
    {
        _tpkt = tpkt ?? throw new ArgumentNullException(nameof(tpkt));
    }

    public bool IsConnected { get; private set; }
    public CotpConnectionConfirm? LastConnectionConfirm { get; private set; }

    public void Reset()
    {
        IsConnected = false;
        LastConnectionConfirm = null;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        Reset();

        await _tpkt.SendTpktAsync(CotpConnectRequest.BuildDefault(), cancellationToken).ConfigureAwait(false);
        var response = await _tpkt.ReceiveTpktAsync(cancellationToken).ConfigureAwait(false);
        var confirm = CotpConnectionConfirm.Parse(response);
        LastConnectionConfirm = confirm;

        if (!confirm.IsAccepted)
            throw new InvalidDataException(confirm.Message);

        IsConnected = true;
    }

    public async Task SendDataAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (!IsConnected)
            throw new InvalidOperationException("COTP session is not connected.");

        var frame = new byte[payload.Length + 3];
        frame[0] = 0x02;
        frame[1] = 0xF0;
        frame[2] = 0x80;
        payload.CopyTo(frame.AsMemory(3));

        await _tpkt.SendTpktAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> ReceiveDataAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
            throw new InvalidOperationException("COTP session is not connected.");

        var parts = new List<byte[]>();
        var total = 0;
        var guard = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tpktPayload = await _tpkt.ReceiveTpktAsync(cancellationToken).ConfigureAwait(false);
            if (tpktPayload.Length < 3)
                throw new InvalidDataException($"COTP data response is too short ({tpktPayload.Length} byte).");

            var headerLength = tpktPayload[0];
            if (headerLength < 2 || tpktPayload.Length < headerLength + 1)
                throw new InvalidDataException($"Invalid COTP data header length {headerLength} for payload size {tpktPayload.Length}.");

            if (tpktPayload[1] != 0xF0)
                throw new InvalidDataException($"Expected COTP Data TPDU 0xF0, received 0x{tpktPayload[1]:X2}.");

            var endOfTransmission = (tpktPayload[2] & 0x80) != 0;
            var userDataOffset = headerLength + 1;
            var userData = tpktPayload[userDataOffset..];
            parts.Add(userData);
            total += userData.Length;

            if (endOfTransmission)
                break;

            guard++;
            if (guard > 32)
                throw new InvalidDataException("COTP segmented response exceeded 32 TPDU fragments.");
        }

        if (parts.Count == 1)
            return parts[0];

        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }
}
