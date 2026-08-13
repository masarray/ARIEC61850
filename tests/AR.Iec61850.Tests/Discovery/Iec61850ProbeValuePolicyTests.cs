using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

// Type-family compatibility is covered end-to-end by Iec61850DesignLiveReconcilerTests.
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
    public void Existing_Primary_Role_Is_Always_Value_Bearing()
    {
        var attribute = new LiveIedResolvedDataSetAttributeModel
        {
            Reference = "IEDLD0/GGIO1.CustomValue",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue
        };

        Assert.True(Iec61850ProbeValuePolicy.IsPrimaryValueBearing(attribute));
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

    [Theory]
    [InlineData("IEDLD0/MMXU1$MX$A$phsA$cVal$mag$f", "IEDLD0/MMXU1$MX$A$phsA$instCVal$mag$f", Iec61850AlternateReferenceStrategyKind.ComplexValueInstantaneousSibling)]
    [InlineData("IEDLD0/MMXU1$MX$A$phsA$instCVal$mag$f", "IEDLD0/MMXU1$MX$A$phsA$cVal$mag$f", Iec61850AlternateReferenceStrategyKind.ComplexValueInstantaneousSibling)]
    [InlineData("IEDLD0/MMXU1$MX$TotW$mag$f", "IEDLD0/MMXU1$MX$TotW$instMag$f", Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling)]
    [InlineData("IEDLD0/MMXU1$MX$TotW$instMag$f", "IEDLD0/MMXU1$MX$TotW$mag$f", Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling)]
    public void Alternate_Reference_Policy_Uses_Known_Measurement_Siblings(
        string canonical,
        string expected,
        Iec61850AlternateReferenceStrategyKind strategy)
    {
        var candidate = Assert.Single(Iec61850AlternateReferencePolicy.GetCandidates(canonical));

        Assert.Equal(canonical, candidate.CanonicalMmsReference);
        Assert.Equal(expected, candidate.MmsReference);
        Assert.Equal(strategy, candidate.Strategy);
    }

    [Fact]
    public void Complex_Alternate_Does_Not_Create_InstMag_Inside_CVal()
    {
        var candidate = Assert.Single(Iec61850AlternateReferencePolicy.GetCandidates(
            "RPRE_CurrentMeasurements/MMXU1$MX$A$phsA$cVal$mag$f"));

        Assert.Equal("RPRE_CurrentMeasurements/MMXU1$MX$A$phsA$instCVal$mag$f", candidate.MmsReference);
        Assert.DoesNotContain("$cVal$instMag$f", candidate.MmsReference, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_Measurement_Target_Has_No_Alternate()
        => Assert.Empty(Iec61850AlternateReferencePolicy.GetCandidates("IEDLD0/GGIO1$ST$Ind1$stVal"));
}
