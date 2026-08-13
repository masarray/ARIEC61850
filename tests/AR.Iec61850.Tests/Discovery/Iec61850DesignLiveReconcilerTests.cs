using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850DesignLiveReconcilerTests
{
    [Fact]
    public async Task Missing_Discovery_Bcr_Primary_Is_Recovered_By_Exact_Probe()
    {
        var design = BuildShallowBcrDesign();
        var observed = EmptyLive();
        var probe = new FakeProbe(Iec61850ExactProbeStatus.Readable);

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(design, observed, probe);

        var point = Assert.Single(result.Points, x => x.IsDataSetMandatory && x.IsPrimaryValue);
        Assert.Equal(Iec61850DesignLiveStatus.RecoveredByProbe, point.Status);
        Assert.EndsWith("MMTR1$ST$SupWh$actVal", point.MmsReference, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, result.RecoveredByProbeCount);
        Assert.Equal(0, result.AbsentCount);
        Assert.Equal(point.MmsReference, probe.LastReference);
        Assert.Equal(1, result.Coverage.MandatoryPrimaryPointCount);
        Assert.Equal(1, result.Coverage.MandatoryPrimaryRecoveredByProbeCount);
        Assert.Equal(1, result.Coverage.MandatoryPrimaryReadableCount);
        Assert.False(result.Coverage.HasConfirmedMandatoryAbsence);
    }

    [Fact]
    public async Task Missing_Discovery_Without_Probe_Is_DesignOnly_Not_Absent()
    {
        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(BuildShallowBcrDesign(), EmptyLive());

        var point = Assert.Single(result.Points, x => x.IsDataSetMandatory && x.IsPrimaryValue);
        Assert.Equal(Iec61850DesignLiveStatus.DesignOnly, point.Status);
        Assert.Equal(0, result.AbsentCount);
        Assert.False(result.HasConfirmedAbsence);
        Assert.Equal(1, result.Coverage.MandatoryPrimaryDesignOnlyCount);
        Assert.Equal(0, result.Coverage.MandatoryPrimaryReadableCount);
    }

    [Fact]
    public async Task Protocol_Object_Non_Existent_Is_Confirmed_Absent()
    {
        var probe = new FakeProbe(Iec61850ExactProbeStatus.Absent, failureCode: 10);

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(BuildShallowBcrDesign(), EmptyLive(), probe);

        var point = Assert.Single(result.Points, x => x.IsDataSetMandatory && x.IsPrimaryValue);
        Assert.Equal(Iec61850DesignLiveStatus.Absent, point.Status);
        Assert.Equal(10, point.Probe?.FailureCode);
        Assert.True(result.HasConfirmedAbsence);
        Assert.Equal(1, result.Coverage.MandatoryPrimaryAbsentCount);
        Assert.True(result.Coverage.HasConfirmedMandatoryAbsence);
    }

    [Fact]
    public async Task Same_Object_Under_Different_Fc_Is_Mismatch_Without_Probe()
    {
        var design = BuildSingleAttribute("IEDLD0/CSWI1.Pos.stVal", "ST", "BOOLEAN", "IEDLD0/CSWI1$ST$Pos$stVal");
        var observed = BuildSingleAttribute("IEDLD0/CSWI1.Pos.stVal", "MX", "BOOLEAN", "IEDLD0/CSWI1$MX$Pos$stVal", source: "LiveMmsDiscovery");
        var probe = new FakeProbe(Iec61850ExactProbeStatus.Readable);

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(design, observed, probe,
            new Iec61850DesignLiveReconciliationOptions { ProbeAllMissingDesignAttributes = true });

        var point = Assert.Single(result.Points, x => x.Status == Iec61850DesignLiveStatus.FunctionalConstraintMismatch);
        Assert.Equal("ST", point.FunctionalConstraint);
        Assert.Equal("MX", point.ObservedFunctionalConstraint);
        Assert.Equal(0, probe.CallCount);
        Assert.Equal(1, result.Coverage.FunctionalConstraintMismatchCount);
    }

    [Fact]
    public async Task Exact_Reference_With_Conflicting_Type_Is_TypeMismatch()
    {
        var design = BuildSingleAttribute("IEDLD0/GGIO1.Ind1.stVal", "ST", "BOOLEAN", "IEDLD0/GGIO1$ST$Ind1$stVal");
        var observed = BuildSingleAttribute("IEDLD0/GGIO1.Ind1.stVal", "ST", "INT32", "IEDLD0/GGIO1$ST$Ind1$stVal", source: "LiveMmsDiscovery");

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(design, observed);

        var point = Assert.Single(result.Points, x => x.Status == Iec61850DesignLiveStatus.TypeMismatch);
        Assert.Equal("IEDLD0/GGIO1$ST$Ind1$stVal", point.ObservedMmsReference);
        Assert.Equal(1, result.Coverage.TypeMismatchCount);
    }

    [Fact]
    public async Task Generic_Mms_Integer_Is_Compatible_With_Scl_Int64()
    {
        const string reference = "IEDLD0/MMTR1.SupWh.actVal";
        const string mmsReference = "IEDLD0/MMTR1$ST$SupWh$actVal";
        var design = BuildTypedAttribute(reference, "ST", "INT64", string.Empty, mmsReference, "SclWorkspace");
        var observed = BuildTypedAttribute(reference, "ST", "INT32", "integer", mmsReference, "LiveMmsDiscovery");

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(design, observed);

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.Compatible, point.Status);
        Assert.Equal(0, result.Coverage.TypeMismatchCount);
        Assert.Equal(1, result.Coverage.DirectlyMatchedCount);
    }

    [Fact]
    public async Task Explicit_Float_Width_Conflict_Remains_TypeMismatch()
    {
        const string reference = "IEDLD0/MMXU1.TotW.mag.f";
        const string mmsReference = "IEDLD0/MMXU1$MX$TotW$mag$f";
        var design = BuildTypedAttribute(reference, "MX", "FLOAT32", string.Empty, mmsReference, "SclWorkspace");
        var observed = BuildTypedAttribute(reference, "MX", "FLOAT64", "floating-point", mmsReference, "LiveMmsDiscovery");

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(design, observed);

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.TypeMismatch, point.Status);
        Assert.Equal(1, result.Coverage.TypeMismatchCount);
    }

    [Fact]
    public async Task Observed_Attribute_Not_In_Design_Is_LiveOnly()
    {
        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(
            EmptyLive(source: "SclWorkspace"),
            BuildSingleAttribute("IEDLD0/GGIO1.Ind2.stVal", "ST", "BOOLEAN", "IEDLD0/GGIO1$ST$Ind2$stVal", source: "LiveMmsDiscovery"));

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.LiveOnly, point.Status);
        Assert.Equal(1, result.LiveOnlyCount);
        Assert.Equal(1, result.Coverage.LiveOnlyCount);
        Assert.Equal(0, result.Coverage.DesignPointCount);
    }

    private static LiveIedModelDiscoveryDocument BuildShallowBcrDesign()
        => new()
        {
            Source = "SclWorkspace",
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0",
                    Inst = "LD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMTR1",
                            LnClass = "MMTR",
                            LnInst = "1",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IEDLD0/MMTR1.SupWh",
                                    Name = "SupWh",
                                    InferredCdc = "BCR",
                                    Attributes = Array.Empty<LiveIedDataAttributeModel>()
                                }
                            }
                        }
                    }
                }
            },
            DataSets = new[]
            {
                new LiveIedDataSetModel
                {
                    Reference = "IEDLD0/LLN0.dsEnergy",
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "dsEnergy",
                    MemberCount = 1,
                    Members = new[]
                    {
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = "IEDLD0/MMTR1.SupWh",
                            FunctionalConstraint = "ST"
                        }
                    }
                }
            }
        };

    private static LiveIedModelDiscoveryDocument BuildSingleAttribute(
        string objectReference,
        string fc,
        string type,
        string mmsReference,
        string source = "SclWorkspace")
        => BuildTypedAttribute(
            objectReference,
            fc,
            source.Contains("Scl", StringComparison.OrdinalIgnoreCase) ? type : string.Empty,
            source.Contains("Live", StringComparison.OrdinalIgnoreCase) ? type : string.Empty,
            mmsReference,
            source);

    private static LiveIedModelDiscoveryDocument BuildTypedAttribute(
        string objectReference,
        string fc,
        string sclBType,
        string mmsType,
        string mmsReference,
        string source)
    {
        var slash = mmsReference.IndexOf('/');
        var domain = mmsReference[..slash];
        var item = mmsReference[(slash + 1)..];
        var logicalNode = item.Split('$')[0];
        var objectName = objectReference[(objectReference.LastIndexOf('/') + 1)..].Split('.')[1];
        var lastDot = objectReference.LastIndexOf('.');
        var attributePath = lastDot >= 0 ? objectReference[(lastDot + 1)..] : objectReference;

        return new LiveIedModelDiscoveryDocument
        {
            Source = source,
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = domain,
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = logicalNode,
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = objectReference[..lastDot],
                                    Name = objectName,
                                    InferredCdc = "SPS",
                                    Attributes = new[]
                                    {
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = objectReference,
                                            AttributePath = attributePath,
                                            FunctionalConstraint = fc,
                                            MmsReference = mmsReference,
                                            MmsItemName = item,
                                            SclBType = sclBType,
                                            MmsType = mmsType,
                                            Source = source
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static LiveIedModelDiscoveryDocument EmptyLive(string source = "LiveMmsDiscovery")
        => new() { Source = source, IedName = "IED" };

    private sealed class FakeProbe : IIec61850ExactReadProbe
    {
        private readonly Iec61850ExactProbeStatus _status;
        private readonly int? _failureCode;

        public FakeProbe(Iec61850ExactProbeStatus status, int? failureCode = null)
        {
            _status = status;
            _failureCode = failureCode;
        }

        public int CallCount { get; private set; }
        public string LastReference { get; private set; } = string.Empty;

        public Task<Iec61850ExactProbeEvidence> ProbeAsync(
            string mmsReference,
            string functionalConstraint,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastReference = mmsReference;
            return Task.FromResult(new Iec61850ExactProbeEvidence
            {
                Status = _status,
                MmsReference = mmsReference,
                FunctionalConstraint = functionalConstraint,
                FailureCode = _failureCode,
                ValueSummary = _status == Iec61850ExactProbeStatus.Readable ? "42" : string.Empty,
                Message = _status.ToString()
            });
        }
    }
}
