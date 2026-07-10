using System.Net;
using System.Net.Sockets;
using AR.Iec61850.Acse;
using AR.Iec61850.Mms;
using AR.Iec61850.Osi;
using AR.Iec61850.Scl;
using AR.Iec61850.Simulation;
using AR.Iec61850.Tests.Scl;

namespace AR.Iec61850.Tests.Simulation;

public sealed class MmsVirtualIedServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Server_Serves_Discovery_Read_And_Rejects_Write_Over_Tcp()
    {
        var serverProfile = BuildServerProfileFromScl();
        await using var server = new MmsVirtualIedServer(serverProfile, new MmsVirtualIedServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0
        });

        server.Start();
        Assert.True(server.IsRunning);

        using var cts = new CancellationTokenSource(Timeout);
        using var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(IPAddress.Loopback, server.BoundPort, cts.Token);
        await using var stream = tcp.GetStream();

        // COTP connect.
        await SendAsync(stream, CotpFrameCodec.EncodeDefaultConnectRequest(), cts.Token);
        var cc = CotpFrameCodec.Decode(TpktFrameCodec.Decode(await ReadFrameAsync(stream, cts.Token)).Payload);
        Assert.Equal(CotpTpduKind.ConnectionConfirm, cc.Kind);

        // ACSE associate.
        await SendCotpDataAsync(stream, AcseMmsInitiateRequest.BuildDefaultAssociationPayload(), cts.Token);
        var aare = CotpFrameCodec.Decode(TpktFrameCodec.Decode(await ReadFrameAsync(stream, cts.Token)).Payload);
        Assert.Equal(CotpTpduKind.Data, aare.Kind);

        var probes = MmsConfirmedRequestBerProfileBuilder.CreateDefaultProbes(serverProfile);

        var domainProbe = probes.First(p => p.Kind == MmsConfirmedBerProbeKind.GetDomainDirectory);
        var domainResponse = await ExchangeAsync(stream, domainProbe.PresentationPayload, cts.Token);
        var domains = MmsGetNameListResponseDecoder.Decode(domainResponse, domainProbe.InvokeId);
        Assert.True(domains.IsSuccess);
        Assert.Contains(domains.Names, n => n.Contains("MU01LD0", StringComparison.OrdinalIgnoreCase));

        var readProbe = probes.First(p => p.Kind == MmsConfirmedBerProbeKind.Read);
        var readResponse = await ExchangeAsync(stream, readProbe.PresentationPayload, cts.Token);
        var read = MmsReadResponseDecoder.DecodeSingleVariable(readResponse, readProbe.InvokeId);
        Assert.True(read.IsSuccess);

        var writeProbe = probes.First(p => p.Kind == MmsConfirmedBerProbeKind.Write);
        var writeResponse = await ExchangeAsync(stream, writeProbe.PresentationPayload, cts.Token);
        var write = MmsWriteResponseDecoder.Decode(writeResponse, writeProbe.InvokeId);
        Assert.False(write.IsSuccess); // read-only guard rejects the write

        await server.StopAsync();

        Assert.True(server.AcceptedConnectionCount >= 1);
        Assert.True(server.RequestCount >= 3);
        Assert.True(server.SuccessCount >= 2);
        Assert.True(server.FailureCount >= 1);
    }

    [Fact]
    public async Task Server_Survives_Client_That_Disconnects_Mid_Session()
    {
        var serverProfile = BuildServerProfileFromScl();
        await using var server = new MmsVirtualIedServer(serverProfile, new MmsVirtualIedServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0
        });
        server.Start();

        using var cts = new CancellationTokenSource(Timeout);

        // First client connects then drops immediately after COTP connect.
        using (var dropper = new TcpClient { NoDelay = true })
        {
            await dropper.ConnectAsync(IPAddress.Loopback, server.BoundPort, cts.Token);
            await using var dropStream = dropper.GetStream();
            await SendAsync(dropStream, CotpFrameCodec.EncodeDefaultConnectRequest(), cts.Token);
            _ = await ReadFrameAsync(dropStream, cts.Token);
        }

        // A second, well-behaved client must still be served.
        using var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(IPAddress.Loopback, server.BoundPort, cts.Token);
        await using var stream = tcp.GetStream();
        await SendAsync(stream, CotpFrameCodec.EncodeDefaultConnectRequest(), cts.Token);
        var cc = CotpFrameCodec.Decode(TpktFrameCodec.Decode(await ReadFrameAsync(stream, cts.Token)).Payload);
        Assert.Equal(CotpTpduKind.ConnectionConfirm, cc.Kind);

        await server.StopAsync();
        Assert.True(server.AcceptedConnectionCount >= 2);
    }

    private static MmsReadOnlyServerProfile BuildServerProfileFromScl()
    {
        var document = new SclParser().Load(SclParserTests.MinimalStationPath());
        var simulatorProfile = new IedSimulatorProfileBuilder().FromScl(document).Profile;
        return new MmsReadOnlyServerModelBuilder().Build(simulatorProfile);
    }

    private static async Task<byte[]> ExchangeAsync(NetworkStream stream, byte[] presentationPayload, CancellationToken ct)
    {
        await SendCotpDataAsync(stream, presentationPayload, ct);
        var response = CotpFrameCodec.Decode(TpktFrameCodec.Decode(await ReadFrameAsync(stream, ct)).Payload);
        Assert.Equal(CotpTpduKind.Data, response.Kind);
        return response.UserData;
    }

    private static Task SendCotpDataAsync(NetworkStream stream, byte[] payload, CancellationToken ct)
        => SendAsync(stream, CotpFrameCodec.EncodeData(payload), ct);

    private static async Task SendAsync(NetworkStream stream, byte[] cotpPayload, CancellationToken ct)
    {
        var frame = TpktFrameCodec.Encode(cotpPayload);
        await stream.WriteAsync(frame, ct);
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = await ReadExactAsync(stream, 4, ct);
        var length = (header[2] << 8) | header[3];
        var payload = await ReadExactAsync(stream, length - 4, ct);
        var frame = new byte[length];
        Buffer.BlockCopy(header, 0, frame, 0, 4);
        Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
        return frame;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }

        return buffer;
    }
}
