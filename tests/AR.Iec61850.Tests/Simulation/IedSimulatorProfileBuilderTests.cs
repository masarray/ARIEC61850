using AR.Iec61850.Discovery;
using AR.Iec61850.Scl;
using AR.Iec61850.Simulation;
using AR.Iec61850.Tests.Scl;

namespace AR.Iec61850.Tests.Simulation;

public sealed class IedSimulatorProfileBuilderTests
{
    [Fact]
    public void FromScl_Builds_Logical_Devices_Nodes_And_Points()
    {
        var result = Build();
        var profile = result.Profile;

        Assert.Equal("MU01", result.SelectedIedName);
        Assert.Equal("MU01", profile.Name);
        Assert.Single(profile.LogicalDevices);

        var device = profile.LogicalDevices.Single();
        Assert.Equal("MU01LD0", device.Name);
        Assert.Equal(2, device.LogicalNodes.Count);
        Assert.Contains(device.LogicalNodes, n => n.Name == "TCTR1" && n.LnClass == "TCTR");
        Assert.Contains(device.LogicalNodes, n => n.Name == "XCBR1" && n.LnClass == "XCBR");

        // dsSV: instMag.i (+q), dsGO: stVal (+q, +t) = 5 unique points with q/t included.
        Assert.Equal(5, profile.PointCount);
    }

    [Fact]
    public void FromScl_Classifies_Measurement_Status_And_Companion_Points()
    {
        var profile = Build().Profile;
        var points = profile.LogicalDevices.SelectMany(d => d.LogicalNodes).SelectMany(n => n.Points).ToList();

        var current = points.Single(p => p.Reference == "TCTR1.Amp.instMag.i");
        Assert.Equal("measurement", current.Kind);
        Assert.Equal("A", current.Unit);
        Assert.Equal("MX", current.FunctionalConstraint);
        Assert.Equal("INT32", current.SclBType);

        var breaker = points.Single(p => p.Reference == "XCBR1.Pos.stVal");
        Assert.Equal("status", breaker.Kind);
        Assert.Equal("true", breaker.InitialValue);
        Assert.Equal("BOOLEAN", breaker.SclBType);

        Assert.Contains(points, p => p.Reference == "XCBR1.Pos.q" && p.Kind == "quality" && p.InitialValue == "valid");
        Assert.Contains(points, p => p.Reference == "XCBR1.Pos.t" && p.Kind == "timestamp");
    }

    [Fact]
    public void FromScl_Maps_DataSets_And_Report_Control_Blocks()
    {
        var profile = Build().Profile;

        Assert.Equal(2, profile.DataSets.Count);
        Assert.All(profile.DataSets, ds => Assert.NotEmpty(ds.Members));

        var rcb = Assert.Single(profile.ReportControlBlocks);
        Assert.False(rcb.Buffered);
        Assert.Equal("URCB", rcb.Mode);
        Assert.Equal(1, rcb.ConfRev);
    }

    [Fact]
    public void FromScl_DataSet_Members_Resolve_Against_ReadOnly_Server_Model()
    {
        var profile = Build().Profile;

        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(profile);

        // Every derived DataSet member must resolve to a readable point: no missing-member gaps.
        Assert.DoesNotContain(serverProfile.Diagnostics, d => d.Code == "DATASET_MEMBER_MISSING");
        Assert.True(serverProfile.IsReady);

        var session = new MmsReadOnlyServerSession(serverProfile);
        var read = session.Handle(new MmsReadOnlyServerRequest
        {
            Operation = MmsReadOnlyOperation.Read,
            Target = "MU01LD0/TCTR1.Amp.instMag.i"
        });

        Assert.True(read.IsSuccess);
        Assert.Contains(read.Values, v => v.Reference == "MU01LD0/TCTR1.Amp.instMag.i");
    }

    [Fact]
    public void FromScl_Can_Exclude_Quality_And_Timestamp_Points()
    {
        var result = new IedSimulatorProfileBuilder().FromScl(Document(), new IedSimulatorProfileFromSclOptions
        {
            IncludeQualityAndTimestampPoints = false
        });

        var points = result.Profile.LogicalDevices.SelectMany(d => d.LogicalNodes).SelectMany(n => n.Points).ToList();
        Assert.Equal(2, points.Count); // only instMag.i and Pos.stVal remain
        Assert.DoesNotContain(points, p => p.Kind == "quality" || p.Kind == "timestamp");
    }

