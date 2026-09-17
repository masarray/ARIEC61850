using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsDataSetCompletenessRegressionTests
{
    [Fact]
    public void Decode_PreservesExactMemberOrderAndMultiplicity()
    {
        var first = BuildMember("LD0", "GGIO1$ST$Ind1$stVal");
        var second = BuildMember("LD0", "MMXU1$MX$A$phsA$cVal$mag$f");
        var repeatedFirst = BuildMember("LD0", "GGIO1$ST$Ind1$stVal");

        var listOfVariable = BerWriter.EncodeTlv(
            0xA1,
            first.Concat(second).Concat(repeatedFirst).ToArray());
        var service = BerWriter.EncodeTlv(
            0xAC,
            BerWriter.EncodeTlv(0x80, [0x00])
                .Concat(listOfVariable)
                .ToArray());
        var response = BerWriter.EncodeTlv(
            0xA1,
            new byte[] { 0x02, 0x01, 0x01 }
                .Concat(service)
                .ToArray());

        var result = MmsDataSetDirectoryResponseDecoder.Decode(
            response,
            expectedInvokeId: 1,
            dataSetReference: "LD0/LLN0.Events");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(3, result.Members.Count);
        Assert.Equal(
            [
                "LD0/GGIO1$ST$Ind1$stVal",
                "LD0/MMXU1$MX$A$phsA$cVal$mag$f",
                "LD0/GGIO1$ST$Ind1$stVal"
            ],
            result.Members.Select(member => member.MmsReference).ToArray());
    }

    private static byte[] BuildMember(string domain, string item)
    {
        var memberObjectName = BerWriter.EncodeTlv(
            0xA1,
            BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii(domain))
                .Concat(BerWriter.EncodeTlv(0x1A, BerWriter.EncodeAscii(item)))
                .ToArray());

        return BerWriter.EncodeTlv(
            0x30,
            BerWriter.EncodeTlv(0xA0, memberObjectName));
    }
}
