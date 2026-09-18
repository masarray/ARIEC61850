using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Engineering;

public sealed class CanonicalLiveIedSclExporterTests
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    [Theory]
    [InlineData(SclSchemaProfile.Edition2V31)]
    [InlineData(SclSchemaProfile.Edition1V16)]
    public void WriteFiles_RoundTripsCanonicalAssociationWithoutExporterDefaults(SclSchemaProfile schema)
    {
        var root = Path.Combine(Path.GetTempPath(), "ariec-canonical-scl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, schema == SclSchemaProfile.Edition2V31 ? "ied.iid" : "ied.icd");
        try
        {
            var canonical = CreateCanonical();
            CanonicalLiveIedSclExporter.WriteFiles(canonical, path, schema);

            var document = XDocument.Load(path);
            var address = document.Descendants(Scl + "ConnectedAP").Single().Element(Scl + "Address")!;
            var values = address.Elements(Scl + "P")
                .ToDictionary(e => (string)e.Attribute("type")!, e => e.Value, StringComparer.OrdinalIgnoreCase);

            Assert.Equal("10.20.30.40", values["IP"]);
            Assert.Equal("1,1,1,999,1", values["OSI-AP-Title"]);
            Assert.Equal("12", values["OSI-AE-Qualifier"]);
            Assert.Equal("00000001", values["OSI-PSEL"]);
            Assert.Equal("0001", values["OSI-SSEL"]);
            Assert.Equal("0001", values["OSI-TSEL"]);
            Assert.False(values.ContainsKey("IP-SUBNET"));
            Assert.False(values.ContainsKey("IP-GATEWAY"));

            var profiles = SclMmsAssociationProfileReader.Read(document);
            var remote = Assert.Single(profiles.AccessPoints);
            var plan = SclAssistedMmsAssociationPlanBuilder.BuildExact(
                remote,
                MmsLocalAssociationProfile.SclInteroperabilityDefault);
            Assert.True(plan.IsSuccess, string.Join(" | ", plan.Errors));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteFiles_RejectsAssociationEvidenceThatChangesDuringRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "ariec-canonical-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "ied.iid");
        try
        {
            var baseline = CreateCanonical();
            var canonical = new LiveIedCanonicalModel
            {
                Discovery = baseline.Discovery,
                Communication = new LiveIedCommunicationEvidence
                {
                    Source = baseline.Communication.Source,
                    AssociationProfileName = baseline.Communication.AssociationProfileName,
                    Host = baseline.Communication.Host,
                    Port = baseline.Communication.Port,
                    AccessPointName = baseline.Communication.AccessPointName,
                    Association = new SclIsoAssociationAddress
                    {
                        ApTitle = "\"1,1,1,999,1\"",
                        AeQualifierText = "12",
                        AeQualifier = 12,
                        PresentationSelector = "00000001",
                        SessionSelector = "0001",
                        TransportSelector = "0001"
                    }
                }
            };

            var ex = Assert.Throws<InvalidDataException>(() =>
                CanonicalLiveIedSclExporter.WriteFiles(
                    canonical,
                    path,
                    SclSchemaProfile.Edition2V31));

            Assert.Contains(
                "changed canonical association evidence during serialization",
                ex.Message,
                StringComparison.Ordinal);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void ValidateCanonicalCommunication_RejectsMissingRemoteApTitle()
    {
        var canonical = CreateCanonical();
        canonical = new LiveIedCanonicalModel
        {
            Discovery = canonical.Discovery,
            Communication = new LiveIedCommunicationEvidence
            {
                Host = canonical.Communication.Host,
                AccessPointName = canonical.Communication.AccessPointName,
                Association = new SclIsoAssociationAddress
                {
                    AeQualifier = 12,
                    AeQualifierText = "12",
                    PresentationSelector = "00000001",
                    SessionSelector = "0001",
                    TransportSelector = "0001"
                }
            }
        };

        var ex = Assert.Throws<InvalidDataException>(() =>
            CanonicalLiveIedSclExporter.ValidateCanonicalCommunication(canonical));
        Assert.Contains("OSI-AP-Title", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PreserveRuntimeServiceCapacity_KeepsPhysicalCountIndependentOfLogicalProjection()
    {
        var document = XDocument.Parse(
            "<SCL xmlns='http://www.iec.ch/61850/2003/SCL'><IED name='IED'><Services><ConfReportControl max='2'/></Services></IED></SCL>");
        var discovery = new LiveIedModelDiscoveryDocument
        {
            ReportControls = Enumerable.Range(1, 34)
                .Select(i => new LiveIedReportControlModel { Name = $"R{i:00}" })
                .ToArray()
        };

        CanonicalLiveIedSclExporter.PreserveRuntimeServiceCapacity(document, discovery);

        Assert.Equal("34", (string?)document.Descendants(Scl + "ConfReportControl").Single().Attribute("max"));
    }

    [Fact]
    public void CanonicalBuilder_RetainsExactInitialFcLeafEvidence()
    {
        var discovery = CreateInstanceDiscovery();
        var communication = CreateCanonical().Communication;
        var target = new InitialFcReadTarget
        {
            Domain = "IEDLD0",
            LogicalNode = "GGIO1",
            FunctionalConstraint = "ST",
            MmsItemName = "GGIO1$ST",
            DataObjects =
            [
                new InitialFcReadDataObjectBinding
                {
                    Name = "Ind1",
                    Reference = "IEDLD0/GGIO1.Ind1",
                    Leaves =
                    [
                        new InitialFcReadLeafBinding
                        {
                            Reference = "IEDLD0/GGIO1.Ind1.stVal",
                            AttributePath = "stVal",
                            FunctionalConstraint = "ST",
                            SclBType = "BOOLEAN"
                        }
                    ]
                }
            ]
        };
        var projection = InitialFcValueProjector.Project(
            target,
            MmsDataValue.Structure(
            [
                MmsDataValue.Structure([MmsDataValue.Boolean(true)])
            ]));
        Assert.True(projection.IsExact, string.Join(" | ", projection.Errors));

        var execution = new InitialFcReadExecutionResult
        {
            Status = InitialFcReadExecutionStatus.Completed,
            Batches =
            [
                new InitialFcReadBatchExecution
                {
                    BatchIndex = 0,
                    Targets = [target],
                    Projections = [projection]
                }
            ]
        };

        var canonical = LiveIedCanonicalModelBuilder.Build(
            discovery,
            communication,
            execution);

        var value = Assert.Single(canonical.InstanceValues);
        Assert.Equal("IEDLD0", value.Domain);
        Assert.Equal("GGIO1", value.LogicalNode);
        Assert.Equal("Ind1", value.DataObject);
        Assert.Equal("stVal", value.AttributePath);
        Assert.Equal(MmsDataKind.Boolean, value.Value.Kind);
    }

    [Theory]
    [InlineData(SclSchemaProfile.Edition2V31)]
    [InlineData(SclSchemaProfile.Edition1V16)]
    public void WriteFiles_MaterializesExactInstanceValueAgainstExportedTypeTree(
        SclSchemaProfile schema)
    {
        var root = Path.Combine(Path.GetTempPath(), "ariec-canonical-instance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, schema == SclSchemaProfile.Edition2V31 ? "ied.iid" : "ied.icd");
        try
        {
            var baseline = CreateCanonical();
            var canonical = new LiveIedCanonicalModel
            {
                Discovery = CreateInstanceDiscovery(),
                Communication = baseline.Communication,
                InstanceValues =
                [
                    new LiveIedInstanceValueEvidence
                    {
                        Domain = "IEDLD0",
                        LogicalNode = "GGIO1",
                        DataObject = "Ind1",
                        AttributePath = "stVal",
                        FunctionalConstraint = "ST",
                        SclBType = "BOOLEAN",
                        Value = MmsDataValue.Boolean(true)
                    }
                ]
            };

            CanonicalLiveIedSclExporter.WriteFiles(canonical, path, schema);

            var document = XDocument.Load(path);
            var lDevice = document.Descendants(Scl + "LDevice")
                .Single(element => (string?)element.Attribute("inst") == "LD0");
            var logicalNode = lDevice.Elements(Scl + "LN")
                .Single(element =>
                    (string?)element.Attribute("lnClass") == "GGIO" &&
                    (string?)element.Attribute("inst") == "1");
            var val = logicalNode.Elements(Scl + "DOI")
                .Single(element => (string?)element.Attribute("name") == "Ind1")
                .Elements(Scl + "DAI")
                .Single(element => (string?)element.Attribute("name") == "stVal")
                .Element(Scl + "Val");

            Assert.NotNull(val);
            Assert.Equal("true", val!.Value);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static LiveIedModelDiscoveryDocument CreateInstanceDiscovery()
        => new()
        {
            Host = "10.20.30.40",
            IedName = "IED",
            AccessPointName = "AP1",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0",
                    Inst = "IEDLD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "GGIO1",
                            LnClass = "GGIO",
                            LnInst = "1",
                            ProposedLnTypeId = "LN_GGIO_1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IEDLD0/GGIO1.Ind1",
                                    Name = "Ind1",
                                    ProposedDoTypeId = "DO_SPS_Ind1",
                                    InferredCdc = "SPS",
                                    CdcConfidence = 0.99,
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = "IEDLD0/GGIO1.Ind1.stVal",
                                            AttributePath = "stVal",
                                            FunctionalConstraint = "ST",
                                            MmsReference = "IEDLD0/GGIO1$ST$Ind1$stVal",
                                            MmsItemName = "GGIO1$ST$Ind1$stVal",
                                            Source = "GetNameList",
                                            SclBType = "BOOLEAN",
                                            MmsType = "Boolean",
                                            TypeDiscoveryStatus = "Exact",
                                            TypeSource = "GetVariableAccessAttributes",
                                            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

    private static LiveIedCanonicalModel CreateCanonical()
        => new()
        {
            Discovery = new LiveIedModelDiscoveryDocument
            {
                Host = "10.20.30.40",
                IedName = "IED",
                AccessPointName = "AP1"
            },
            Communication = new LiveIedCommunicationEvidence
            {
                Source = "AcceptedAssociationProfile",
                AssociationProfileName = "BalancedApTitle",
                Host = "10.20.30.40",
                Port = 102,
                AccessPointName = "AP1",
                Association = new SclIsoAssociationAddress
                {
                    ApTitle = "1,1,1,999,1",
                    AeQualifierText = "12",
                    AeQualifier = 12,
                    PresentationSelector = "00000001",
                    SessionSelector = "0001",
                    TransportSelector = "0001"
                }
            }
        };
}
