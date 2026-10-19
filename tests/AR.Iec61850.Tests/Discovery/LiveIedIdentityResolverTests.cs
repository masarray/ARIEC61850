using AR.Iec61850.Binding;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Discovery;

public sealed class LiveIedIdentityResolverTests
{

    [Fact]
    public void Resolve_Five_LogicalDevices_Preserves_Full_Common_Ied_Name_Without_Scl()
    {
        var domains = new[] { "SIEBCUCTRL", "SIEBCUMEAS", "SIEBCUPROT", "SIEBCUUD1", "SIEBCUUD2" };
        var identity = LiveIedIdentityResolver.Resolve(domains, "192.0.2.20");

        Assert.Equal("SIEBCU", identity.IedName);
        Assert.Equal("MmsDomainCommonPrefix", identity.Source);
        Assert.NotEqual(LiveIedDiscoveryConfidenceLevel.High, identity.Confidence);
        Assert.NotEqual(LiveIedDiscoveryConfidenceLevel.Exact, identity.Confidence);
        Assert.Equal("CTRL", identity.LogicalDeviceAliases["SIEBCUCTRL"]);
        Assert.Equal("UD2", identity.LogicalDeviceAliases["SIEBCUUD2"]);
    }

    [Theory]
    [InlineData("BCU")]
    [InlineData("PROT")]
    [InlineData("CTRL")]
    [InlineData("MEAS")]
    [InlineData("MU")]
    public void Resolve_Common_Prefix_Does_Not_Remove_A_Valid_Ied_Name_Ending(string ending)
    {
        var fullName = "STATION" + ending;
        var identity = LiveIedIdentityResolver.Resolve(
            [fullName + "Application", fullName + "Reports", fullName + "Custom1"],
            "192.0.2.21");

        Assert.Equal(fullName, identity.IedName);
        Assert.NotEqual(LiveIedDiscoveryConfidenceLevel.High, identity.Confidence);
    }


    [Fact]
    public void Mms_Domain_Names_Are_Case_Sensitive_And_Never_Coalesced()
    {
        var identity = LiveIedIdentityResolver.Resolve(
            ["SIEBCUCTRL", "siebcuctrl", "SIEBCUMEAS"], "192.0.2.61");
        Assert.Equal(3, identity.LogicalDeviceAliases.Count);
        Assert.Contains("SIEBCUCTRL", identity.LogicalDeviceAliases.Keys);
        Assert.Contains("siebcuctrl", identity.LogicalDeviceAliases.Keys);
    }

