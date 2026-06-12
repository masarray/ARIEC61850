using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReceiveRouterTests
{
    [Fact]
    public void DecodeEnvelope_ClassifiesConfirmedResponseWithInvokeId()
    {
        var payload = BuildConfirmedReadResponse(invokeId: 7);

        var envelope = MmsPduEnvelope.Decode(payload);

        Assert.Equal(MmsPduKind.ConfirmedResponse, envelope.Kind);
        Assert.Equal(7, envelope.InvokeId);
        Assert.True(envelope.IsConfirmedServiceResult);
        Assert.False(envelope.IsInformationReport);
    }

    [Fact]
    public void DecodeEnvelope_ClassifiesInformationReport()
    {
        var payload = BuildInformationReport();

        var envelope = MmsPduEnvelope.Decode(payload);

        Assert.Equal(MmsPduKind.Unconfirmed, envelope.Kind);
        Assert.True(envelope.IsInformationReport);
        Assert.Null(envelope.InvokeId);
    }

    [Fact]
    public void DecodeEnvelope_ClassifiesConfirmedErrorWithContextInvokeId()
    {
        var serviceError = BerWriter.EncodeTlv(0xA2, BerWriter.EncodeTlv(0x80, [0x01]));
        var mms = BerWriter.EncodeTlv(
            0xA2,
            BerWriter.EncodeTlv(0x80, [0x09])
                .Concat(serviceError)
                .ToArray());
        var payload = MmsPresentation.WrapIsoPresentationPData(mms);

        var envelope = MmsPduEnvelope.Decode(payload);

        Assert.Equal(MmsPduKind.ConfirmedError, envelope.Kind);
        Assert.Equal(9, envelope.InvokeId);
        Assert.True(envelope.IsConfirmedServiceResult);
    }

    [Fact]
    public void Route_QueuesInformationReportSeparatelyFromConfirmedResponse()
    {
        var router = new MmsReceiveRouter();

        var reportRoute = router.Route(BuildInformationReport());
        var responseRoute = router.Route(BuildConfirmedReadResponse(invokeId: 12));

        Assert.Equal(MmsReceiveRouteAction.QueuedInformationReport, reportRoute.Action);
        Assert.Equal(MmsReceiveRouteAction.QueuedConfirmedResult, responseRoute.Action);
        Assert.Equal(1, router.QueuedInformationReportCount);
        Assert.Equal(1, router.QueuedConfirmedResultCount);

        Assert.True(router.TryDequeueInformationReport(out var report));
        Assert.True(report.IsInformationReport);

        Assert.True(router.TryDequeueConfirmedResult(12, out var response));
        Assert.Equal(MmsPduKind.ConfirmedResponse, response.Kind);
        Assert.Equal(12, response.InvokeId);
    }

    [Fact]
    public void Route_KeepsOtherInvokeUntilOwnerRequestsIt()
    {
        var router = new MmsReceiveRouter();

        router.Route(BuildConfirmedReadResponse(invokeId: 22));

        Assert.False(router.TryDequeueConfirmedResult(21, out _));
        Assert.True(router.TryDequeueConfirmedResult(22, out var response));
        Assert.Equal(22, response.InvokeId);
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
