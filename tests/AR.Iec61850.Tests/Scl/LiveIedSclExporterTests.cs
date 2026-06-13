using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Export;
using System.Xml.Linq;

namespace AR.Iec61850.Tests.Scl;

public sealed class LiveIedSclExporterTests
{
    [Fact]
    public void Exporter_Builds_Importable_Scl_With_Dataset_Report_And_Templates()
    {
        var discovery = new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory(
            [
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "BR",
                    DataObjectPath = "brcbA01.RptEna",
                    MmsItemName = "LLN0$BR$brcbA01$RptEna"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.stVal",
                    MmsItemName = "A50PTOC1$ST$Op$stVal"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.q",
                    MmsItemName = "A50PTOC1$ST$Op$q"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.t",
                    MmsItemName = "A50PTOC1$ST$Op$t"
                }
            ])
        };
        discovery.ReportInventory.DataSets.Add(new MmsDataSetCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "DataSet",
            Reference = "LD0/LLN0.DataSet"
        });
        discovery.ReportInventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "brcbA01",
            Reference = "LD0/LLN0.BR.brcbA01",
            Buffered = true,
            DataSetReference = "LD0/LLN0.DataSet",
            ReportId = "LD0/LLN0$BR$brcbA01",
            ConfRev = "1"
        });
        var dataSet = new MmsDataSetDirectoryResult
        {
            IsSuccess = true,
            DataSetReference = "LD0/LLN0.DataSet",
            Members =
            [
                new MmsDataSetDirectoryMember
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op",
                    UserReference = "LD0/A50PTOC1.Op",
                    MmsItemName = "A50PTOC1$ST$Op"
                }
            ]
        };

        var model = LiveIedModelDiscoveryBuilder.Build(
            discovery,
            new LiveIedModelDiscoveryBuildOptions { Host = "192.0.2.10", IedName = "IED1", AccessPointName = "AP1" },
            [dataSet]);

        var xml = LiveIedSclExporter.BuildDocument(model, new LiveIedSclExportOptions { IpAddress = "192.0.2.10" }).ToString();
        var parsed = new SclParser().Parse(xml, "generated.iid");

        Assert.Single(parsed.Ieds);
        Assert.Single(parsed.DataSets);
        Assert.Single(parsed.ReportControls);
        Assert.Equal("IED1LD0/LLN0$DataSet", parsed.DataSets[0].Reference);
        Assert.Equal("IED1LD0/LLN0$BR$brcbA01", parsed.ReportControls[0].ControlBlockReference);
        Assert.Equal("ACT", parsed.DataSets[0].Entries[0].Cdc);
        Assert.Contains("indexed=\"false\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Exporter_AutoLdNameMode_Strips_IedName_Prefix_For_Product_Related_Domains()
    {
        var discovery = new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory(
            [
                new MmsFcResolvedPoint
                {
                    Domain = "IED1PROT",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "BR",
                    DataObjectPath = "brcbA01.RptEna",
                    MmsItemName = "LLN0$BR$brcbA01$RptEna"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "IED1PROT",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.stVal",
                    MmsItemName = "A50PTOC1$ST$Op$stVal"
                }
            ])
        };
        discovery.ReportInventory.DataSets.Add(new MmsDataSetCandidate
        {
            Domain = "IED1PROT",
            LogicalNode = "LLN0",
            Name = "DataSet",
            Reference = "IED1PROT/LLN0.DataSet"
        });
        discovery.ReportInventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "IED1PROT",
            LogicalNode = "LLN0",
            Name = "brcbA01",
            Reference = "IED1PROT/LLN0.BR.brcbA01",
            Buffered = true,
            DataSetReference = "IED1PROT/LLN0.DataSet",
            ReportId = "IED1PROT/LLN0$BR$brcbA01",
            ConfRev = "1"
        });
        var dataSet = new MmsDataSetDirectoryResult
        {
            IsSuccess = true,
            DataSetReference = "IED1PROT/LLN0.DataSet",
            Members =
            [
                new MmsDataSetDirectoryMember
                {
                    Domain = "IED1PROT",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op",
                    UserReference = "IED1PROT/A50PTOC1.Op",
                    MmsItemName = "A50PTOC1$ST$Op"
                }
            ]
        };

        var model = LiveIedModelDiscoveryBuilder.Build(
            discovery,
            new LiveIedModelDiscoveryBuildOptions { Host = "192.0.2.10", IedName = "IED1", AccessPointName = "AP1" },
            [dataSet]);

        var xml = LiveIedSclExporter.BuildDocument(model, new LiveIedSclExportOptions { IpAddress = "192.0.2.10" }).ToString();
        var document = XDocument.Parse(xml);
        var ns = document.Root!.Name.Namespace;
        var lDevice = document.Descendants(ns + "LDevice").Single();
        var fcda = document.Descendants(ns + "FCDA").Single();
        var parsed = new SclParser().Parse(xml, "generated.iid");

        Assert.Equal("PROT", lDevice.Attribute("inst")?.Value);
        Assert.Equal("PROT", fcda.Attribute("ldInst")?.Value);
        Assert.Equal("IED1PROT/LLN0$DataSet", parsed.DataSets[0].Reference);
        Assert.Equal("IED1PROT/LLN0$BR$brcbA01", parsed.ReportControls[0].ControlBlockReference);
    }

    [Fact]
    public void Exporter_KeepLdNameMode_Preserves_Live_Mms_Domain_As_LDevice_Inst()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "192.0.2.10",
            IedName = "IED1",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IED1LD0",
                    Inst = "IED1LD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "LLN0",
                            LnClass = "LLN0",
                            ProposedLnTypeId = "LN_LLN0_LLN0"
                        }
                    ]
                }
            ]
        };

        var xml = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions
            {
                IpAddress = "192.0.2.10",
                LogicalDeviceNameMode = LiveIedSclLogicalDeviceNameMode.Keep
            }).ToString();
        var document = XDocument.Parse(xml);
        var ns = document.Root!.Name.Namespace;

        Assert.Equal("IED1LD0", document.Descendants(ns + "LDevice").Single().Attribute("inst")?.Value);
    }

    [Fact]
    public void Exporter_Does_Not_Write_Internal_Cdc_Labels_For_Common_Live_Discovery_Patterns()
    {
        var discovery = new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory(
            [
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "DC",
                    DataObjectPath = "NamPlt.vendor",
                    MmsItemName = "LLN0$DC$NamPlt$vendor"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Beh.stVal",
                    MmsItemName = "LLN0$ST$Beh$stVal"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LPHD1",
                    FunctionalConstraint = "DC",
                    DataObjectPath = "PhyNam.vendor",
                    MmsItemName = "LPHD1$DC$PhyNam$vendor"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.general",
                    MmsItemName = "A50PTOC1$ST$Op$general"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.q",
                    MmsItemName = "A50PTOC1$ST$Op$q"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.t",
                    MmsItemName = "A50PTOC1$ST$Op$t"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Str.dirGeneral",
                    MmsItemName = "A50PTOC1$ST$Str$dirGeneral"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Str.general",
                    MmsItemName = "A50PTOC1$ST$Str$general"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "GGIO1",
                    FunctionalConstraint = "CO",
                    DataObjectPath = "SPCSO1.ctlVal",
                    MmsItemName = "GGIO1$CO$SPCSO1$ctlVal"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "GGIO1",
                    FunctionalConstraint = "CF",
                    DataObjectPath = "SPCSO1.ctlModel",
                    MmsItemName = "GGIO1$CF$SPCSO1$ctlModel"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "SP",
                    DataObjectPath = "SGCB.ActSG",
                    MmsItemName = "LLN0$SP$SGCB$ActSG"
                }
            ])
        };
        var model = LiveIedModelDiscoveryBuilder.Build(
            discovery,
            new LiveIedModelDiscoveryBuildOptions { Host = "192.0.2.10", IedName = "IED1", AccessPointName = "AP1" });

        var xml = LiveIedSclExporter.BuildDocument(model, new LiveIedSclExportOptions { IpAddress = "192.0.2.10" }).ToString();
        var document = XDocument.Parse(xml);
        var ns = document.Root!.Name.Namespace;
        var cdcValues = document.Descendants(ns + "DOType")
            .Select(x => x.Attribute("cdc")?.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        Assert.DoesNotContain("GEN", cdcValues);
        Assert.DoesNotContain("Status", cdcValues);
        Assert.DoesNotContain("Controllable", cdcValues);
        Assert.DoesNotContain("Setting", cdcValues);
        Assert.DoesNotContain("Measurement", cdcValues);
        Assert.Contains("LPL", cdcValues);
        Assert.Contains("DPL", cdcValues);
        Assert.Contains("ACT", cdcValues);
        Assert.Contains("ACD", cdcValues);
        Assert.Contains("SPC", cdcValues);
        Assert.Single(document.Descendants(ns + "SettingControl"));

        var beh = document.Descendants(ns + "DOType").Single(x => x.Attribute("id")?.Value == "DO_INS_LLN0_Beh");
        var behStVal = beh.Elements(ns + "DA").Single(x => x.Attribute("name")?.Value == "stVal");
        Assert.Equal("Enum", behStVal.Attribute("bType")?.Value);
        Assert.Equal("ARIEC61850_BehaviourKind", behStVal.Attribute("type")?.Value);

        var op = document.Descendants(ns + "DOType").Single(x => x.Attribute("id")?.Value == "DO_ACT_PTOC_Op");
        var opGeneral = op.Elements(ns + "DA").Single(x => x.Attribute("name")?.Value == "general");
        Assert.Equal("BOOLEAN", opGeneral.Attribute("bType")?.Value);
    }

    [Fact]
    public void Exporter_Includes_Goose_And_Sv_Control_Block_Shells_When_Discovered()
    {
        var dataSet = new LiveIedDataSetModel
        {
            Reference = "LD0/LLN0.DataSet",
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "DataSet",
            MemberCount = 1,
            Members =
            [
                new LiveIedDataSetMemberModel
                {
                    Index = 0,
                    Reference = "LD0/A50PTOC1.Op",
                    FunctionalConstraint = "ST"
                }
            ]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "192.0.2.10",
            IedName = "IED1",
            AccessPointName = "AP1",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    Inst = "LD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "LLN0",
                            LnClass = "LLN0",
                            ProposedLnTypeId = "LN_LLN0_LLN0"
                        }
                    ]
                }
            ],
            DataSets = [dataSet],
            GooseControlBlocks =
            [
                new LiveIedControlBlockModel
                {
                    Kind = "GSEControl",
                    Reference = "LD0/LLN0.GO.gcbA01",
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    Name = "gcbA01",
                    FunctionalConstraint = "GO",
                    DataSetReference = "LD0/LLN0.DataSet",
                    ControlId = "GOOSE_A01",
                    ConfRev = "1"
                }
            ],
            SampledValueControlBlocks =
            [
                new LiveIedControlBlockModel
                {
                    Kind = "SampledValueControl",
                    Reference = "LD0/LLN0.MS.msvcb01",
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    Name = "msvcb01",
                    FunctionalConstraint = "MS",
                    DataSetReference = "LD0/LLN0.DataSet",
                    SmvId = "SV_A01",
                    ConfRev = "2",
                    SampleRate = "4000",
                    NumberOfAsdu = "1"
                }
            ]
        };

        var xml = LiveIedSclExporter.BuildDocument(model, new LiveIedSclExportOptions { IpAddress = "192.0.2.10" }).ToString();
        var parsed = new SclParser().Parse(xml, "generated.iid");

        Assert.Single(parsed.GooseStreams);
        Assert.Single(parsed.SampledValuesStreams);
        Assert.Equal("IED1LD0/LLN0$GO$gcbA01", parsed.GooseStreams[0].ControlBlockReference);
        Assert.Equal("IED1LD0/LLN0$SV$msvcb01", parsed.SampledValuesStreams[0].ControlBlockReference);
    }


    [Fact]
    public void Exporter_IedScoutConnectionProfile_Excludes_Control_Service_And_Optional_Config_Attributes()
    {
        var discovery = new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory(
            [
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "Q0CSWI1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Pos.stVal",
                    MmsItemName = "Q0CSWI1$ST$Pos$stVal"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "Q0CSWI1",
                    FunctionalConstraint = "CO",
                    DataObjectPath = "Pos.Oper.ctlVal",
                    MmsItemName = "Q0CSWI1$CO$Pos$Oper$ctlVal"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "Q0CSWI1",
                    FunctionalConstraint = "CO",
                    DataObjectPath = "Pos.SBOw.Check",
                    MmsItemName = "Q0CSWI1$CO$Pos$SBOw$Check"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "Q0CSWI1",
                    FunctionalConstraint = "CO",
                    DataObjectPath = "Pos.origin",
                    MmsItemName = "Q0CSWI1$CO$Pos$origin"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "MMXU1",
                    FunctionalConstraint = "MX",
                    DataObjectPath = "PhV.phsA.cVal.mag.f",
                    MmsItemName = "MMXU1$MX$PhV$phsA$cVal$mag$f"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "MMXU1",
                    FunctionalConstraint = "CF",
                    DataObjectPath = "PhV.phsA.units.SIUnit",
                    MmsItemName = "MMXU1$CF$PhV$phsA$units$SIUnit"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "MMXU1",
                    FunctionalConstraint = "CF",
                    DataObjectPath = "PhV.phsA.units",
                    MmsItemName = "MMXU1$CF$PhV$phsA$units"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "IED1LD0",
                    LogicalNode = "MMXU1",
                    FunctionalConstraint = "CF",
                    DataObjectPath = "PhV.phsA.db",
                    MmsItemName = "MMXU1$CF$PhV$phsA$db"
                }
            ])
        };

        var model = LiveIedModelDiscoveryBuilder.Build(
            discovery,
            new LiveIedModelDiscoveryBuildOptions { Host = "192.0.2.10", IedName = "IED1", AccessPointName = "AP1" });

        var xml = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions { Profile = "iedscout-connection", IpAddress = "192.0.2.10" }).ToString();

        Assert.Contains("<DA name=\"stVal\"", xml, StringComparison.Ordinal);
        Assert.Contains("<BDA name=\"f\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Oper", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("SBOw", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("origin", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("units", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SIUnit", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("db\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Exporter_FullModelProfile_Keeps_Control_Service_Attributes_For_Simulator_Seed_Workflows()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "192.0.2.10",
            IedName = "IED1",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "LD0",
                    Inst = "LD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "Q0CSWI1",
                            Prefix = "Q0",
                            LnClass = "CSWI",
                            LnInst = "1",
                            ProposedLnTypeId = "LN_CSWI_Q0CSWI1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "LD0/Q0CSWI1.Pos",
                                    Name = "Pos",
                                    ProposedDoTypeId = "DO_DPC_CSWI_Pos",
                                    InferredCdc = "DPC",
                                    CdcConfidence = 0.94,
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel { AttributePath = "stVal", FunctionalConstraint = "ST", SclBType = "INT32" },
                                        new LiveIedDataAttributeModel { AttributePath = "Oper.ctlVal", FunctionalConstraint = "CO", SclBType = "INT32" }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var xml = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions { Profile = "full-model", IpAddress = "192.0.2.10" }).ToString();

        Assert.Contains("Oper", xml, StringComparison.Ordinal);
        Assert.Contains("ctlVal", xml, StringComparison.Ordinal);
    }


    [Fact]
    public void Exporter_InsCdc_Uses_Enum_BType_And_EnumType_For_Status_Value()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "192.0.2.10",
            IedName = "IED1",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IED1CTRL",
                    Inst = "IED1CTRL",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "LLN0",
                            LnClass = "LLN0",
                            ProposedLnTypeId = "LN_LLN0",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IED1CTRL/LLN0.Beh",
                                    Name = "Beh",
                                    ProposedDoTypeId = "DO_INS_LLN0_Beh",
                                    InferredCdc = "INS",
                                    CdcConfidence = 0.94,
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel { AttributePath = "stVal", FunctionalConstraint = "ST", SclBType = "INT32" },
                                        new LiveIedDataAttributeModel { AttributePath = "q", FunctionalConstraint = "ST", SclBType = "Quality" },
                                        new LiveIedDataAttributeModel { AttributePath = "t", FunctionalConstraint = "ST", SclBType = "Timestamp" }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var xml = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions { Profile = "full-model", IpAddress = "192.0.2.10" }).ToString();

        Assert.Contains("cdc=\"INS\"", xml, StringComparison.Ordinal);
        Assert.Contains("<DA name=\"stVal\" fc=\"ST\" bType=\"Enum\" type=\"ARIEC61850_BehaviourKind\"", xml, StringComparison.Ordinal);
        Assert.Contains("<EnumType id=\"ARIEC61850_BehaviourKind\">", xml, StringComparison.Ordinal);
    }

}
