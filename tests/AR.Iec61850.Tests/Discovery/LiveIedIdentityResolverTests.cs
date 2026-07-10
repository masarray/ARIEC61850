using AR.Iec61850.Binding;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Discovery;

public sealed class LiveIedIdentityResolverTests
{
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
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.High, identity.Confidence);
        Assert.False(identity.IsAmbiguous);
        Assert.Equal("PROT", identity.LogicalDeviceAliases["OLSF501PROT"]);
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
    public void Resolve_Uses_Explicit_Name_Over_Live_Heuristic()
    {
        var identity = LiveIedIdentityResolver.Resolve(["OLSF501LD0"], "192.0.2.10", explicitIedName: "COMMISSIONED_IED");

        Assert.Equal("COMMISSIONED_IED", identity.IedName);
        Assert.Equal("ExplicitOverride", identity.Source);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Exact, identity.Confidence);
    }

    [Fact]
    public void Binding_Identity_Resolver_Uses_The_Live_Domain_Resolver()
    {
        var identity = Iec61850IdentityResolver.ResolveFromDomains(
            ["OLSF501LD0", "OLSF501LD1"],
            "192.0.2.10");

        Assert.Equal("OLSF501", identity.DisplayName);
        Assert.Equal("MmsDomainKnownLogicalDeviceSuffix", identity.Source);
        Assert.Equal("High", identity.Confidence);
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
