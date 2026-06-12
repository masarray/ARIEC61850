using System.Threading.Channels;
using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReceivePumpTests
{
    [Fact]
    public async Task Pump_CompletesPendingInvokeAndQueuesInformationReport()
    {
        var channel = Channel.CreateUnbounded<byte[]>();
        await channel.Writer.WriteAsync(BuildInformationReport());
        await channel.Writer.WriteAsync(BuildConfirmedReadResponse(invokeId: 42));

        var router = new MmsReceiveRouter();
        var pump = new MmsReceivePump(router, cancellationToken => channel.Reader.ReadAsync(cancellationToken).AsTask());

        using var pending = pump.RegisterConfirmedOperation(42);
        pump.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var response = await pending.WaitAsync(timeout.Token);
        await pump.StopAsync();

        Assert.Equal(MmsPduKind.ConfirmedResponse, response.Kind);
        Assert.Equal(42, response.InvokeId);
        Assert.Equal(2, pump.RoutedPduCount);
        Assert.Equal(1, pump.CompletedConfirmedCount);
        Assert.Equal(1, pump.QueuedInformationReportCount);
        Assert.True(router.TryDequeueInformationReport(out var report));
        Assert.True(report.IsInformationReport);
    }

    [Fact]
    public async Task Pump_RoutesMultipleConfirmedOperationsAroundReports()
    {
        var channel = Channel.CreateUnbounded<byte[]>();
        await channel.Writer.WriteAsync(BuildInformationReport());
        await channel.Writer.WriteAsync(BuildConfirmedReadResponse(invokeId: 10));
        await channel.Writer.WriteAsync(BuildInformationReport());
        await channel.Writer.WriteAsync(BuildConfirmedReadResponse(invokeId: 11));

        var router = new MmsReceiveRouter();
        var pump = new MmsReceivePump(router, cancellationToken => channel.Reader.ReadAsync(cancellationToken).AsTask());

        using var first = pump.RegisterConfirmedOperation(10);
        using var second = pump.RegisterConfirmedOperation(11);
        pump.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var firstResponse = await first.WaitAsync(timeout.Token);
        var secondResponse = await second.WaitAsync(timeout.Token);
        await pump.StopAsync();

        Assert.Equal(MmsPduKind.ConfirmedResponse, firstResponse.Kind);
        Assert.Equal(10, firstResponse.InvokeId);
        Assert.Equal(MmsPduKind.ConfirmedResponse, secondResponse.Kind);
        Assert.Equal(11, secondResponse.InvokeId);
        Assert.Equal(4, pump.RoutedPduCount);
        Assert.Equal(2, pump.CompletedConfirmedCount);
        Assert.Equal(2, pump.QueuedInformationReportCount);
        Assert.True(router.TryDequeueInformationReport(out var firstReport));
        Assert.True(router.TryDequeueInformationReport(out var secondReport));
        Assert.True(firstReport.IsInformationReport);
        Assert.True(secondReport.IsInformationReport);
    }

    [Fact]
    public async Task Pump_FaultsPendingInvokeWhenReaderFails()
    {
        var router = new MmsReceiveRouter();
        var pump = new MmsReceivePump(router, _ => throw new IOException("scripted reader failure"));

        using var pending = pump.RegisterConfirmedOperation(7);
        pump.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var ex = await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(timeout.Token));
        await pump.StopAsync();

        Assert.Contains("scripted reader failure", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IOException", pump.LastFaultMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, pump.PendingOperationCount);
    }

    private static byte[] BuildConfirmedReadResponse(int invokeId)
    {
        var data = MmsDataCodec.Encode(MmsDataValue.Boolean(true));
        var listOfAccessResult = BerWriter.EncodeTlv(0xA1, data);
        var readService = BerWriter.EncodeTlv(0xA4, listOfAccessResult);
        var mms = BerWriter.EncodeTlv(
            0xA1,
            Integer(invokeId)
                .Concat(readService)
                .ToArray());

        return MmsPresentation.WrapIsoPresentationPData(mms);
    }

    private static byte[] BuildInformationReport()
    {
        var variableAccessSpecification = BerWriter.EncodeTlv(
            0xA1,
            BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii("LD0"))
                .Concat(BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii("LLN0$Events")))
                .ToArray());
        var listOfAccessResult = BerWriter.EncodeTlv(0xA0, MmsDataCodec.Encode(MmsDataValue.Boolean(true)));
        var informationReport = BerWriter.EncodeTlv(
            0xA0,
            variableAccessSpecification
                .Concat(listOfAccessResult)
                .ToArray());
        var mms = BerWriter.EncodeTlv(0xA3, informationReport);

        return MmsPresentation.WrapIsoPresentationPData(mms);
    }

    private static byte[] Integer(int value)
    {
        if (value <= 0x7F)
            return [0x02, 0x01, (byte)value];

        if (value <= 0xFF)
            return [0x02, 0x02, 0x00, (byte)value];

        return [0x02, 0x02, (byte)(value >> 8), (byte)value];
    }
}
