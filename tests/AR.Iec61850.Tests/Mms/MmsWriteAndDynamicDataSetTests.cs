using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsWriteAndDynamicDataSetTests
{
    [Fact]
    public void BuildSingleVariableWrite_EncodesWriteServiceAndBooleanData()
    {
        var request = MmsWriteRequest.BuildSingleVariableWrite(
            7,
            new MmsObjectReference("LD0", "LLN0$BR$brcbA01$RptEna", "BR"),
            MmsDataValue.Boolean(true));

        var hex = Convert.ToHexString(request);

        Assert.True(hex.Contains("A5", StringComparison.OrdinalIgnoreCase));
        Assert.True(hex.Contains("4C4430", StringComparison.OrdinalIgnoreCase)); // LD0
        Assert.True(hex.Contains("4C4C4E30244252246272636241303124527074456E61", StringComparison.OrdinalIgnoreCase));
        Assert.True(hex.Contains("830101", StringComparison.OrdinalIgnoreCase)); // MMS boolean true
    }

    [Fact]
    public void BuildDefineNamedVariableList_EncodesDefineServiceAndMembers()
    {
        var request = MmsDefineNamedVariableListRequest.Build(
            8,
            "LD0/LLN0.AR_DYN_DS01",
            [
                new MmsObjectReference("LD0", "PTOC1$ST$Str$stVal", "ST"),
                new MmsObjectReference("LD0", "MMXU1$MX$PhV$phsA$cVal$mag$f", "MX")
            ]);

        var hex = Convert.ToHexString(request);

        Assert.True(hex.Contains("AB", StringComparison.OrdinalIgnoreCase));
        Assert.True(hex.Contains("41525F44594E5F44533031", StringComparison.OrdinalIgnoreCase)); // AR_DYN_DS01
        Assert.True(hex.Contains("50544F43312453542453747224737456616C", StringComparison.OrdinalIgnoreCase));
        Assert.True(hex.Contains("4D4D585531244D58245068562470687341246356616C246D61672466", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DecodeWriteResponse_AcceptsSuccessResult()
    {
        var mms = Convert.FromHexString("A107020109A5028100");
        var payload = MmsPresentation.WrapIsoPresentationPData(mms);

        var result = MmsWriteResponseDecoder.Decode(payload, 9);

        Assert.True(result.IsSuccess);
        Assert.Single(result.AccessResults);
    }

    [Fact]
    public void DecodeDefineNamedVariableListResponse_AcceptsNullResponse()
    {
        var mms = Convert.FromHexString("A10502010A8B00");
        var payload = MmsPresentation.WrapIsoPresentationPData(mms);

        var result = MmsDefineNamedVariableListResponseDecoder.Decode(payload, 10, "LD0/LLN0.AR_DYN_DS01");

        Assert.True(result.IsSuccess);
    }
}

public sealed class MmsInformationReportDecoderTests
{
    [Fact]
    public void DecodeInformationReport_DecodesAccessResults()
    {
        var mms = Convert.FromHexString("A305A003830101");
        var payload = MmsPresentation.WrapIsoPresentationPData(mms);

        Assert.True(MmsInformationReportDecoder.IsInformationReport(payload));

        var report = MmsInformationReportDecoder.Decode(payload);

        Assert.True(report.IsSuccess);
        var item = Assert.Single(report.Items);
        Assert.Equal(MmsDataKind.Boolean, item.Value?.Kind);
    }
}