    [Fact]
    public void Resolve_Preserves_Digits_In_A_Single_Ld0_Domain()
    {
        var identity = LiveIedIdentityResolver.Resolve(["OLSF501LD0"], "192.0.2.10");

        Assert.Equal("OLSF501", identity.IedName);
        Assert.Equal("MmsDomainKnownLogicalDeviceSuffix", identity.Source);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Medium, identity.Confidence);
        Assert.Equal("LD0", identity.LogicalDeviceAliases["OLSF501LD0"]);
    }

    [Fact]
    public void Resolve_Uses_All_Domains_To_Confirm_The_Ied_Name()
    {
        var identity = LiveIedIdentityResolver.Resolve(
            ["OLSF501LD0", "OLSF501PROT", "OLSF501CTRL"],
            "192.0.2.10");

        Assert.Equal("OLSF501", identity.IedName);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Medium, identity.Confidence);
        Assert.False(identity.IsAmbiguous);
        Assert.Equal("PROT", identity.LogicalDeviceAliases["OLSF501PROT"]);
    }

    [Fact]
    public void Resolve_Does_Not_Promote_A_Nested_Mu_Domain_To_The_Physical_Ied_Name()
    {
        var identity = LiveIedIdentityResolver.Resolve(
            [
                "OCR7SJ8Application",
                "OCR7SJ8CB1",
                "OCR7SJ8Dc1",
                "OCR7SJ8Mod2_MU1",
                "OCR7SJ8V3p1_5051OC3phase1"
            ],
            "1.110.1.1");

        Assert.Equal("OCR7SJ8", identity.IedName);
        Assert.Equal("MmsDomainCommonPrefix", identity.Source);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Medium, identity.Confidence);
        Assert.Equal("Mod2_MU1", identity.LogicalDeviceAliases["OCR7SJ8Mod2_MU1"]);
        Assert.DoesNotContain("OCR7SJ8Mod2", identity.CandidateNames);
    }

    [Fact]
    public void Resolve_Rejects_Conflicting_Domain_Candidates()
    {
        var identity = LiveIedIdentityResolver.Resolve(["ALPHA_LD0", "BETA_LD0"], "192.0.2.10");

        Assert.Equal("192.0.2.10", identity.IedName);
        Assert.Equal("MmsDomainAmbiguous", identity.Source);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Low, identity.Confidence);
        Assert.True(identity.IsAmbiguous);
        Assert.Equal(["ALPHA", "BETA"], identity.CandidateNames);
    }


    [Fact]
    public void Resolve_Fully_Known_Ld_Suffixes_Is_Still_Provisional_Without_Scl()
    {
        var identity = LiveIedIdentityResolver.Resolve(
            ["STATIONBCUCTRL", "STATIONBCUPROT", "STATIONBCUMEAS"],
            "192.0.2.20");
        Assert.Equal("STATIONBCU", identity.IedName);
        Assert.Equal("MmsDomainKnownLogicalDeviceSuffix", identity.Source);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Medium, identity.Confidence);
    }

    [Fact]
    public void Resolve_Explicit_Alias_Is_Operator_Supplied_Not_Wire_Verified()
    {
        var identity = LiveIedIdentityResolver.Resolve(
            ["WIRELD0"], "192.0.2.20", explicitIedName: "OPERATOR_ALIAS");
        Assert.Equal("OPERATOR_ALIAS", identity.IedName);
        Assert.Equal("ExplicitOverride", identity.Source);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Medium, identity.Confidence);
    }

    [Fact]
    public void Resolve_Uses_Explicit_Name_Over_Live_Heuristic()
    {
        var identity = LiveIedIdentityResolver.Resolve(["OLSF501LD0"], "192.0.2.10", explicitIedName: "COMMISSIONED_IED");

        Assert.Equal("COMMISSIONED_IED", identity.IedName);
        Assert.Equal("ExplicitOverride", identity.Source);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Medium, identity.Confidence);
        Assert.Equal("ExplicitOverride", identity.Source);
    }

    [Fact]
    public void Heuristic_Suffix_Is_Never_A_Verified_Identity_Even_With_Five_Lds()
    {
        var identity = LiveIedIdentityResolver.Resolve(
            ["PLANTCTRL", "PLANTMEAS", "PLANTPROT", "PLANTLD0", "PLANTMU1"], "192.0.2.90");
        Assert.Equal("PLANT", identity.IedName);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Medium, identity.Confidence);
        Assert.DoesNotContain(identity.CandidateNames, name => name.Equals("PLANTCTRL", StringComparison.Ordinal));
    }

    [Fact]
    public void Binding_Identity_Resolver_Uses_The_Live_Domain_Resolver()
    {
        var identity = Iec61850IdentityResolver.ResolveFromDomains(
            ["OLSF501LD0", "OLSF501LD1"],
            "192.0.2.10");

        Assert.Equal("OLSF501", identity.DisplayName);
        Assert.Equal("MmsDomainKnownLogicalDeviceSuffix", identity.Source);
        Assert.Equal("Medium", identity.Confidence);
        Assert.Equal("LD1", identity.LogicalDeviceAliases["OLSF501LD1"]);
    }

    [Fact]
    public void Builder_Stores_Smart_Ied_Identity_In_The_Discovery_Document()
    {
        var discovery = new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory(
            [
                new MmsFcResolvedPoint
                {
                    Domain = "OLSF501LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Mod.stVal",
                    MmsItemName = "LLN0$ST$Mod$stVal"
                }
            ])
        };

        var document = LiveIedModelDiscoveryBuilder.Build(discovery, new LiveIedModelDiscoveryBuildOptions { Host = "192.0.2.10" });

        Assert.Equal("OLSF501", document.IedName);
        Assert.Equal("OLSF501", document.IedIdentity.IedName);
        Assert.Equal("MmsDomainKnownLogicalDeviceSuffix", document.IedIdentity.Source);
    }
}
