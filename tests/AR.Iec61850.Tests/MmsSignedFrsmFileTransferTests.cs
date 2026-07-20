using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests;

public sealed class MmsSignedFrsmFileTransferTests
{
    [Fact]
    public void FileOpenResponse_AcceptsNegativeSignedFrsmIdentifier()
    {
        var response = BuildConfirmedResponse(
            invokeId: 31,
            serviceTag: 72,
            constructed: true,
            serviceValue: BerWriter.EncodeTlv(
                BerClass.ContextSpecific,
                constructed: false,
                tagNumber: 0,
                BerWriter.EncodeSignedInteger(-17)));

        var result = MmsInteroperableFileOpenResponseDecoder.Decode(response, expectedInvokeId: 31);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(-17, result.FileReadStateMachineId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-17)]
    [InlineData(int.MinValue)]
    [InlineData(23)]
    public void FileReadRequest_EchoesCompleteSignedInteger32Range(int frsmId)
    {
        var request = MmsInteroperableFileReadRequest.Build(32, frsmId);
        var service = ReadService(request, expectedTag: 73);

        Assert.Equal((long)frsmId, BerReader.ReadSignedInteger(service));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-17)]
    [InlineData(int.MinValue)]
    [InlineData(23)]
    public void FileCloseRequest_EchoesCompleteSignedInteger32Range(int frsmId)
    {
        var request = MmsInteroperableFileCloseRequest.Build(33, frsmId);
        var service = ReadService(request, expectedTag: 74);

        Assert.Equal((long)frsmId, BerReader.ReadSignedInteger(service));
    }

    private static BerTlv ReadService(byte[] request, int expectedTag)
    {
        var mms = MmsPresentation.StripPresentationPrefix(request);
        var offset = 0;
        Assert.True(BerReader.TryReadTlv(mms, ref offset, out var outer));
        var children = BerReader.ReadChildren(outer.Value);
        var service = Assert.Single(children.Skip(1));
        Assert.Equal(BerClass.ContextSpecific, service.Class);
        Assert.Equal(expectedTag, service.TagNumber);
        return service;
    }

    private static byte[] BuildConfirmedResponse(
        int invokeId,
        int serviceTag,
        bool constructed,
        byte[] serviceValue)
    {
        var service = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            constructed,
            serviceTag,
            serviceValue);
        var confirmedResponse = BerWriter.EncodeTlv(
            0xA1,
            MmsPresentation.Concat(MmsPresentation.Integer(invokeId), service));
        return MmsPresentation.WrapIsoPresentationPData(confirmedResponse);
    }
}
