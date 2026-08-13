using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850DesignLiveProbeOutcomeTests
{
    [Theory]
    [InlineData(Iec61850ExactProbeStatus.InvalidTarget, Iec61850DesignLiveStatus.InvalidTarget)]
    [InlineData(Iec61850ExactProbeStatus.Unreadable, Iec61850DesignLiveStatus.Unreadable)]
    [InlineData(Iec61850ExactProbeStatus.TransportFailure, Iec61850DesignLiveStatus.TransportFailure)]
    public async Task Probe_Failure_Outcomes_Are_Not_Reported_As_Absent(
        Iec61850ExactProbeStatus probeStatus,
        Iec61850DesignLiveStatus expectedStatus)
    {
        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(
            BuildMandatoryDesign(),
            EmptyLive(),
            new FixedProbe(probeStatus));

        var point = Assert.Single(result.Points, x => x.IsDataSetMandatory && x.IsPrimaryValue);
        Assert.Equal(expectedStatus, point.Status);
        Assert.Equal(0, result.AbsentCount);
        Assert.False(result.HasConfirmedAbsence);
        if (probeStatus == Iec61850ExactProbeStatus.InvalidTarget)
            Assert.Equal(1, result.InvalidTargetCount);
    }

    [Fact]
    public async Task Alternate_Sibling_Read_Recovers_Canonical_Measurement_Target()
    {
        const string canonical = "IEDLD0/MMXU1$MX$TotW$mag$f";
        const string alternate = "IEDLD0/MMXU1$MX$TotW$instMag$f";
        var probe = new ReferenceProbe(canonical, Iec61850ExactProbeStatus.Absent, alternate, Iec61850ExactProbeStatus.Readable);

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(
            BuildMeasurementDesign(canonical),
            EmptyLive(),
            probe,
            new Iec61850DesignLiveReconciliationOptions { ProbeAllMissingDesignAttributes = true });

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.RecoveredByAlternateProbe, point.Status);
        Assert.Equal(canonical, point.CanonicalMmsReference);
        Assert.Equal(alternate, point.EffectiveMmsReference);
        Assert.Equal(2, point.ProbeAttempts.Count);
        Assert.Equal(Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling, point.ProbeAttempts[1].AlternateStrategy);
        Assert.Equal(1, result.Coverage.RecoveredByAlternateProbeCount);
        Assert.Equal(1, result.Coverage.ReadableCount);
        Assert.Equal(0, result.AbsentCount);
    }

    [Fact]
    public async Task Canonical_And_Known_Alternate_Must_Both_Be_Absent_For_Final_Absent()
    {
        const string canonical = "IEDLD0/MMXU1$MX$TotW$mag$f";
        const string alternate = "IEDLD0/MMXU1$MX$TotW$instMag$f";
        var probe = new ReferenceProbe(canonical, Iec61850ExactProbeStatus.Absent, alternate, Iec61850ExactProbeStatus.Absent);

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(
            BuildMeasurementDesign(canonical),
            EmptyLive(),
            probe,
            new Iec61850DesignLiveReconciliationOptions { ProbeAllMissingDesignAttributes = true });

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.Absent, point.Status);
        Assert.Equal(2, point.ProbeAttempts.Count);
        Assert.True(result.HasConfirmedAbsence);
    }

    [Theory]
    [InlineData(Iec61850ExactProbeStatus.InvalidTarget, Iec61850DesignLiveStatus.InvalidTarget)]
    [InlineData(Iec61850ExactProbeStatus.Unreadable, Iec61850DesignLiveStatus.Unreadable)]
    public async Task Non_Absent_Alternate_Evidence_Blocks_False_Absent(
        Iec61850ExactProbeStatus alternateOutcome,
        Iec61850DesignLiveStatus expectedStatus)
    {
        const string canonical = "IEDLD0/MMXU1$MX$TotW$mag$f";
        const string alternate = "IEDLD0/MMXU1$MX$TotW$instMag$f";
        var probe = new ReferenceProbe(canonical, Iec61850ExactProbeStatus.Absent, alternate, alternateOutcome);

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(
            BuildMeasurementDesign(canonical),
            EmptyLive(),
            probe,
            new Iec61850DesignLiveReconciliationOptions { ProbeAllMissingDesignAttributes = true });

        Assert.Equal(expectedStatus, Assert.Single(result.Points).Status);
        Assert.Equal(0, result.AbsentCount);
        Assert.False(result.HasConfirmedAbsence);
    }

    private static LiveIedModelDiscoveryDocument BuildMandatoryDesign()
        => new()
        {
            Source = "SclWorkspace",
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMTR1",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IEDLD0/MMTR1.SupWh",
                                    Name = "SupWh",
                                    InferredCdc = "BCR"
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

    private static LiveIedModelDiscoveryDocument BuildMeasurementDesign(string mmsReference)
        => new()
        {
            Source = "SclWorkspace",
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMXU1",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IEDLD0/MMXU1.TotW",
                                    Name = "TotW",
                                    InferredCdc = "MV",
                                    Attributes = new[]
                                    {
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = "IEDLD0/MMXU1.TotW.mag.f",
                                            AttributePath = "mag.f",
                                            FunctionalConstraint = "MX",
                                            MmsReference = mmsReference,
                                            MmsItemName = "MMXU1$MX$TotW$mag$f",
                                            SclBType = "FLOAT32",
                                            Source = "SclWorkspace"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

    private static LiveIedModelDiscoveryDocument EmptyLive()
        => new() { Source = "LiveMmsDiscovery", IedName = "IED" };

    private sealed class FixedProbe : IIec61850ExactReadProbe
    {
        private readonly Iec61850ExactProbeStatus _status;

        public FixedProbe(Iec61850ExactProbeStatus status) => _status = status;

        public Task<Iec61850ExactProbeEvidence> ProbeAsync(
            string mmsReference,
            string functionalConstraint,
            CancellationToken cancellationToken = default)
            => Task.FromResult(BuildEvidence(mmsReference, functionalConstraint, _status));
    }

    private sealed class ReferenceProbe : IIec61850ExactReadProbe
    {
        private readonly string _canonical;
        private readonly Iec61850ExactProbeStatus _canonicalOutcome;
        private readonly string _alternate;
        private readonly Iec61850ExactProbeStatus _alternateOutcome;

        public ReferenceProbe(
            string canonical,
            Iec61850ExactProbeStatus canonicalOutcome,
            string alternate,
            Iec61850ExactProbeStatus alternateOutcome)
        {
            _canonical = canonical;
            _canonicalOutcome = canonicalOutcome;
            _alternate = alternate;
            _alternateOutcome = alternateOutcome;
        }

        public Task<Iec61850ExactProbeEvidence> ProbeAsync(
            string mmsReference,
            string functionalConstraint,
            CancellationToken cancellationToken = default)
        {
            var status = string.Equals(mmsReference, _canonical, StringComparison.OrdinalIgnoreCase)
                ? _canonicalOutcome
                : string.Equals(mmsReference, _alternate, StringComparison.OrdinalIgnoreCase)
                    ? _alternateOutcome
                    : Iec61850ExactProbeStatus.InvalidTarget;
            return Task.FromResult(BuildEvidence(mmsReference, functionalConstraint, status));
        }
    }

    private static Iec61850ExactProbeEvidence BuildEvidence(
        string mmsReference,
        string functionalConstraint,
        Iec61850ExactProbeStatus status)
        => new()
        {
            Status = status,
            MmsReference = mmsReference,
            FunctionalConstraint = functionalConstraint,
            FailureCode = status == Iec61850ExactProbeStatus.Absent ? 10 : null,
            ValueSummary = status == Iec61850ExactProbeStatus.Readable ? "42" : string.Empty,
            Message = status.ToString()
        };
}
