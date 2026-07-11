using System.Net;
using System.Net.Sockets;
using AR.Iec61850.Acse;
using AR.Iec61850.Osi;

namespace AR.Iec61850.Simulation;

public sealed class MmsVirtualIedServerOptions
{
    /// <summary>Bind address. Use <see cref="IPAddress.Any"/> to accept external clients.</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Loopback;

    /// <summary>TCP port. 102 is the IEC 61850 MMS default; 0 binds an ephemeral port.</summary>
    public int Port { get; init; } = 102;

    public ushort ServerReference { get; init; } = 0x1001;
    public string ResponseProfileName { get; init; } = "DeterministicInitiateResponse";
    public int MaxConcurrentConnections { get; init; } = 16;
}

public sealed class MmsVirtualIedConnectionEventArgs : EventArgs
{
    public required string RemoteEndpoint { get; init; }
    public int ConnectionId { get; init; }
}

public sealed class MmsVirtualIedRequestEventArgs : EventArgs
{
    public int ConnectionId { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class MmsVirtualIedServerErrorEventArgs : EventArgs
{
    public int ConnectionId { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// A persistent, read-only virtual IED MMS server. It binds a TCP listener and serves the same
/// read-only model contract validated by the loopback BER profile: COTP CR/CC, an ACSE AARE + MMS
/// InitiateResponse, then a confirmed-request loop dispatched through
/// <see cref="MmsConfirmedRequestBerDispatcher"/> against the <see cref="MmsReadOnlyServerSession"/>.
///
/// Writes are rejected by the session's read-only guard. Each connection is isolated, so one client
/// disconnecting or sending a malformed frame does not affect the listener or other clients.
///
/// Scope: this serves browse (GetNameList), read, and DataSet directory requests using the canned
/// deterministic ACSE/MMS association body with Session Accept parameters mirrored from the incoming
/// CN SPDU when possible. Full association negotiation that echoes arbitrary presentation contexts
/// and MMS InitiateRequest parameters is a separate hardening step.
/// </summary>
public sealed class MmsVirtualIedServer : IAsyncDisposable
{
    private readonly MmsReadOnlyServerProfile _profile;
    private readonly MmsReadOnlyServerSession _session;
    private readonly MmsVirtualIedServerOptions _options;
    private readonly SemaphoreSlim _connectionLimiter;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private int _connectionCounter;
    private int _acceptedConnections;
    private int _activeConnections;
    private int _requestCount;
    private int _successCount;
    private int _failureCount;

    public MmsVirtualIedServer(MmsReadOnlyServerProfile profile, MmsVirtualIedServerOptions? options = null)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _options = options ?? new MmsVirtualIedServerOptions();
        _session = new MmsReadOnlyServerSession(_profile);
        _connectionLimiter = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentConnections));
    }

    public event EventHandler<MmsVirtualIedConnectionEventArgs>? ConnectionAccepted;
    public event EventHandler<MmsVirtualIedConnectionEventArgs>? ConnectionClosed;
    public event EventHandler<MmsVirtualIedRequestEventArgs>? RequestDispatched;
    public event EventHandler<MmsVirtualIedServerErrorEventArgs>? ServerError;

    public bool IsRunning => _listener is not null;
    public int BoundPort { get; private set; }
    public int AcceptedConnectionCount => Volatile.Read(ref _acceptedConnections);
    public int ActiveConnectionCount => Volatile.Read(ref _activeConnections);
    public int RequestCount => Volatile.Read(ref _requestCount);
    public int SuccessCount => Volatile.Read(ref _successCount);
    public int FailureCount => Volatile.Read(ref _failureCount);
    public MmsReadOnlyServerProfile Profile => _profile;

    public void Start()
    {
        if (IsRunning)
            throw new InvalidOperationException("The virtual IED server is already running.");

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(_options.BindAddress, _options.Port);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var listener = _listener;
        var loop = _acceptLoop;

        _cts = null;
        _listener = null;
        _acceptLoop = null;

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        listener?.Stop();

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // Expected during shutdown.
            }
        }

        cts?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var listener = _listener;
        if (listener is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                break;
            }

