using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Export;
using System.Xml.Linq;

namespace AR.Iec61850.Tests.Discovery;

public sealed class TrustedSclIedIdentityMatcherTests
{
    private const string SclNs = "http://www.iec.ch/61850/2003/SCL";

    [Fact]
    public void GE_F650_Single_Domain_Uses_SCL_Evidence_Not_Vendor_Suffix_Heuristic()
    {
        var inferred = LiveIedIdentityResolver.Resolve(["BCUGEF650"], "192.16.1.33");
        Assert.Equal("HostFallback", inferred.Source);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Low,inferred.Confidence);

        var identity = TrustedSclIedIdentityMatcher.TryMatch(
            Design("BCUGE", "F650", "192.16.1.33"), ["BCUGEF650"], "192.16.1.33");
        Assert.NotNull(identity);
        Assert.Equal("BCUGE",identity.IedName);
        Assert.Equal("F650",identity.LogicalDeviceAliases["BCUGEF650"]);
        Assert.Equal("TrustedSclExactDomainMatch",identity.Source);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.High,identity.Confidence);
    }

    [Fact]
    public void Verified_F650_Export_RoundTrips_Mms_Domain_And_Fcda_LdInst()
    {
        var model = Model("IED_192_16_1_33","BCUGEF650");
        var identity = TrustedSclIedIdentityMatcher.TryMatch(
            Design("BCUGE","F650","192.16.1.33"),["BCUGEF650"],"192.16.1.33");
        Assert.NotNull(identity);

        var generated = LiveIedSclExporter.BuildDocument(model,
            new LiveIedSclExportOptions { SchemaProfile = SclSchemaProfile.Edition1V16 });
        var normalized = AuthoritativeLiveIedSclExporter.ApplyVerifiedIdentity(generated,model,identity);
        XNamespace ns=SclNs;
        var ied=Assert.Single(normalized.Root!.Elements(ns+"IED"));
        Assert.Equal("BCUGE",(string?)ied.Attribute("name"));
        var device=Assert.Single(ied.Descendants(ns+"LDevice"));
        Assert.Equal("F650",(string?)device.Attribute("inst"));
        Assert.Null(device.Attribute("ldName")); // implicit MMS domain BCUGEF650
        Assert.Equal("BCUGE",(string?)Assert.Single(normalized.Descendants(ns+"ConnectedAP")).Attribute("iedName"));
        var fcda=Assert.Single(normalized.Descendants(ns+"FCDA"));
        Assert.Equal("F650",(string?)fcda.Attribute("ldInst"));
    }

    [Fact]
    public void Explicit_LdName_Remains_Exact_When_Different_From_Implicit_Mms_Domain()
    {
        var scl=Design("BCUGE","F650","192.16.1.33","CUSTOM_WIRE_DOMAIN");
        var identity=TrustedSclIedIdentityMatcher.TryMatch(scl,
            ["CUSTOM_WIRE_DOMAIN"],"192.16.1.33");
        Assert.NotNull(identity);
        var model=Model("IED_192_16_1_33","CUSTOM_WIRE_DOMAIN");
        var doc=AuthoritativeLiveIedSclExporter.ApplyVerifiedIdentity(
            LiveIedSclExporter.BuildDocument(model,new LiveIedSclExportOptions()),model,identity);
        XNamespace ns=SclNs;
        var ld=Assert.Single(doc.Descendants(ns+"LDevice"));
        Assert.Equal("F650",(string?)ld.Attribute("inst"));
        Assert.Equal("CUSTOM_WIRE_DOMAIN",(string?)ld.Attribute("ldName"));
        Assert.Equal("F650",(string?)Assert.Single(doc.Descendants(ns+"FCDA")).Attribute("ldInst"));
    }

    [Fact]
    public void Different_Host_Or_Missing_Domain_Or_Wrong_Selected_IED_Fails_Closed()
    {
        var scl=Design("BCUGE","F650","192.16.1.33");
        Assert.Null(TrustedSclIedIdentityMatcher.TryMatch(scl,["BCUGEF650"],"192.16.1.34"));
        Assert.Null(TrustedSclIedIdentityMatcher.TryMatch(scl,["BCUGEF650","BCUGECTRL"],"192.16.1.33"));
        Assert.Null(TrustedSclIedIdentityMatcher.TryMatch(scl,["BCUGEF650"],"192.16.1.33","OTHER_IED"));
        Assert.Null(TrustedSclIedIdentityMatcher.TryMatch(scl,["DIFFERENT"],"192.16.1.33"));
    }

    [Fact]
    public void Ambiguous_Duplicate_AccessPoints_And_Conflicting_Sources_Are_Not_Promoted()
    {
        XNamespace ns=SclNs;
        var scl=Design("BCUGE","F650","192.16.1.33");
        var ied=Assert.Single(scl.Descendants(ns+"IED"));
        var ap=Assert.Single(ied.Elements(ns+"AccessPoint"));
        var duplicate=new XElement(ap);
        duplicate.SetAttributeValue("name","S2");
        ied.Add(duplicate);
        Assert.Null(TrustedSclIedIdentityMatcher.TryMatch(scl,["BCUGEF650"],"192.16.1.33"));

        var model=Model("IED_192_16_1_33","BCUGEF650");
        var unverified=LiveIedIdentityResolver.Resolve(["BCUGEF650"],"192.16.1.33");
        Assert.Throws<InvalidDataException>(() =>
            AuthoritativeLiveIedSclExporter.ApplyVerifiedIdentity(
                LiveIedSclExporter.BuildDocument(model,new LiveIedSclExportOptions()),
                model,unverified));
    }

    [Fact]
    public void Other_Devices_And_Already_Recognized_Multiple_Domains_Are_Unchanged()
    {
        var old=LiveIedIdentityResolver.Resolve(
            ["OLSF501LD0","OLSF501CTRL"],"192.0.2.10");
        Assert.Equal("OLSF501",old.IedName);
        var other=LiveIedIdentityResolver.Resolve(
            ["OCR7SJ8Application","OCR7SJ8CB1","OCR7SJ8Mod2_MU1"],
            "192.0.2.12");
        Assert.Equal("OCR7SJ8",other.IedName);
    }

    private static XDocument Design(string ied,string inst,string ip,string? ldName=null)
    {
        XNamespace ns=SclNs;
        return new XDocument(new XElement(ns+"SCL",
            new XElement(ns+"Communication",
                new XElement(ns+"SubNetwork",
                    new XElement(ns+"ConnectedAP",
                        new XAttribute("iedName",ied),new XAttribute("apName","S1"),
                        new XElement(ns+"Address",
                            new XElement(ns+"P",new XAttribute("type","IP"),ip))))),
            new XElement(ns+"IED",new XAttribute("name",ied),
                new XElement(ns+"AccessPoint",new XAttribute("name","S1"),
                    new XElement(ns+"Server",
                        new XElement(ns+"LDevice",
                            new XAttribute("inst",inst),
                            ldName is null ? null : new XAttribute("ldName",ldName),
                            new XElement(ns+"LN0",
                                new XAttribute("lnType","LLN0_type"))))))));
    }

    private static LiveIedModelDiscoveryDocument Model(string ied,string domain)
        => new()
        {
            Host="192.16.1.33",
            IedName=ied,
            AccessPointName="S1",
            LogicalDevices=[
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain=domain,
                    Inst=domain,
                    LogicalNodes=[
                        new LiveIedLogicalNodeModel
                        {
                            Name="LLN0",
                            LnClass="LLN0",
                            ProposedLnTypeId="LN_LLN0_type"
                        },
                        new LiveIedLogicalNodeModel
                        {
                            Name="CSWI6",
                            LnClass="CSWI",
                            LnInst="6",
                            ProposedLnTypeId="LN_CSWI6_type"
                        }
                    ]
                }
            ],
            DataSets=[
                new LiveIedDataSetModel
                {
                    Reference=$"{domain}/LLN0.Digital",
                    Domain=domain,LogicalNode="LLN0",Name="Digital",
                    MemberCount=1,
                    Members=[
                        new LiveIedDataSetMemberModel
                        {
                            Index=0,Reference=$"{domain}/CSWI6.Pos.stVal",
                            FunctionalConstraint="ST"
                        }
                    ]
                }
            ]
        };
}
