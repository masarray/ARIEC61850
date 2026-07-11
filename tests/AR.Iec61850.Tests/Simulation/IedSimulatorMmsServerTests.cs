using System.Net.Sockets;
using AR.Iec61850.Acse;
using AR.Iec61850.Mms;
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

    [Fact]
    public async Task Server_Reassembles_Segmented_Request_And_Segments_Large_Directory_Response()
    {
        var points = Enumerable.Range(0, 128)
            .Select(index => IedSimulatorPoint.Status($"MMXU1.Signal{index:D3}VeryLongName.stVal", "ST", "false"))
            .ToArray();
        var engine = new IedSimulatorEngine(new IedSimulatorProfile
        {
            Name = "Segmented IED",
            LogicalDevices =
            [
                new IedSimulatorLogicalDevice
                {
                    Name = "IED1LD0",
                    LogicalNodes =
                    [
                        new IedSimulatorLogicalNode
                        {
                            Name = "MMXU1",
                            LnClass = "MMXU",
                            Points = points
                        }
                    ]
                }
            ]
        });
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
        await EstablishAssociationAsync(stream, timeout.Token);

        var request = MmsGetNameListRequest.Build(41, MmsGetNameListObjectClass.NamedVariable, "IED1LD0");
        var split = request.Length / 2;
        await stream.WriteAsync(TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(request.AsSpan(0, split), endOfTransmission: false)), timeout.Token);
        await stream.WriteAsync(TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(request.AsSpan(split), endOfTransmission: true)), timeout.Token);

        var response = await ReadCotpDataPayloadAsync(stream, timeout.Token);
        var names = MmsGetNameListResponseDecoder.Decode(response.UserData, expectedInvokeId: 41);

        Assert.True(response.SegmentCount > 1, $"Expected a segmented response, received {response.SegmentCount} TPDU.");
        Assert.True(names.IsSuccess, names.Message);
        Assert.True(names.MoreFollows);
        Assert.Equal(64, names.Names.Count);
        Assert.Contains("MMXU1", names.Names);
        Assert.Contains("MMXU1$ST", names.Names);
        Assert.Contains(server.RecentActivity(), activity =>
            activity.Operation == nameof(MmsReadOnlyOperation.GetNamedVariableDirectory) &&
            activity.Message.Contains("COTP segments=", StringComparison.Ordinal));

        await server.StopAsync();
    }

    [Fact]
    public async Task StopAsync_Completes_When_A_Client_Is_Connected_But_Idle()
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
        _ = await WaitForActivityAsync(server, a => a.Kind == IedSimulatorServerActivityKind.ClientConnected, timeout.Token);

        await server.StopAsync().WaitAsync(timeout.Token);

        Assert.False(server.IsRunning);
        Assert.Contains(server.RecentActivity(), a => a.Kind == IedSimulatorServerActivityKind.ServerStopped);
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

    private static async Task EstablishAssociationAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(TpktFrameCodec.Encode(CotpFrameCodec.EncodeDefaultConnectRequest()), cancellationToken);
        var cc = CotpFrameCodec.Decode(TpktFrameCodec.Decode(await ReadTpktFrameAsync(stream, cancellationToken)).Payload);
        Assert.True(cc.IsValid, cc.Message);
        Assert.Equal(CotpTpduKind.ConnectionConfirm, cc.Kind);

        await stream.WriteAsync(
            TpktFrameCodec.Encode(CotpFrameCodec.EncodeData(AcseMmsInitiateRequest.BuildDefaultAssociationPayload())),
            cancellationToken);
        var aare = await ReadCotpDataPayloadAsync(stream, cancellationToken);
        Assert.NotEmpty(aare.UserData);
    }

    private static async Task<(byte[] UserData, int SegmentCount)> ReadCotpDataPayloadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var segments = new List<byte[]>();
        while (true)
        {
            var tpkt = TpktFrameCodec.Decode(await ReadTpktFrameAsync(stream, cancellationToken));
            Assert.True(tpkt.IsValid, tpkt.Message);

            var data = CotpFrameCodec.Decode(tpkt.Payload);
            Assert.True(data.IsValid, data.Message);
            Assert.Equal(CotpTpduKind.Data, data.Kind);

            segments.Add(data.UserData);
            if (data.EndOfTransmission)
                return (segments.SelectMany(segment => segment).ToArray(), segments.Count);
        }
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
