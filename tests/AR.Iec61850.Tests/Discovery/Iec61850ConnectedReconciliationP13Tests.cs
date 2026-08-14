using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850ConnectedReconciliationP13Tests
{
    [Fact]
    public async Task Alternate_Magnitude_Already_In_Discovery_Is_Recovered_Without_Probe()
    {
        const string canonical = "IEDLD0/MMXU1$MX$TotW$mag$f";
        const string alternate = "IEDLD0/MMXU1$MX$TotW$instMag$f";
        var design = BuildModel(new TestAttribute("IEDLD0/MMXU1.TotW.mag.f", canonical, "MX", "FLOAT32", string.Empty));
        var observed = BuildModel(new TestAttribute("IEDLD0/MMXU1.TotW.instMag.f", alternate, "MX", "FLOAT32", "floating-point"), "LiveMmsDiscovery");
        var probe = new CountingProbe(Iec61850ExactProbeStatus.Absent);

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(
            design,
            observed,
            probe,
            new Iec61850DesignLiveReconciliationOptions { ProbeAllMissingDesignAttributes = true });

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery, point.Status);
        Assert.Equal(canonical, point.CanonicalMmsReference);
        Assert.Equal(alternate, point.EffectiveMmsReference);
        Assert.Equal(alternate, point.ObservedMmsReference);
        Assert.Equal(Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling, point.AlternateStrategy);
        Assert.Equal(0, probe.CallCount);
        Assert.Equal(0, result.LiveOnlyCount);
        Assert.Equal(1, result.RecoveredByAlternateDiscoveryCount);
        Assert.Equal(1, result.Coverage.ReadableCount);
    }

    [Fact]
    public async Task Alternate_Complex_Value_Already_In_Discovery_Is_Recovered_Without_Probe()
    {
        const string canonical = "IEDLD0/MMXU1$MX$A$phsA$cVal$mag$f";
        const string alternate = "IEDLD0/MMXU1$MX$A$phsA$instCVal$mag$f";
        var design = BuildModel(new TestAttribute("IEDLD0/MMXU1.A.phsA.cVal.mag.f", canonical, "MX", "FLOAT32", string.Empty));
        var observed = BuildModel(new TestAttribute("IEDLD0/MMXU1.A.phsA.instCVal.mag.f", alternate, "MX", "FLOAT32", "floating-point"), "LiveMmsDiscovery");
        var probe = new CountingProbe(Iec61850ExactProbeStatus.Absent);

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(
            design,
            observed,
            probe,
            new Iec61850DesignLiveReconciliationOptions { ProbeAllMissingDesignAttributes = true });

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery, point.Status);
        Assert.Equal(Iec61850AlternateReferenceStrategyKind.ComplexValueInstantaneousSibling, point.AlternateStrategy);
        Assert.Equal(alternate, point.EffectiveMmsReference);
        Assert.Equal(0, probe.CallCount);
        Assert.Equal(0, result.LiveOnlyCount);
    }

    [Fact]
    public async Task Alternate_Discovery_With_Explicit_Type_Conflict_Is_Not_Present()
    {
        const string canonical = "IEDLD0/MMXU1$MX$TotW$mag$f";
        const string alternate = "IEDLD0/MMXU1$MX$TotW$instMag$f";
        var design = BuildModel(new TestAttribute("IEDLD0/MMXU1.TotW.mag.f", canonical, "MX", "FLOAT32", string.Empty));
        var observed = BuildModel(new TestAttribute("IEDLD0/MMXU1.TotW.instMag.f", alternate, "MX", "FLOAT64", "floating-point"), "LiveMmsDiscovery");
        var probe = new CountingProbe(Iec61850ExactProbeStatus.Readable);

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(
            design,
            observed,
            probe,
            new Iec61850DesignLiveReconciliationOptions { ProbeAllMissingDesignAttributes = true });

        var point = Assert.Single(result.Points);
        Assert.Equal(Iec61850DesignLiveStatus.TypeMismatch, point.Status);
        Assert.Equal(alternate, point.ObservedMmsReference);
        Assert.Equal(Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling, point.AlternateStrategy);
        Assert.Equal(0, probe.CallCount);
        Assert.Equal(0, result.LiveOnlyCount);
    }

    [Fact]
    public async Task Probe_Budget_Defers_Extra_Targets_Without_Creating_Absence()
    {
        var design = BuildModel(
            new TestAttribute("IEDLD0/GGIO1.Ind1.stVal", "IEDLD0/GGIO1$ST$Ind1$stVal", "ST", "BOOLEAN", string.Empty),
            new TestAttribute("IEDLD0/GGIO1.Ind2.stVal", "IEDLD0/GGIO1$ST$Ind2$stVal", "ST", "BOOLEAN", string.Empty));
        var probe = new CountingProbe(Iec61850ExactProbeStatus.Absent);

        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(
            design,
            EmptyLive(),
            probe,
            new Iec61850DesignLiveReconciliationOptions
            {
                ProbeAllMissingDesignAttributes = true,
                ProbeKnownAlternateReferences = false,
                MaxProbeTargetCount = 1
            });

        Assert.Equal(1, probe.CallCount);
        Assert.Equal(1, result.AbsentCount);
        Assert.Equal(1, result.ProbeBudgetDeferredCount);
        var deferred = Assert.Single(result.Points, point => point.ProbeDeferredByBudget);
        Assert.Equal(Iec61850DesignLiveStatus.DesignOnly, deferred.Status);
        Assert.Contains("budget", deferred.Evidence.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Connected_Service_On_Disconnected_Session_Returns_Transport_Diagnostic_Not_Exception()
    {
        await using var session = new MmsClientSession();
        var service = new Iec61850ConnectedReconciliationService(session);
        var design = BuildModel(new TestAttribute(
            "IEDLD0/GGIO1.Ind1.stVal",
            "IEDLD0/GGIO1$ST$Ind1$stVal",
            "ST",
            "BOOLEAN",
            string.Empty));

        var result = await service.ReconcileAsync(
            design,
            EmptyLive(),
            new Iec61850DesignLiveReconciliationOptions
            {
                ProbeAllMissingDesignAttributes = true,
                ProbeKnownAlternateReferences = false,
                MaxProbeTargetCount = 1
            });

        var point = Assert.Single(result.Points);
        Assert.False(service.IsSessionReady);
        Assert.Equal(MmsAssociationState.Disconnected, service.SessionState);
        Assert.Equal(Iec61850DesignLiveStatus.TransportFailure, point.Status);
        Assert.Equal(Iec61850ExactProbeStatus.TransportFailure, point.Probe?.Status);
        Assert.Equal(0, result.AbsentCount);
    }

    [Theory]
    [InlineData(3, Iec61850ExactProbeStatus.Unreadable)]
    [InlineData(4, Iec61850ExactProbeStatus.Absent)]
    [InlineData(5, Iec61850ExactProbeStatus.InvalidTarget)]
    [InlineData(10, Iec61850ExactProbeStatus.Absent)]
    public void Exact_Probe_Classifier_Owns_Mms_Failure_Code_Mapping(
        int failureCode,
        Iec61850ExactProbeStatus expected)
    {
        var status = Iec61850ExactProbeOutcomeClassifier.Classify(
            new MmsReadResult { IsSuccess = false, FailureCode = failureCode },
            sessionInitiatedAfterRead: true);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void Exact_Probe_Classifier_Prioritizes_Transport_State_After_Failed_Read()
    {
        var status = Iec61850ExactProbeOutcomeClassifier.Classify(
            new MmsReadResult { IsSuccess = false, FailureCode = 10 },
            sessionInitiatedAfterRead: false);

        Assert.Equal(Iec61850ExactProbeStatus.TransportFailure, status);
    }

    [Fact]
    public void Exact_Probe_Classifier_Preserves_Success_Even_If_Session_Changes_After_Read()
    {
        var status = Iec61850ExactProbeOutcomeClassifier.Classify(
            new MmsReadResult { IsSuccess = true },
            sessionInitiatedAfterRead: false);

        Assert.Equal(Iec61850ExactProbeStatus.Readable, status);
    }

    [Fact]
    public void New_Reconciliation_Status_Is_Appended_Without_Shifting_P1_Ordinals()
    {
        Assert.Equal(13, (int)Iec61850DesignLiveStatus.RecoveredByAlternateProbe);
        Assert.Equal(14, (int)Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery);
    }

    private static LiveIedModelDiscoveryDocument BuildModel(
        TestAttribute attribute,
        string source = "SclWorkspace")
        => BuildModel(new[] { attribute }, source);

    private static LiveIedModelDiscoveryDocument BuildModel(
        TestAttribute first,
        TestAttribute second)
        => BuildModel(new[] { first, second }, "SclWorkspace");

    private static LiveIedModelDiscoveryDocument BuildModel(
        IReadOnlyCollection<TestAttribute> attributes,
        string source)
    {
        var models = attributes.Select((attribute, index) =>
        {
            var slash = attribute.MmsReference.IndexOf('/');
            var item = attribute.MmsReference[(slash + 1)..];
            var logicalNode = item.Split('$', StringSplitOptions.RemoveEmptyEntries)[0];
            var domain = attribute.MmsReference[..slash];
            return new
            {
                Domain = domain,
                LogicalNode = logicalNode,
                DataObject = new LiveIedDataObjectModel
                {
                    Reference = $"{domain}/{logicalNode}.DO{index + 1}",
                    Name = $"DO{index + 1}",
                    InferredCdc = "MV",
                    Attributes = new[]
                    {
                        new LiveIedDataAttributeModel
                        {
                            ObjectReference = attribute.ObjectReference,
                            AttributePath = attribute.ObjectReference[(attribute.ObjectReference.LastIndexOf('.') + 1)..],
                            FunctionalConstraint = attribute.FunctionalConstraint,
                            MmsReference = attribute.MmsReference,
                            MmsItemName = item,
                            SclBType = attribute.SclBType,
                            MmsType = attribute.MmsType,
                            Source = source
                        }
                    }
                }
            };
        }).ToArray();

        return new LiveIedModelDiscoveryDocument
        {
            Source = source,
            IedName = "IED",
            LogicalDevices = models
                .GroupBy(model => model.Domain, StringComparer.OrdinalIgnoreCase)
                .Select(domain => new LiveIedLogicalDeviceModel
                {
                    MmsDomain = domain.Key,
                    LogicalNodes = domain
                        .GroupBy(model => model.LogicalNode, StringComparer.OrdinalIgnoreCase)
                        .Select(node => new LiveIedLogicalNodeModel
                        {
                            Name = node.Key,
                            DataObjects = node.Select(model => model.DataObject).ToArray()
                        })
                        .ToArray()
                })
                .ToArray()
        };
    }

    private static LiveIedModelDiscoveryDocument EmptyLive()
        => new() { Source = "LiveMmsDiscovery", IedName = "IED" };

    private sealed record TestAttribute(
        string ObjectReference,
        string MmsReference,
        string FunctionalConstraint,
        string SclBType,
        string MmsType);

    private sealed class CountingProbe : IIec61850ExactReadProbe
    {
        private readonly Iec61850ExactProbeStatus _status;

        public CountingProbe(Iec61850ExactProbeStatus status) => _status = status;

        public int CallCount { get; private set; }

        public Task<Iec61850ExactProbeEvidence> ProbeAsync(
            string mmsReference,
            string functionalConstraint,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new Iec61850ExactProbeEvidence
            {
                Status = _status,
                MmsReference = mmsReference,
                FunctionalConstraint = functionalConstraint,
                FailureCode = _status == Iec61850ExactProbeStatus.Absent ? 10 : null,
                Message = _status.ToString()
            });
        }
    }
}
