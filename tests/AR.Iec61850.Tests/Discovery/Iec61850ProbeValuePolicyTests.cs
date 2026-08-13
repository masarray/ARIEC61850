using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850ProbeValuePolicyTests
{
    [Theory]
    [InlineData("IEDLD0/PTRC1.Op.general", true)]
    [InlineData("IEDLD0/XCBR1.Pos.stVal", true)]
    [InlineData("IEDLD0/YPTR1.TapPos.posVal", true)]
    [InlineData("IEDLD0/MMTR1.SupWh.actVal", true)]
    [InlineData("IEDLD0/MMXU1.TotW.mag.f", true)]
    [InlineData("IEDLD0/MMXU1.A.phsA.cVal.mag.f", true)]
    [InlineData("IEDLD0/MMXU1.PhV.phsA.cVal.ang.f", true)]
    [InlineData("IEDLD0/MMXU1.TotW.q", false)]
    [InlineData("IEDLD0/MMXU1.TotW.t", false)]
    public void Structural_Value_Policy_Covers_Operational_Status_And_Measurement(string reference, bool expected)
    {
        var role = reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase)
            ? Iec61850DataAttributeSemanticRole.Quality
            : reference.EndsWith(".t", StringComparison.OrdinalIgnoreCase)
                ? Iec61850DataAttributeSemanticRole.Timestamp
                : Iec61850DataAttributeSemanticRole.Other;

        var attribute = new LiveIedResolvedDataSetAttributeModel
        {
            Reference = reference,
            SemanticRole = role
        };

        Assert.Equal(expected, Iec61850ProbeValuePolicy.IsPrimaryValueBearing(attribute));
    }

    [Fact]
    public void Frozen_Value_Is_Not_Default_Primary_Probe_Target()
    {
        var attribute = new LiveIedResolvedDataSetAttributeModel
        {
            Reference = "IEDLD0/MMTR1.SupWh.frVal",
            SemanticRole = Iec61850DataAttributeSemanticRole.FrozenValue
        };

        Assert.False(Iec61850ProbeValuePolicy.IsPrimaryValueBearing(attribute));
    }
}