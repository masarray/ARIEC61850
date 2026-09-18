using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Mms;

public sealed class InitialFcReadSclShapeTests
{
    [Fact]
    public void Exported_Scl_RoundTrip_Preserves_Status_Leaf_Order_For_FcRoot_Projection()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "192.0.2.10",
            IedName = "IED1",
            AccessPointName = "AP1",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IED1ADD",
                    Inst = "IED1ADD",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "GGIO1",
                            LnClass = "GGIO",
                            LnInst = "1",
                            ProposedLnTypeId = "LN_GGIO1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IED1ADD/GGIO1.Ind1",
                                    Name = "Ind1",
                                    ProposedDoTypeId = "DO_SPS_GGIO_Ind1",
                                    InferredCdc = "SPS",
                                    CdcConfidence = 1.0,
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Exact,
                                    TypeDeclarationOrder = "000000.000000",
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = "IED1ADD/GGIO1.Ind1.stVal",
                                            AttributePath = "stVal",
                                            FunctionalConstraint = "ST",
                                            SclBType = "BOOLEAN",
                                            TypeDeclarationOrder = "000000.000000.000000"
                                        },
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = "IED1ADD/GGIO1.Ind1.q",
                                            AttributePath = "q",
                                            FunctionalConstraint = "ST",
                                            SclBType = "Quality",
                                            TypeDeclarationOrder = "000000.000000.000001"
                                        },
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = "IED1ADD/GGIO1.Ind1.t",
                                            AttributePath = "t",
                                            FunctionalConstraint = "ST",
                                            SclBType = "Timestamp",
                                            TypeDeclarationOrder = "000000.000000.000002"
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var scl = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions
            {
                Profile = "full-model",
                IpAddress = "192.0.2.10"
            }).ToString();

        var design = SclInitialFcReadDesignBuilder.Read(scl, "IED1", "AP1");
        Assert.True(design.IsSuccess, string.Join(" | ", design.Errors));

        var plan = InitialFcReadPlanner.FromSclModel(
            design.Model,
            design.DomainInventory.ExpectedDomains);
        var target = Assert.Single(
            plan.Targets,
            item => item.MmsReference == "IED1ADD/GGIO1$ST");
        var binding = Assert.Single(target.DataObjects);

        Assert.Equal(
            ["stVal", "q", "t"],
            binding.Leaves.Select(leaf => leaf.AttributePath).ToArray());

        var projection = InitialFcValueProjector.Project(
            target,
            MmsDataValue.Structure(
            [
                MmsDataValue.Structure(
                [
                    MmsDataValue.Boolean(false),
                    MmsDataValue.BitString(3, [0x00, 0x00]),
                    MmsDataValue.VisibleString("timestamp-placeholder")
                ])
            ]));

        Assert.Empty(projection.Errors);
        Assert.Equal(
            ["stVal", "q", "t"],
            projection.Leaves.Select(leaf => leaf.AttributePath).ToArray());
        Assert.Equal(MmsDataKind.Boolean, projection.Leaves[0].Value.Kind);
        Assert.Equal(MmsDataKind.BitString, projection.Leaves[1].Value.Kind);
        Assert.Equal(MmsDataKind.VisibleString, projection.Leaves[2].Value.Kind);
    }

    [Fact]
    public void Planner_Treats_Scl_Struct_As_Container_Not_Leaf()
    {
        var design = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IED01LD0",
                    Inst = "LD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMXU1",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Name = "A",
                                    Reference = "IED01LD0/MMXU1.A",
                                    Attributes = new[]
                                    {
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = "IED01LD0/MMXU1.A.mag",
                                            AttributePath = "mag",
                                            FunctionalConstraint = "MX",
                                            SclBType = "Struct"
                                        },
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = "IED01LD0/MMXU1.A.mag.f",
                                            AttributePath = "mag.f",
                                            FunctionalConstraint = "MX",
                                            SclBType = "FLOAT32"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var plan = InitialFcReadPlanner.FromSclModel(design);

        var target = Assert.Single(plan.Targets);
        var dataObject = Assert.Single(target.DataObjects);
        var leaf = Assert.Single(dataObject.Leaves);
        Assert.Equal("mag.f", leaf.AttributePath);
    }

    [Fact]
    public void Scl_Design_Is_Scoped_To_Exact_AccessPoint_When_LdInst_Repeats()
    {
        const string xml = """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
              <IED name="IED01">
                <AccessPoint name="AP1">
                  <Server>
                    <LDevice inst="LD0">
                      <LN0 lnClass="LLN0" inst="" lnType="LNT_A" />
                    </LDevice>
                  </Server>
                </AccessPoint>
                <AccessPoint name="AP2">
                  <Server>
                    <LDevice inst="LD0" ldName="AP2_DOMAIN">
                      <LN0 lnClass="LLN0" inst="" lnType="LNT_B" />
                    </LDevice>
                  </Server>
                </AccessPoint>
              </IED>
              <DataTypeTemplates>
                <LNodeType id="LNT_A" lnClass="LLN0"><DO name="FromAP1" type="DOT_A" /></LNodeType>
                <LNodeType id="LNT_B" lnClass="LLN0"><DO name="FromAP2" type="DOT_B" /></LNodeType>
                <DOType id="DOT_A" cdc="INS"><DA name="stVal" bType="INT32" fc="ST" /></DOType>
                <DOType id="DOT_B" cdc="INS"><DA name="stVal" bType="INT32" fc="ST" /></DOType>
              </DataTypeTemplates>
            </SCL>
            """;

        var design = SclInitialFcReadDesignBuilder.Read(xml, "IED01", "AP2");
        var plan = InitialFcReadPlanner.FromSclModel(
            design.Model,
            design.DomainInventory.ExpectedDomains);

        Assert.True(design.IsSuccess, string.Join(" | ", design.Errors));
        var target = Assert.Single(plan.Targets);
        Assert.Equal("AP2_DOMAIN/LLN0$ST", target.MmsReference);
        var dataObject = Assert.Single(target.DataObjects);
        Assert.Equal("FromAP2", dataObject.Name);
        Assert.DoesNotContain(target.DataObjects, item => item.Name == "FromAP1");
    }
}
