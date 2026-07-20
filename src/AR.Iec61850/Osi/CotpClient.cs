namespace AR.Iec61850.Osi;

public sealed class CotpClient
{
    internal const int MaximumReassembledResponseBytes = 64 * 1024 * 1024;
    internal const int MaximumResponseFragments = 1_048_576;
    internal const int MaximumEmptyNonFinalFragments = 1_024;

    private readonly TpktClient _tpkt;

    public CotpClient(TpktClient tpkt)
    {
        _tpkt = tpkt ?? throw new ArgumentNullException(nameof(tpkt));
    }

    public bool IsConnected { get; private set; }
    public bool HasDataAvailable => IsConnected && _tpkt.HasDataAvailable;
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

        using var reassembled = new MemoryStream();
        var fragmentCount = 0;
        var emptyNonFinalFragments = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tpktPayload = await _tpkt.ReceiveTpktAsync(cancellationToken).ConfigureAwait(false);
            if (tpktPayload.Length < 3)
                throw new InvalidDataException($"COTP data response is too short ({tpktPayload.Length} byte)." );

            var headerLength = tpktPayload[0];
            if (headerLength < 2 || tpktPayload.Length < headerLength + 1)
                throw new InvalidDataException($"Invalid COTP data header length {headerLength} for payload size {tpktPayload.Length}." );

            if (tpktPayload[1] != 0xF0)
                throw new InvalidDataException($"Expected COTP Data TPDU 0xF0, received 0x{tpktPayload[1]:X2}." );

            fragmentCount++;
            if (fragmentCount > MaximumResponseFragments)
            {
                throw new InvalidDataException(
                    $"COTP segmented response exceeded the bounded limit of {MaximumResponseFragments:N0} TPDU fragments. " +
                    $"Reassembled {reassembled.Length:N0} byte(s) before EOT." );
            }

            var endOfTransmission = (tpktPayload[2] & 0x80) != 0;
            var userDataOffset = headerLength + 1;
            var userDataLength = tpktPayload.Length - userDataOffset;

            if (userDataLength == 0 && !endOfTransmission)
            {
                emptyNonFinalFragments++;
                if (emptyNonFinalFragments > MaximumEmptyNonFinalFragments)
                {
                    throw new InvalidDataException(
                        $"COTP segmented response contained more than {MaximumEmptyNonFinalFragments:N0} empty non-final TPDU fragments." );
                }
            }

            if (reassembled.Length + userDataLength > MaximumReassembledResponseBytes)
            {
                throw new InvalidDataException(
                    $"COTP segmented response exceeded the bounded reassembly limit of {MaximumReassembledResponseBytes:N0} byte(s). " +
                    $"Fragments={fragmentCount:N0}, receivedBeforeFragment={reassembled.Length:N0}, incoming={userDataLength:N0}." );
            }

            if (userDataLength > 0)
                reassembled.Write(tpktPayload, userDataOffset, userDataLength);

            if (endOfTransmission)
                return reassembled.ToArray();
        }
    }
}