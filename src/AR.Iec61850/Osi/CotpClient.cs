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

        using var accumulator = new CotpDataSequenceAccumulator(
            MaximumReassembledResponseBytes,
            MaximumResponseFragments,
            MaximumEmptyNonFinalFragments);

        while (!accumulator.IsComplete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tpktPayload = await _tpkt.ReceiveTpktAsync(cancellationToken).ConfigureAwait(false);
            accumulator.AppendTpktPayload(tpktPayload);
        }

        return accumulator.Complete();
    }
}

/// <summary>
/// Reassembles one COTP Data TPDU sequence. Large MMS FileRead responses can
/// legitimately span thousands of TPKT/COTP frames, so safety is enforced by
/// bounded total bytes plus very high fragment and empty-fragment guards rather
/// than the previous interoperability-breaking 32-fragment ceiling.
/// </summary>
internal sealed class CotpDataSequenceAccumulator : IDisposable
{
    private readonly long _maximumBytes;
    private readonly int _maximumFragments;
    private readonly int _maximumEmptyNonFinalFragments;
    private readonly MemoryStream _buffer = new();
    private int _fragmentCount;
    private int _emptyNonFinalFragments;

    public CotpDataSequenceAccumulator(
        long maximumBytes,
        int maximumFragments,
        int maximumEmptyNonFinalFragments)
    {
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (maximumFragments <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFragments));
        if (maximumEmptyNonFinalFragments <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEmptyNonFinalFragments));

        _maximumBytes = maximumBytes;
        _maximumFragments = maximumFragments;
        _maximumEmptyNonFinalFragments = maximumEmptyNonFinalFragments;
    }

    public bool IsComplete { get; private set; }
    public int FragmentCount => _fragmentCount;
    public long ReassembledBytes => _buffer.Length;

    public void AppendTpktPayload(ReadOnlySpan<byte> tpktPayload)
    {
        if (IsComplete)
            throw new InvalidOperationException("The COTP Data TPDU sequence is already complete.");
        if (tpktPayload.Length < 3)
            throw new InvalidDataException($"COTP data response is too short ({tpktPayload.Length} byte)." );

        var headerLength = tpktPayload[0];
        if (headerLength < 2 || tpktPayload.Length < headerLength + 1)
            throw new InvalidDataException($"Invalid COTP data header length {headerLength} for payload size {tpktPayload.Length}." );
        if (tpktPayload[1] != 0xF0)
            throw new InvalidDataException($"Expected COTP Data TPDU 0xF0, received 0x{tpktPayload[1]:X2}." );

        _fragmentCount++;
        if (_fragmentCount > _maximumFragments)
        {
            throw new InvalidDataException(
                $"COTP segmented response exceeded the bounded limit of {_maximumFragments:N0} TPDU fragments. " +
                $"Reassembled {_buffer.Length:N0} byte(s) before EOT." );
        }

        var endOfTransmission = (tpktPayload[2] & 0x80) != 0;
        var userDataOffset = headerLength + 1;
        var userDataLength = tpktPayload.Length - userDataOffset;

        if (userDataLength == 0 && !endOfTransmission)
        {
            _emptyNonFinalFragments++;
            if (_emptyNonFinalFragments > _maximumEmptyNonFinalFragments)
            {
                throw new InvalidDataException(
                    $"COTP segmented response contained more than {_maximumEmptyNonFinalFragments:N0} empty non-final TPDU fragments." );
            }
        }

        if (_buffer.Length + userDataLength > _maximumBytes)
        {
            throw new InvalidDataException(
                $"COTP segmented response exceeded the bounded reassembly limit of {_maximumBytes:N0} byte(s). " +
                $"Fragments={_fragmentCount:N0}, receivedBeforeFragment={_buffer.Length:N0}, incoming={userDataLength:N0}." );
        }

        if (userDataLength > 0)
            _buffer.Write(tpktPayload[userDataOffset..]);

        IsComplete = endOfTransmission;
    }

    public byte[] Complete()
    {
        if (!IsComplete)
            throw new InvalidOperationException("The COTP Data TPDU sequence ended before EOT.");

        return _buffer.ToArray();
    }

    public void Dispose() => _buffer.Dispose();
}