    [Fact]
    public void FromScl_Instantiates_Generic_Icd_Template_And_Preserves_Full_Structural_Model()
    {
        var document = new SclDocument
        {
            SourceName = "SIE7SR5.icd",
            Ieds =
            [
                new SclIed { Name = "TEMPLATE", Manufacturer = "SIEMENS", Type = "7SR5" }
            ],
            ReportControls =
            [
                new SclReportControl
                {
                    IedName = "TEMPLATE",
                    LdInst = "PROT",
                    LogicalNodePath = "CSWI1",
                    Name = "urcbA",
                    ReportId = "SIEMENS_STATUS",
                    ControlBlockReference = "TEMPLATEPROT/CSWI1.RP.urcbA",
                    DataSetReference = "TEMPLATEPROT/LLN0.dsStatus",
                    ConfigurationRevision = 7
                }
            ]
        };
        var structuralModel = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "TEMPLATEPROT",
                    Inst = "PROT",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "LLN0",
                            LnClass = "LLN0",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Name = "Mod",
                                    InferredCdc = "ENS",
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel
                                        {
                                            AttributePath = "stVal",
                                            FunctionalConstraint = "ST",
                                            SclBType = "BOOLEAN"
                                        }
                                    ]
                                }
                            ]
                        },
                        new LiveIedLogicalNodeModel
                        {
                            Name = "CSWI1",
                            LnClass = "CSWI"
                        }
                    ]
                },
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "TEMPLATECTRL",
                    Inst = "CTRL",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "XCBR1",
                            LnClass = "XCBR",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Name = "Pos",
                                    InferredCdc = "DPC",
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel
                                        {
                                            AttributePath = "stVal",
                                            FunctionalConstraint = "ST",
                                            SclBType = "Dbpos"
                                        },
                                        new LiveIedDataAttributeModel
                                        {
                                            AttributePath = "ctlModel",
                                            FunctionalConstraint = "CF",
                                            SclBType = "Enum"
                                        }
                                    ]
                                },
                                new LiveIedDataObjectModel
                                {
                                    Name = "Beh",
                                    InferredCdc = "INS",
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel
                                        {
                                            AttributePath = "stVal",
                                            FunctionalConstraint = "ST",
                                            SclBType = "Enum"
                                        }
                                    ]
                                },
                                new LiveIedDataObjectModel
                                {
                                    Name = "Str",
                                    InferredCdc = "ACD",
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel
                                        {
                                            AttributePath = "dirGeneral",
                                            FunctionalConstraint = "ST",
                                            SclBType = "Enum"
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var result = new IedSimulatorProfileBuilder().FromScl(document, structuralModel);
        var profile = result.Profile;

        Assert.Equal("TEMPLATE", result.SourceIedName);
        Assert.Equal("SIE7SR5", result.SelectedIedName);
        Assert.Equal("SIE7SR5", profile.Name);
        Assert.Equal(5, result.StructuralDataAttributeCount);
        Assert.Equal(2, profile.LogicalDevices.Count);
        Assert.Contains(profile.LogicalDevices, device => device.Name == "SIE7SR5PROT");
        Assert.Contains(profile.LogicalDevices, device => device.Name == "SIE7SR5CTRL");

        var protection = profile.LogicalDevices.Single(device => device.Name == "SIE7SR5PROT");
        Assert.Contains(protection.LogicalNodes, node => node.Name == "CSWI1");
        Assert.Contains(profile.LogicalDevices.SelectMany(device => device.LogicalNodes).SelectMany(node => node.Points),
            point => point.Reference == "LLN0.Mod.stVal" && point.FunctionalConstraint == "ST");
        Assert.Contains(profile.LogicalDevices.SelectMany(device => device.LogicalNodes).SelectMany(node => node.Points),
            point => point.Reference == "XCBR1.Pos.stVal" && point.InitialValue == "closed");
        Assert.Contains(profile.LogicalDevices.SelectMany(device => device.LogicalNodes).SelectMany(node => node.Points),
            point => point.Reference == "XCBR1.Pos.ctlModel" && point.InitialValue == "0");
        Assert.Contains(profile.LogicalDevices.SelectMany(device => device.LogicalNodes).SelectMany(node => node.Points),
            point => point.Reference == "XCBR1.Str.dirGeneral" && point.InitialValue == "0");
        Assert.Contains(profile.LogicalDevices.SelectMany(device => device.LogicalNodes).SelectMany(node => node.Points),
            point => point.Reference == "XCBR1.Beh.stVal" && point.InitialValue == "1");
        Assert.Contains(profile.ReportControlBlocks,
            rcb => rcb.Reference == "SIE7SR5PROT/CSWI1.RP.urcbA" && rcb.ConfRev == 7);
    }

    [Fact]
    public void FromScl_Derived_Profile_Steps_Without_Error()
    {
        var profile = Build().Profile;
        var engine = new IedSimulatorEngine(profile);
        var start = DateTimeOffset.UtcNow;

        for (var i = 0; i < 10; i++)
            engine.Step(start.AddMilliseconds(i * 20));

        var snapshot = engine.CreateSnapshot(start.AddSeconds(1));
        Assert.Equal(profile.PointCount, snapshot.Points.Count);
    }

    private static IedSimulatorProfileFromSclResult Build()
        => new IedSimulatorProfileBuilder().FromScl(Document());

    private static SclDocument Document()
        => new SclParser().Load(SclParserTests.MinimalStationPath());
}
