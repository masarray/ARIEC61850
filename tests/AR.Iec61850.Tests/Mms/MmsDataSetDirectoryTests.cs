using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsDataSetDirectoryTests
{
    [Fact]
    public void ParseDataSetReference_ConvertsIecReferenceToMmsNamedVariableListName()
    {
        var request = MmsDataSetDirectoryRequest.Build(1, "LD0/LLN0.Events");

        Assert.NotEmpty(request);
        Assert.Contains((byte)'L', request);
        Assert.Contains((byte)'D', request);
        Assert.Contains((byte)'E', request);
    }

    [Fact]
    public void Decode_MapsMembersBackToLiveDirectoryPoints()
    {
        var directory = new MmsIedModelDirectory([
            new MmsFcResolvedPoint
            {
                Domain = "LD0",
                LogicalNode = "GGIO1",
                FunctionalConstraint = "ST",
                DataObjectPath = "Ind1.stVal",
                MmsItemName = "GGIO1$ST$Ind1$stVal",
                Confidence = 100
            }
        ]);

        var memberObjectName = BerWriter.EncodeTlv(0xA1,
            BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii("LD0"))
                .Concat(BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii("GGIO1$ST$Ind1$stVal")))
                .ToArray());
        var service = BerWriter.EncodeTlv(0xAC,
            BerWriter.EncodeTlv(0x80, [0x00])
                .Concat(memberObjectName)
                .ToArray());
        var response = BerWriter.EncodeTlv(0xA1,
            new byte[] { 0x02, 0x01, 0x01 }
                .Concat(service)
                .ToArray());

        var result = MmsDataSetDirectoryResponseDecoder.Decode(response, 1, "LD0/LLN0.Events", directory);

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(result.IsDeletable);
        var member = Assert.Single(result.Members);
        Assert.Equal("LD0/GGIO1.Ind1.stVal", member.UserReference);
        Assert.Equal("ST", member.FunctionalConstraint);
        Assert.Equal("LD0/GGIO1$ST$Ind1$stVal", member.MmsReference);
    }
}
