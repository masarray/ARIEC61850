using AR.Iec61850.Discovery;
using System.Xml.Linq;

namespace AR.Iec61850.Tests.Discovery;

/// <summary>
/// Independently constructed IEC 61850 domain/SCL topology; no device capture or external-client fixture.
/// </summary>
public sealed class GlobalIdentityEvidenceRegressionTests
{
    private static readonly string[] Domains =
    [
        "SIEBCUCTRL", "SIEBCUMEAS", "SIEBCUPROT", "SIEBCUUD1", "SIEBCUUD2"
    ];

    [Fact]
    public void MultipleDomains_NeverStripAnUnprovenSuffixFromCommonIdentity()
    {
        var inferred = LiveIedIdentityResolver.Resolve(Domains, "192.0.2.20");
        Assert.Equal("SIEBCU", inferred.IedName);
        Assert.Equal("MmsDomainCommonPrefix", inferred.Source);
        Assert.NotEqual(LiveIedDiscoveryConfidenceLevel.High, inferred.Confidence);
        Assert.NotEqual(LiveIedDiscoveryConfidenceLevel.Exact, inferred.Confidence);
        Assert.Equal("CTRL", inferred.LogicalDeviceAliases["SIEBCUCTRL"]);
        Assert.Equal("UD2", inferred.LogicalDeviceAliases["SIEBCUUD2"]);
        Assert.Equal(Domains.Length, inferred.LogicalDeviceAliases.Count);
    }

    [Fact]
    public void DomainSuffixHeuristic_MustNotClaimVerifiedWireIdentity()
    {
        var inferred = LiveIedIdentityResolver.Resolve(
            ["LABCTRLLD0", "LABCTRLPROT", "LABCTRLMEAS"], "192.0.2.20");
        Assert.Equal("LABCTRL", inferred.IedName);
        Assert.NotEqual(LiveIedDiscoveryConfidenceLevel.High, inferred.Confidence);
        Assert.NotEqual(LiveIedDiscoveryConfidenceLevel.Exact, inferred.Confidence);
        Assert.Equal("MmsDomainKnownLogicalDeviceSuffix", inferred.Source);
    }

    [Fact]
    public void OperatorDisplayAlias_IsNotWireVerified()
    {
        var inferred = LiveIedIdentityResolver.Resolve(Domains, "192.0.2.20",
            explicitIedName: "OPERATOR_LABEL");
        Assert.Equal("OPERATOR_LABEL", inferred.IedName);
        Assert.Equal("ExplicitOverride", inferred.Source);
        Assert.NotEqual(LiveIedDiscoveryConfidenceLevel.High, inferred.Confidence);
        Assert.NotEqual(LiveIedDiscoveryConfidenceLevel.Exact, inferred.Confidence);
    }

    [Fact]
    public void UniqueSclTopology_PromotesFullName_AndWrongEndpointOrApFailsClosed()
    {
        var verified = TrustedSclIedIdentityMatcher.TryMatch(
            SyntheticScl(), Domains, "192.0.2.20", "SIEBCU");
        Assert.NotNull(verified);
        Assert.Equal("SIEBCU", verified.IedName);
        Assert.Equal("TrustedSclExactDomainMatch", verified.Source);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.High, verified.Confidence);
        Assert.Equal("UD1", verified.LogicalDeviceAliases["SIEBCUUD1"]);

        Assert.Null(TrustedSclIedIdentityMatcher.TryMatch(
            SyntheticScl(), Domains, "192.0.2.21", "SIEBCU"));
        Assert.Null(TrustedSclIedIdentityMatcher.TryMatch(
            SyntheticScl(), Domains[..4], "192.0.2.20", "SIEBCU"));
        Assert.Null(TrustedSclIedIdentityMatcher.TryMatch(
            SyntheticScl(), Domains, "192.0.2.20", "ANOTHER"));

        var ambiguous = SyntheticScl();
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        var ied = Assert.Single(ambiguous.Descendants(ns + "IED"));
        var duplicate = new XElement(Assert.Single(ied.Elements(ns + "AccessPoint")));
        duplicate.SetAttributeValue("name", "P2");
        ied.Add(duplicate);
        Assert.Null(TrustedSclIedIdentityMatcher.TryMatch(
            ambiguous, Domains, "192.0.2.20", "SIEBCU"));
    }

    [Fact]
    public void SclWithoutEndpointEvidence_CannotPromoteToVerifiedIdentity()
    {
        var scl = SyntheticScl();
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        scl.Root!.Element(ns + "Communication")!.Remove();
        Assert.Null(TrustedSclIedIdentityMatcher.TryMatch(
            scl, Domains, "192.0.2.20", "SIEBCU"));
    }

    private static XDocument SyntheticScl()
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        string[] instances = ["CTRL", "MEAS", "PROT", "UD1", "UD2"];
        return new XDocument(new XElement(ns + "SCL",
            new XElement(ns + "Communication",
                new XElement(ns + "SubNetwork",
                    new XElement(ns + "ConnectedAP",
                        new XAttribute("iedName", "SIEBCU"),
                        new XAttribute("apName", "P1"),
                        new XElement(ns + "Address",
                            new XElement(ns + "P",
                                new XAttribute("type", "IP"), "192.0.2.20"))))),
            new XElement(ns + "IED", new XAttribute("name", "SIEBCU"),
                new XElement(ns + "AccessPoint",
                    new XAttribute("name", "P1"),
                    new XElement(ns + "Server",
                        instances.Select(inst => new XElement(ns + "LDevice",
                            new XAttribute("inst", inst))))))));
    }
}
