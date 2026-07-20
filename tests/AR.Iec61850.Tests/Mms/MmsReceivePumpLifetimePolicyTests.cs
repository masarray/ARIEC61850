using System.Threading.Channels;
using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReceivePumpLifetimePolicyTests
{
    [Fact]
    public async Task Rebind_Detaches_Pump_From_Completed_Connect_Operation_Token()
    {
        var channel = Channel.CreateUnbounded<byte[]>();
        var router = new MmsReceiveRouter();
        var pump = new MmsReceivePump(
            router,
            cancellationToken => channel.Reader.ReadAsync(cancellationToken).AsTask());
        using var connectOperation = new CancellationTokenSource();

        pump.Start(connectOperation.Token);
        await MmsReceivePumpLifetimePolicy.RebindAsync(pump);
        connectOperation.Cancel();

        using var pending = pump.RegisterConfirmedOperation(27);
        await channel.Writer.WriteAsync(BuildConfirmedReadResponse(27));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var response = await pending.WaitAsync(timeout.Token);

        Assert.True(pump.IsRunning);
        Assert.Equal(MmsPduKind.ConfirmedResponse, response.Kind);
        Assert.Equal(27, response.InvokeId);

        await pump.StopAsync();
    }

    private static byte[] BuildConfirmedReadResponse(int invokeId)
    {
        var data = MmsDataCodec.Encode(MmsDataValue.Boolean(true));
        var listOfAccessResult = BerWriter.EncodeTlv(0xA1, data);
        var readService = BerWriter.EncodeTlv(0xA4, listOfAccessResult);
        var mms = BerWriter.EncodeTlv(
            0xA1,
            BerWriter.EncodeTlv(0x02, BerWriter.EncodeSignedInteger(invokeId))
                .Concat(readService)
                .ToArray());

        return MmsPresentation.WrapIsoPresentationPData(mms);
    }
}
