using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Tests.Engineering;

public sealed class CanonicalStaticReportCoverageResolverTests
{
    [Fact]
    public void Discovery_And_Scl_Ingress_Use_The_Same_Static_Coverage_Contract()
    {
        var discovery = BuildCanonical();
        var scl = WithIngress(discovery, CanonicalIngressKind.SclFile);

        var selection = new[]
        {
            new CanonicalStaticReportSelection
            {
                Reference = "IEDALD0/MMXU1.A.phsA.cVal.mag.f",
                FunctionalConstraint = "MX"
            }
        };

        var fromDiscovery = CanonicalStaticReportCoverageResolver.Resolve(discovery, selection);
        var fromScl = CanonicalStaticReportCoverageResolver.Resolve(scl, selection);

        Assert.Equal(CanonicalIngressKind.LiveMmsDiscovery, fromDiscovery.Ingress);
        Assert.Equal(CanonicalIngressKind.SclFile, fromScl.Ingress);
        Assert.Equal(fromDiscovery.CoveredSignalCount, fromScl.CoveredSignalCount);
        Assert.Equal(
            fromDiscovery.Signals[0].DataSetCandidates.Select(candidate => candidate.DataSetReference),
            fromScl.Signals[0].DataSetCandidates.Select(candidate => candidate.DataSetReference));
        Assert.Equal(
            fromDiscovery.Signals[0].DataSetCandidates[0].ReportControlReferences,
            fromScl.Signals[0].DataSetCandidates[0].ReportControlReferences);
    }

    [Fact]
    public void Structured_Member_Covers_Only_Exact_Boundary_Descendants()
    {
        var model = BuildCanonical();
        var plan = CanonicalStaticReportCoverageResolver.Resolve(
            model,
            [
                new CanonicalStaticReportSelection
                {
                    Reference = "IEDALD0/MMXU1.A.phsA.cVal.mag.f",
                    FunctionalConstraint = "MX"
                },
                new CanonicalStaticReportSelection
                {
                    Reference = "IEDALD0/MMXU1.A.phsB.cVal.mag.f",
                    FunctionalConstraint = "MX"
                }
            ]);

        Assert.Equal(CanonicalStaticReportSignalCoverageStatus.Covered, plan.Signals[0].Status);
        Assert.Equal([0], plan.Signals[0].DataSetCandidates[0].MemberIndexes);
        Assert.Equal(
            CanonicalStaticReportSignalCoverageStatus.NoStaticDataSetMembership,
            plan.Signals[1].Status);
    }

    [Fact]
    public void Segment_Preserves_Full_Ordered_DataSet_And_All_Configured_Rcbs()
    {
        var model = BuildCanonical();
        var plan = CanonicalStaticReportCoverageResolver.Resolve(
            model,
            [
                new CanonicalStaticReportSelection
                {
                    Reference = "IEDALD0/MMXU1.A.phsA.cVal.mag.f",
                    FunctionalConstraint = "MX"
                }
            ]);

        var segment = Assert.Single(plan.Segments);
        Assert.Equal("IEDALD0/LLN0.Analog", segment.DataSetReference);
        Assert.Equal(2, segment.OrderedMembers.Length);
        Assert.Equal(0, segment.OrderedMembers[0].Index);
        Assert.Equal(1, segment.OrderedMembers[1].Index);
        Assert.Equal(
            new[]
            {
                "IEDALD0/LLN0.BR.Buffer01",
                "IEDALD0/LLN0.RP.Unbuffer01"
            },
            segment.ReportControls.Select(report => report.Reference));
        Assert.Equal(
            ["IEDALD0/MMXU1.A.phsA.cVal.mag.f"],
            segment.SelectedSignalReferences);
    }

    [Fact]
    public void Canonical_Identity_Remains_Exact_Case()
    {
        var model = BuildCanonical();
        var plan = CanonicalStaticReportCoverageResolver.Resolve(
            model,
            [
                new CanonicalStaticReportSelection
                {
                    Reference = "iedald0/MMXU1.A.phsA.cVal.mag.f",
                    FunctionalConstraint = "MX"
                }
            ]);

        Assert.Equal(CanonicalStaticReportSignalCoverageStatus.SignalNotFound, plan.Signals[0].Status);
        Assert.Equal(0, plan.CoveredSignalCount);
    }

