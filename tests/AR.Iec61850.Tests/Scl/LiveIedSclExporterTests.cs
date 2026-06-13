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
                    Domain = "OCR7SR12PROT",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "BR",
                    DataObjectPath = "brcbA01.RptEna",
                    MmsItemName = "LLN0$BR$brcbA01$RptEna"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "OCR7SR12PROT",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.stVal",
                    MmsItemName = "A50PTOC1$ST$Op$stVal"
                }
            ])
        };
        discovery.ReportInventory.DataSets.Add(new MmsDataSetCandidate
        {
            Domain = "OCR7SR12PROT",
            LogicalNode = "LLN0",
            Name = "DataSet",
            Reference = "OCR7SR12PROT/LLN0.DataSet"
        });
        discovery.ReportInventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "OCR7SR12PROT",
            LogicalNode = "LLN0",
            Name = "brcbA01",
            Reference = "OCR7SR12PROT/LLN0.BR.brcbA01",
            Buffered = true,
            DataSetReference = "OCR7SR12PROT/LLN0.DataSet",
            ReportId = "OCR7SR12PROT/LLN0$BR$brcbA01",
            ConfRev = "1"
        });
        var dataSet = new MmsDataSetDirectoryResult
        {
            IsSuccess = true,
            DataSetReference = "OCR7SR12PROT/LLN0.DataSet",
            Members =
            [
                new MmsDataSetDirectoryMember
                {
                    Domain = "OCR7SR12PROT",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op",
                    UserReference = "OCR7SR12PROT/A50PTOC1.Op",
                    MmsItemName = "A50PTOC1$ST$Op"
                }
            ]
        };

        var model = LiveIedModelDiscoveryBuilder.Build(
            discovery,
            new LiveIedModelDiscoveryBuildOptions { Host = "192.0.2.10", IedName = "OCR7SR12", AccessPointName = "AP1" },
            [dataSet]);

        var xml = LiveIedSclExporter.BuildDocument(model, new LiveIedSclExportOptions { IpAddress = "192.0.2.10" }).ToString();
        var document = XDocument.Parse(xml);
        var ns = document.Root!.Name.Namespace;
        var lDevice = document.Descendants(ns + "LDevice").Single();
        var fcda = document.Descendants(ns + "FCDA").Single();
        var parsed = new SclParser().Parse(xml, "generated.iid");

        Assert.Equal("PROT", lDevice.Attribute("inst")?.Value);
        Assert.Equal("PROT", fcda.Attribute("ldInst")?.Value);
        Assert.Equal("OCR7SR12PROT/LLN0$DataSet", parsed.DataSets[0].Reference);
        Assert.Equal("OCR7SR12PROT/LLN0$BR$brcbA01", parsed.ReportControls[0].ControlBlockReference);
    }

    [Fact]
    public void Exporter_KeepLdNameMode_Preserves_Live_Mms_Domain_As_LDevice_Inst()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "192.0.2.10",
            IedName = "OCR7SR12",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "OCR7SR12PROT",
                    Inst = "OCR7SR12PROT",
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

        Assert.Equal("OCR7SR12PROT", document.Descendants(ns + "LDevice").Single().Attribute("inst")?.Value);
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
        Assert.Equal("INT32", behStVal.Attribute("bType")?.Value);

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

}
