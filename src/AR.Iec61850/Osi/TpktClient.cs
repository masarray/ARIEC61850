using System.Net.Sockets;

namespace AR.Iec61850.Osi;

public sealed class TpktClient : IAsyncDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    public bool IsConnected => _tcpClient?.Connected == true;
    public bool HasDataAvailable => _stream?.DataAvailable == true;

    public async Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("MMS host cannot be empty.", nameof(host));

        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "TCP port must be 1..65535.");

        await DisposeAsync().ConfigureAwait(false);

        _tcpClient = new TcpClient { NoDelay = true };
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : timeout);

        await _tcpClient.ConnectAsync(host, port, timeoutSource.Token).ConfigureAwait(false);
        _stream = _tcpClient.GetStream();
    }

    public async Task SendTpktAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (_stream == null)
            throw new InvalidOperationException("TPKT stream is not connected.");

        if (payload.Length > ushort.MaxValue - 4)
            throw new ArgumentOutOfRangeException(nameof(payload), "TPKT payload is too large.");

        var frame = new byte[payload.Length + 4];
        frame[0] = 0x03;
        frame[1] = 0x00;
        frame[2] = (byte)(frame.Length >> 8);
        frame[3] = (byte)(frame.Length & 0xFF);
        payload.CopyTo(frame.AsMemory(4));

        await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> ReceiveTpktAsync(CancellationToken cancellationToken)
    {
        if (_stream == null)
            throw new InvalidOperationException("TPKT stream is not connected.");

        var header = await ReadExactAsync(4, cancellationToken).ConfigureAwait(false);
        if (header[0] != 0x03)
            throw new InvalidDataException($"Unsupported TPKT version {header[0]}.");

        var length = (header[2] << 8) | header[3];
        if (length < 4)
            throw new InvalidDataException($"Invalid TPKT length {length}.");

        return await ReadExactAsync(length - 4, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken)
    {
        if (_stream == null)
            throw new InvalidOperationException("TPKT stream is not connected.");

        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("Remote IEC 61850 peer closed the TCP connection.");

            offset += read;
        }

        return buffer;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
        }

        try
        {
            _tcpClient?.Close();
        }
        catch
        {
        }

        _stream = null;
        _tcpClient = null;
        return ValueTask.CompletedTask;
    }
}