    [Fact]
    public void DataSet_Without_Rcb_Is_Not_Promoted_To_Report_Coverage()
    {
        var model = BuildCanonical();
        var withoutReports = new CanonicalIedModel
        {
            SchemaVersion = model.SchemaVersion,
            GeneratedAtUtc = model.GeneratedAtUtc,
            Identity = model.Identity,
            Communication = model.Communication,
            Source = model.Source,
            Strings = model.Strings,
            AccessPoints = model.AccessPoints,
            LogicalDevices = model.LogicalDevices,
            LogicalNodes = model.LogicalNodes,
            DataObjects = model.DataObjects,
            Signals = model.Signals,
            DataSets = model.DataSets,
            ReportControls = Array.Empty<CanonicalReportControl>(),
            Diagnostics = model.Diagnostics
        };

        var plan = CanonicalStaticReportCoverageResolver.Resolve(
            withoutReports,
            [
                new CanonicalStaticReportSelection
                {
                    Reference = "IEDALD0/MMXU1.A.phsA.cVal.mag.f",
                    FunctionalConstraint = "MX"
                }
            ]);

        Assert.Equal(
            CanonicalStaticReportSignalCoverageStatus.StaticDataSetWithoutReportControl,
            plan.Signals[0].Status);
        Assert.Empty(plan.Segments);
    }

    private static CanonicalIedModel BuildCanonical()
    {
        var live = new LiveIedModelDiscoveryDocument
        {
            Source = "LiveMmsDiscovery",
            IedName = "IEDA",
            IedIdentity = new LiveIedIdentity
            {
                IedName = "IEDA",
                Confidence = LiveIedDiscoveryConfidenceLevel.Exact
            },
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDALD0",
                    Inst = "LD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMXU1",
                            LnClass = "MMXU",
                            LnInst = "1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IEDALD0/MMXU1.A",
                                    Name = "A",
                                    InferredCdc = "WYE",
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Exact,
                                    Attributes =
                                    [
                                        Attribute("IEDALD0/MMXU1.A.phsA.cVal.mag.f", "phsA.cVal.mag.f"),
                                        Attribute("IEDALD0/MMXU1.A.phsB.cVal.mag.f", "phsB.cVal.mag.f")
                                    ]
                                },
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IEDALD0/MMXU1.TotW",
                                    Name = "TotW",
                                    InferredCdc = "MV",
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Exact,
                                    Attributes =
                                    [
                                        Attribute("IEDALD0/MMXU1.TotW.mag.f", "mag.f")
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ],
            DataSets =
            [
                new LiveIedDataSetModel
                {
                    Reference = "IEDALD0/LLN0.Analog",
                    Domain = "IEDALD0",
                    LogicalNode = "LLN0",
                    Name = "Analog",
                    MemberCount = 2,
                    Members =
                    [
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = "IEDALD0/MMXU1.A.phsA",
                            FunctionalConstraint = "MX"
                        },
                        new LiveIedDataSetMemberModel
                        {
                            Index = 1,
                            Reference = "IEDALD0/MMXU1.TotW",
                            FunctionalConstraint = "MX"
                        }
                    ]
                }
            ],
            ReportControls =
            [
                new LiveIedReportControlModel
                {
                    Reference = "IEDALD0/LLN0.BR.Buffer01",
                    Domain = "IEDALD0",
                    LogicalNode = "LLN0",
                    Name = "Buffer01",
                    Buffered = true,
                    DataSetReference = "IEDALD0/LLN0.Analog"
                },
                new LiveIedReportControlModel
                {
                    Reference = "IEDALD0/LLN0.RP.Unbuffer01",
                    Domain = "IEDALD0",
                    LogicalNode = "LLN0",
                    Name = "Unbuffer01",
                    Buffered = false,
                    DataSetReference = "IEDALD0/LLN0.Analog"
                }
            ]
        };

        return CanonicalLiveModelAdapter.FromLiveDiscovery(live);
    }

    private static LiveIedDataAttributeModel Attribute(string reference, string path)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = "MX",
            SclBType = "FLOAT32",
            MmsType = "floating-point",
            MmsTypeSignature = "floating-point",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

    private static CanonicalIedModel WithIngress(
        CanonicalIedModel model,
        CanonicalIngressKind ingress)
        => new()
        {
            SchemaVersion = model.SchemaVersion,
            GeneratedAtUtc = model.GeneratedAtUtc,
            Identity = model.Identity,
            Communication = model.Communication,
            Source = new CanonicalSourceEnvelope
            {
                Ingress = ingress,
                SourceName = ingress == CanonicalIngressKind.SclFile ? "sample.scd" : "LiveMmsDiscovery"
            },
            Strings = model.Strings,
            AccessPoints = model.AccessPoints,
            LogicalDevices = model.LogicalDevices,
            LogicalNodes = model.LogicalNodes,
            DataObjects = model.DataObjects,
            Signals = model.Signals,
            DataSets = model.DataSets,
            ReportControls = model.ReportControls,
            Diagnostics = model.Diagnostics
        };
}
