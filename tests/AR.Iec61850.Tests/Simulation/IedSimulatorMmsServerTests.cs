using System.Net.Sockets;
using AR.Iec61850.Acse;
using AR.Iec61850.Osi;
using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class IedSimulatorMmsServerTests
{
    [Fact]
    public async Task Server_Binds_And_Reports_Running_Then_Stops()
    {
        var engine = new IedSimulatorEngine(IedSimulatorProfile.CreateDefaultFeederProfile());
        await using var server = IedSimulatorMmsServer.Create(engine, new IedSimulatorMmsServerOptions
        {
            Host = "127.0.0.1",
            Port = 0
        });

        server.Start();
        Assert.True(server.IsRunning);
        Assert.True(server.BoundPort > 0);

        await server.StopAsync();
        Assert.False(server.IsRunning);

        var activity = server.RecentActivity();
        Assert.Contains(activity, a => a.Kind == IedSimulatorServerActivityKind.ServerStarted);
        Assert.Contains(activity, a => a.Kind == IedSimulatorServerActivityKind.ServerStopped);
    }

    [Fact]
    public async Task Server_Completes_Cotp_And_Acse_Association_With_A_Real_Client()
    {
        var engine = new IedSimulatorEngine(IedSimulatorProfile.CreateDefaultFeederProfile());
        await using var server = IedSimulatorMmsServer.Create(engine, new IedSimulatorMmsServerOptions
        {
            Host = "127.0.0.1",
            Port = 0
        });

        server.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", server.BoundPort, timeout.Token);
        await using var stream = client.GetStream();

        // COTP connection request -> expect connection confirm.
        await stream.WriteAsync(TpktFrameCodec.Encode(CotpFrameCodec.EncodeDefaultConnectRequest()), timeout.Token);
        var ccFrame = await ReadTpktFrameAsync(stream, timeout.Token);
        var cc = CotpFrameCodec.Decode(TpktFrameCodec.Decode(ccFrame).Payload);
        Assert.True(cc.IsValid);
        Assert.Equal(CotpTpduKind.ConnectionConfirm, cc.Kind);

        // ACSE associate request -> expect an AARE response wrapped in a COTP Data TPDU.
        await stream.WriteAsync(TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(AcseMmsInitiateRequest.BuildDefaultAssociationPayload())), timeout.Token);
        var aareFrame = await ReadTpktFrameAsync(stream, timeout.Token);
        var aare = CotpFrameCodec.Decode(TpktFrameCodec.Decode(aareFrame).Payload);
        Assert.True(aare.IsValid);
        Assert.Equal(CotpTpduKind.Data, aare.Kind);
        Assert.NotEmpty(aare.UserData);

        await server.StopAsync();
        Assert.Equal(1, server.AcceptedConnectionCount);
        var activity = server.RecentActivity();
        Assert.Contains(activity, a => a.Kind == IedSimulatorServerActivityKind.ClientConnected);
        Assert.Contains(activity, a => a.Kind == IedSimulatorServerActivityKind.HandshakeReceived && a.Operation == "COTP CR");
        Assert.Contains(activity, a => a.Kind == IedSimulatorServerActivityKind.HandshakeSent && a.Operation == "COTP CC");
        Assert.Contains(activity, a => a.Kind == IedSimulatorServerActivityKind.HandshakeReceived && a.Operation == "ACSE AARQ");
        Assert.Contains(activity, a => a.Kind == IedSimulatorServerActivityKind.HandshakeSent && a.Operation == "ACSE AARE" && a.Target.Contains("SessionMirror", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Server_Records_ClientClosed_When_Client_Stops_After_Cotp()
    {
        var engine = new IedSimulatorEngine(IedSimulatorProfile.CreateDefaultFeederProfile());
        await using var server = IedSimulatorMmsServer.Create(engine, new IedSimulatorMmsServerOptions
        {
            Host = "127.0.0.1",
            Port = 0
        });

        server.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using (var client = new TcpClient())
        {
            await client.ConnectAsync("127.0.0.1", server.BoundPort, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(TpktFrameCodec.Encode(CotpFrameCodec.EncodeDefaultConnectRequest()), timeout.Token);
            _ = await ReadTpktFrameAsync(stream, timeout.Token);
        }

        var closed = await WaitForActivityAsync(
            server,
            a => a.Kind == IedSimulatorServerActivityKind.ClientClosed && a.Operation == "ACSE AARQ",
            timeout.Token);

        await server.StopAsync();

        Assert.Contains("before sending ACSE AARQ", closed.Message, StringComparison.Ordinal);
    }

    private static async Task<byte[]> ReadTpktFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, TpktFrameCodec.HeaderLength, cancellationToken);
        var declaredLength = (header[2] << 8) | header[3];
        var body = await ReadExactAsync(stream, declaredLength - TpktFrameCodec.HeaderLength, cancellationToken);
        var frame = new byte[declaredLength];
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        Buffer.BlockCopy(body, 0, frame, header.Length, body.Length);
        return frame;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0)
                throw new IOException("Stream closed before the expected frame was read.");
            offset += read;
        }

        return buffer;
    }

    private static async Task<IedSimulatorServerActivity> WaitForActivityAsync(
        IedSimulatorMmsServer server,
        Func<IedSimulatorServerActivity, bool> predicate,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var match = server.RecentActivity().FirstOrDefault(predicate);
            if (match is not null)
                return match;

            await Task.Delay(20, cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for simulator activity.");
    }
}
