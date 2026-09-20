using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class CdcInferenceTypeAuthorityTests
{
    [Fact]
    public void ExactIntegerStatusType_UsesInsInsteadOfBooleanShapeGuess()
    {
        var attributes = StatusTriplet("INT32", "integer");

        var result = CdcInferenceEngine.Infer("GGIO", "CBClsCounter", attributes);

        Assert.Equal("INS", result.Cdc);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.High, result.Level);
        Assert.Contains(result.Evidence, item =>
            item.Contains("exact stVal type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExactBooleanStatusType_UsesSps()
    {
        var attributes = StatusTriplet("BOOLEAN", "boolean");

        var result = CdcInferenceEngine.Infer("GGIO", "BinaryStatus", attributes);

        Assert.Equal("SPS", result.Cdc);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.High, result.Level);
    }

    [Fact]
    public void HeuristicIntegerLabel_DoesNotOverrideExistingConservativeStatusInference()
    {
        var attributes = StatusTriplet(
            "INT32",
            "integer",
            LiveIedDiscoveryConfidenceLevel.Low);

        var result = CdcInferenceEngine.Infer("GGIO", "UnprovenStatus", attributes);

        Assert.Equal("SPS", result.Cdc);
    }

    private static LiveIedDataAttributeModel[] StatusTriplet(
        string sclBType,
        string mmsType,
        LiveIedDiscoveryConfidenceLevel confidence = LiveIedDiscoveryConfidenceLevel.Exact)
        =>
        [
            new LiveIedDataAttributeModel
            {
                AttributePath = "stVal",
                FunctionalConstraint = "ST",
                SclBType = sclBType,
                MmsType = mmsType,
                TypeConfidence = confidence,
                TypeDiscoveryStatus = confidence == LiveIedDiscoveryConfidenceLevel.Exact ? "Exact" : "NotRead"
            },
            new LiveIedDataAttributeModel
            {
                AttributePath = "q",
                FunctionalConstraint = "ST",
                SclBType = "Quality",
                MmsType = "bit-string",
                TypeConfidence = confidence
            },
            new LiveIedDataAttributeModel
            {
                AttributePath = "t",
                FunctionalConstraint = "ST",
                SclBType = "Timestamp",
                MmsType = "utc-time",
                TypeConfidence = confidence
            }
        ];
}
