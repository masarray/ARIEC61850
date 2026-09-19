using AR.Iec61850.Discovery;
using Xunit;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850StandardModelRegistryTests
{
    [Theory]
    [InlineData("TCTR", "ARtg", "ASG")]
    [InlineData("TCTR", "Rat", "ASG")]
    [InlineData("TCTR", "AmpSv", "SAV")]
    [InlineData("TCTR", "AccMeas", "ING")]
    [InlineData("TCTR", "Mod", "ENC")]
    [InlineData("TCTR", "Beh", "ENS")]
    [InlineData("TVTR", "VRtg", "ASG")]
    [InlineData("TVTR", "VolSv", "SAV")]
    [InlineData("TVTR", "AccPro", "ING")]
    [InlineData("LTIM", "TmChgDT", "TSG")]
    [InlineData("LTIM", "TmUseDT", "SPG")]
    [InlineData("LTIM", "TmOfsTmm", "ING")]
    [InlineData("XCBR", "EEName", "DPL")]
    [InlineData("XSWI", "EEName", "DPL")]
    [InlineData("LLN0", "Mod", "ENC")]
    [InlineData("LLN0", "Beh", "ENS")]
    [InlineData("LLN0", "Health", "ENS")]
    [InlineData("GGIO", "Mod", "ENC")]
    [InlineData("MMXU", "Beh", "ENS")]
    [InlineData("XSWI", "Health", "ENS")]
    [InlineData("LLN0", "GrRef", "ORG")]
    [InlineData("LPHD", "PhyHealth", "ENS")]
    [InlineData("LLN0", "MltLev", "SPG")]
    [InlineData("MHAI", "ThdA", "WYE")]
    [InlineData("MHAI", "ThdPhV", "WYE")]
    [InlineData("LTRK", "BrcbTrk", "BTS")]
    [InlineData("LTRK", "UrcbTrk", "UTS")]
    [InlineData("LTRK", "GenTrk", "CST")]
    [InlineData("LTRK", "SgcbTrk", "STS")]
    [InlineData("LTRK", "DpcTrk", "CTS")]
    public void Registry_Resolves_Standard_Physical_Model_DataObjects(
        string lnClass,
        string dataObject,
        string expectedCdc)
    {
        Assert.True(Iec61850StandardModelRegistry.TryResolve(lnClass, dataObject, out var definition));
        Assert.Equal(expectedCdc, definition.Cdc);
        Assert.True(definition.Confidence >= 0.95);
    }

    [Fact]
    public void CdcInference_Uses_Standard_Tctr_Registry_Before_Heuristics()
    {
        var result = CdcInferenceEngine.Infer(
            "TCTR",
            "ARtg",
            Array.Empty<string>(),
            ["CF"]);

        Assert.Equal("ASG", result.Cdc);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.High, result.Level);
        Assert.Contains(result.Evidence, item => item.Contains("standard registry match", StringComparison.OrdinalIgnoreCase));
    }
}