            await _connectionLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            var connectionId = Interlocked.Increment(ref _connectionCounter);
            _ = Task.Run(() => HandleConnectionAsync(client, connectionId, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, int connectionId, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        Interlocked.Increment(ref _acceptedConnections);
        Interlocked.Increment(ref _activeConnections);
        ConnectionAccepted?.Invoke(this, new MmsVirtualIedConnectionEventArgs { RemoteEndpoint = remote, ConnectionId = connectionId });

        try
        {
            client.NoDelay = true;
            await using var stream = client.GetStream();

            var presentationContextId = await PerformHandshakeAsync(stream, connectionId, cancellationToken).ConfigureAwait(false);
            if (!presentationContextId.HasValue)
                return;

            await ServeRequestsAsync(stream, connectionId, presentationContextId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ServerError?.Invoke(this, new MmsVirtualIedServerErrorEventArgs { ConnectionId = connectionId, Message = ex.Message });
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
            _connectionLimiter.Release();
            try { client.Dispose(); } catch { /* ignore */ }
            ConnectionClosed?.Invoke(this, new MmsVirtualIedConnectionEventArgs { RemoteEndpoint = remote, ConnectionId = connectionId });
        }
    }

    private async Task<int?> PerformHandshakeAsync(NetworkStream stream, int connectionId, CancellationToken cancellationToken)
    {
        // COTP Connection Request -> Connection Confirm.
        var crFrame = await ReadTpktFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        var crTpkt = TpktFrameCodec.Decode(crFrame);
        if (!crTpkt.IsValid)
        {
            ServerError?.Invoke(this, new MmsVirtualIedServerErrorEventArgs { ConnectionId = connectionId, Message = $"Invalid TPKT connect frame: {crTpkt.Message}" });
            return null;
        }

        var cr = CotpFrameCodec.Decode(crTpkt.Payload);
        if (!cr.IsValid || cr.Kind != CotpTpduKind.ConnectionRequest)
        {
            ServerError?.Invoke(this, new MmsVirtualIedServerErrorEventArgs { ConnectionId = connectionId, Message = $"Expected COTP CR, received {cr.Kind}: {cr.Message}" });
            return null;
        }

        var cc = TpktFrameCodec.Encode(CotpFrameCodec.EncodeConnectionConfirm(cr, _options.ServerReference));
        await stream.WriteAsync(cc, cancellationToken).ConfigureAwait(false);

        // ACSE AARQ (COTP Data) -> AARE + MMS InitiateResponse.
        var aarqFrame = await ReadTpktFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        var aarqTpkt = TpktFrameCodec.Decode(aarqFrame);
        if (!aarqTpkt.IsValid)
        {
            ServerError?.Invoke(this, new MmsVirtualIedServerErrorEventArgs { ConnectionId = connectionId, Message = $"Invalid TPKT AARQ frame: {aarqTpkt.Message}" });
            return null;
        }

        var aarqData = CotpFrameCodec.Decode(aarqTpkt.Payload);
        if (!aarqData.IsValid || aarqData.Kind != CotpTpduKind.Data)
        {
            ServerError?.Invoke(this, new MmsVirtualIedServerErrorEventArgs { ConnectionId = connectionId, Message = $"Expected COTP Data carrying AARQ, received {aarqData.Kind}: {aarqData.Message}" });
            return null;
        }

        // Inspect for diagnostics, but remain lenient so varied clients can still associate.
        _ = AcseAssociationPayloadInspector.Inspect(aarqData.UserData);

        var associateResponse = AcseMmsAssociateResponse.SelectForRequest(_options.ResponseProfileName, aarqData.UserData);
        var aare = TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(associateResponse.Payload));
        await stream.WriteAsync(aare, cancellationToken).ConfigureAwait(false);
        return associateResponse.MmsPresentationContextId;
    }

    private async Task ServeRequestsAsync(NetworkStream stream, int connectionId, int presentationContextId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] requestFrame;
            try
            {
                requestFrame = await ReadTpktFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
            {
                // Client closed the association or sent a malformed frame; end this connection.
                break;
            }

            var requestTpkt = TpktFrameCodec.Decode(requestFrame);
            if (!requestTpkt.IsValid)
                break;

            var requestData = CotpFrameCodec.Decode(requestTpkt.Payload);
            if (!requestData.IsValid || requestData.Kind != CotpTpduKind.Data)
                break;

            var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(requestData.UserData, _session, presentationContextId);
            Interlocked.Increment(ref _requestCount);

            if (!dispatch.IsRequestDecoded)
            {
                Interlocked.Increment(ref _failureCount);
                RequestDispatched?.Invoke(this, new MmsVirtualIedRequestEventArgs
                {
                    ConnectionId = connectionId,
                    Operation = "Decode",
                    IsSuccess = false,
                    Message = dispatch.Message
                });
                // Cannot safely frame a response for an undecodable request; close the connection.
                break;
            }

            if (dispatch.Response.IsSuccess)
                Interlocked.Increment(ref _successCount);
            else
                Interlocked.Increment(ref _failureCount);

            RequestDispatched?.Invoke(this, new MmsVirtualIedRequestEventArgs
            {
                ConnectionId = connectionId,
                Operation = dispatch.Request.Operation.ToString(),
                Target = dispatch.Request.Target,
                IsSuccess = dispatch.Response.IsSuccess,
                Message = dispatch.Response.Summary
            });

            var responseFrame = TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(dispatch.ResponsePresentationPayload));
            await stream.WriteAsync(responseFrame, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ReadTpktFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, TpktFrameCodec.HeaderLength, cancellationToken).ConfigureAwait(false);
        if (header[0] != 0x03)
            throw new InvalidDataException($"Unsupported TPKT version {header[0]}.");

        var declaredLength = (header[2] << 8) | header[3];
        if (declaredLength < TpktFrameCodec.HeaderLength)
            throw new InvalidDataException($"Invalid TPKT declared length {declaredLength}.");

        var payload = await ReadExactAsync(stream, declaredLength - TpktFrameCodec.HeaderLength, cancellationToken).ConfigureAwait(false);
        var frame = new byte[declaredLength];
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        Buffer.BlockCopy(payload, 0, frame, header.Length, payload.Length);
        return frame;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Remote MMS client closed the TCP connection.");

            offset += read;
        }

        return buffer;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _connectionLimiter.Dispose();
    }
}
