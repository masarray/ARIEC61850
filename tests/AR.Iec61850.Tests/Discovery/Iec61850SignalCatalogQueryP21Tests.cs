using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850SignalCatalogQueryP21Tests
{
    [Fact]
    public void Selection_Api_Keeps_Unreconciled_DesignOnly_Transport_And_Absent_Distinct()
    {
        var catalog = new Iec61850SignalCatalogDocument
        {
            Signals = new[]
            {
                Signal("IEDLD0/GGIO1$ST$Ind6$stVal", Iec61850SignalCatalogResolutionStatus.LiveOnly, Iec61850DesignLiveStatus.LiveOnly),
                Signal("IEDLD0/GGIO1$ST$Ind5$stVal", liveStatus: null, engineeringOnly: true),
                Signal("IEDLD0/GGIO1$ST$Ind4$stVal", liveStatus: Iec61850DesignLiveStatus.TransportFailure, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind3$stVal", liveStatus: Iec61850DesignLiveStatus.Absent, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind2$stVal", liveStatus: Iec61850DesignLiveStatus.DesignOnly, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind7$stVal", liveStatus: Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind1$stVal", liveStatus: Iec61850DesignLiveStatus.Exact, mandatoryPrimary: true)
            }
        };

        Assert.Equal(6, catalog.GetDesignSignals().Count);
        Assert.Single(catalog.GetLiveOnlySignals());
        Assert.Equal(5, catalog.GetMandatoryPrimarySignals().Count);
        Assert.Equal(5, catalog.GetOperationalSignals().Count);
        Assert.Single(catalog.GetEngineeringOnlySignals());
        Assert.Equal(2, catalog.GetVerifiedPresentSignals().Count);
        Assert.Single(catalog.GetConfirmedAbsentSignals());
        Assert.Single(catalog.GetUnreconciledDesignSignals());
        Assert.Equal(5, catalog.GetReconciledDesignSignals().Count);
        Assert.Single(catalog.GetDesignOnlySignals());

        Assert.Equal(Iec61850DesignLiveStatus.Absent,
            Assert.Single(catalog.GetConfirmedAbsentSignals()).LiveStatus);
        Assert.Equal(Iec61850DesignLiveStatus.TransportFailure,
            Assert.Single(catalog.GetSignalsByLiveStatus(Iec61850DesignLiveStatus.TransportFailure)).LiveStatus);
        Assert.Null(Assert.Single(catalog.GetUnreconciledDesignSignals()).LiveStatus);
    }

    [Fact]
    public void Literal_Filters_Use_Existing_Catalog_Fields_And_Memberships()
    {
        var energyDataSet = new Iec61850SignalDataSetMembership
        {
            DataSetReference = "IEDLD0/LLN0.dsEnergy",
            MemberIndex = 7,
            FunctionalConstraint = "ST",
            Cdc = "BCR",
            IsPrimaryValueForMember = true
        };
        var energyReport = new Iec61850SignalReportMembership
        {
            ReportControlReference = "IEDLD0/LLN0.BR.Energy",
            DataSetReference = energyDataSet.DataSetReference,
            Buffered = true,
            ReportId = "ENERGY"
        };
        var measurementDataSet = new Iec61850SignalDataSetMembership
        {
            DataSetReference = "IEDLD0/LLN0.dsMeasurements",
            MemberIndex = 2,
            FunctionalConstraint = "MX",
            Cdc = "MV",
            IsPrimaryValueForMember = true
        };

        var catalog = new Iec61850SignalCatalogDocument
        {
            Signals = new[]
            {
                new Iec61850SignalDescriptor
                {
                    DesignReference = "IEDLD0/MMTR1.SupWh.actVal",
                    CanonicalMmsReference = "IEDLD0/MMTR1$ST$SupWh$actVal",
                    EffectiveMmsReference = "IEDLD0/MMTR1$ST$SupWh$actVal",
                    ObservedMmsReference = "IEDLD0/MMTR1$ST$SupWh$actVal",
                    FunctionalConstraint = "ST",
                    Cdc = "BCR",
                    LogicalNodeClass = "MMTR",
                    SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
                    IsStaticDataSetMandatory = true,
                    IsOperationalCandidate = true,
                    ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
                    LiveStatus = Iec61850DesignLiveStatus.Exact,
                    DataSetMemberships = new[] { energyDataSet },
                    ReportMemberships = new[] { energyReport }
                },
                new Iec61850SignalDescriptor
                {
                    DesignReference = "IEDLD0/MMTR1.SupWh.q",
                    CanonicalMmsReference = "IEDLD0/MMTR1$ST$SupWh$q",
                    EffectiveMmsReference = "IEDLD0/MMTR1$ST$SupWh$q",
                    FunctionalConstraint = "ST",
                    Cdc = "BCR",
                    LogicalNodeClass = "MMTR",
                    SemanticRole = Iec61850DataAttributeSemanticRole.Quality,
                    IsStaticDataSetMandatory = true,
                    ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
                    DataSetMemberships = new[] { energyDataSet },
                    ReportMemberships = new[] { energyReport }
                },
                new Iec61850SignalDescriptor
                {
                    DesignReference = "IEDLD0/MMXU1.TotW.mag.f",
                    CanonicalMmsReference = "IEDLD0/MMXU1$MX$TotW$mag$f",
                    EffectiveMmsReference = "IEDLD0/MMXU1$MX$TotW$instMag$f",
                    ObservedMmsReference = "IEDLD0/MMXU1$MX$TotW$instMag$f",
                    FunctionalConstraint = "MX",
                    Cdc = "MV",
                    LogicalNodeClass = "MMXU",
                    SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
                    IsStaticDataSetMandatory = true,
                    IsOperationalCandidate = true,
                    ResolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
                    LiveStatus = Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery,
                    DataSetMemberships = new[] { measurementDataSet }
                }
            }
        };

        Assert.Equal(2, catalog.GetSignalsByFunctionalConstraint(" st ").Count);
        Assert.Single(catalog.GetSignalsByFunctionalConstraint("MX"));
        Assert.Equal(2, catalog.GetSignalsByCdc("bcr").Count);
        Assert.Single(catalog.GetSignalsBySemanticRole(Iec61850DataAttributeSemanticRole.Quality));
        Assert.Equal(2, catalog.GetSignalsByLogicalNodeClass("mmtr").Count);
        Assert.Equal(2, catalog.GetSignalsByDataSetReference("iedld0/lln0.dsEnergy").Count);
        Assert.Equal(2, catalog.GetSignalsByReportControlReference("IEDLD0/LLN0.BR.Energy").Count);
        Assert.Single(catalog.GetSignalsByDesignReference("IEDLD0/MMTR1.SupWh.actVal"));
        Assert.Single(catalog.GetSignalsByObservedMmsReference("iedld0/mmxu1$mx$totw$instmag$f"));
        Assert.Single(catalog.GetSignalsByResolutionStatus(Iec61850SignalCatalogResolutionStatus.DesignAttribute),
            signal => signal.Cdc == "MV");

        Assert.Empty(catalog.GetSignalsByFunctionalConstraint(""));
        Assert.Empty(catalog.GetSignalsByDataSetReference(null));
    }

    [Fact]
    public void Coverage_Diagnostics_Count_Catalog_And_Reconciliation_Statuses_Without_Reclassification()
    {
        var report = new Iec61850SignalReportMembership
        {
            ReportControlReference = "IEDLD0/LLN0.BR.Fast",
            DataSetReference = "IEDLD0/LLN0.dsFast"
        };
        var dsA = new Iec61850SignalDataSetMembership { DataSetReference = "IEDLD0/LLN0.dsA", MemberIndex = 0 };
        var dsB = new Iec61850SignalDataSetMembership { DataSetReference = "IEDLD0/LLN0.dsB", MemberIndex = 1 };

        var catalog = new Iec61850SignalCatalogDocument
        {
            Signals = new[]
            {
                Signal("IEDLD0/GGIO1$ST$Ind1$stVal", liveStatus: Iec61850DesignLiveStatus.Exact, mandatoryPrimary: true,
                    dataSets: new[] { dsA, dsB }, reports: new[] { report, new Iec61850SignalReportMembership { ReportControlReference = "IEDLD0/LLN0.BR.Backup", DataSetReference = dsB.DataSetReference } }),
                Signal("IEDLD0/GGIO1$ST$Ind2$stVal", liveStatus: Iec61850DesignLiveStatus.Compatible, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind3$stVal", liveStatus: Iec61850DesignLiveStatus.RecoveredByProbe, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind4$stVal", liveStatus: Iec61850DesignLiveStatus.RecoveredByAlternateProbe, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind5$stVal", liveStatus: Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind6$stVal", liveStatus: Iec61850DesignLiveStatus.DesignOnly, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind7$stVal", liveStatus: Iec61850DesignLiveStatus.InvalidTarget, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind8$stVal", liveStatus: Iec61850DesignLiveStatus.Unreadable, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind9$stVal", liveStatus: Iec61850DesignLiveStatus.Absent, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind10$stVal", liveStatus: Iec61850DesignLiveStatus.TransportFailure, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind11$stVal", liveStatus: Iec61850DesignLiveStatus.FunctionalConstraintMismatch, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind12$stVal", liveStatus: Iec61850DesignLiveStatus.TypeMismatch, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind13$stVal", liveStatus: Iec61850DesignLiveStatus.Ambiguous, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind14$stVal", liveStatus: Iec61850DesignLiveStatus.UnresolvedDesign, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind15$stVal", liveStatus: null, engineeringOnly: true),
                Signal("IEDLD0/GGIO1$ST$Ind16$stVal", Iec61850SignalCatalogResolutionStatus.DataSetResolvedAttribute, liveStatus: null, mandatoryPrimary: true),
                Signal("IEDLD0/GGIO1$ST$Ind17$stVal", Iec61850SignalCatalogResolutionStatus.DataSetSyntheticFallback, liveStatus: null, mandatoryPrimary: true),
                Signal("", Iec61850SignalCatalogResolutionStatus.Unresolved, liveStatus: null, engineeringOnly: true),
                Signal("IEDLD0/GGIO1$ST$Live$stVal", Iec61850SignalCatalogResolutionStatus.LiveOnly, Iec61850DesignLiveStatus.LiveOnly)
            }
        };

        var coverage = catalog.GetCoverageDiagnostics();

        Assert.Equal(catalog.SignalCount, coverage.SignalCount);
        Assert.Equal(catalog.DesignSignalCount, coverage.DesignSignalCount);
        Assert.Equal(catalog.LiveOnlyCount, coverage.LiveOnlyCount);
        Assert.Equal(catalog.StaticDataSetMandatoryCount, coverage.StaticDataSetMandatoryCount);
        Assert.Equal(catalog.OperationalCandidateCount, coverage.OperationalCandidateCount);
        Assert.Equal(catalog.VerifiedPresentCount, coverage.VerifiedPresentCount);
        Assert.Equal(catalog.ConfirmedAbsentCount, coverage.ConfirmedAbsentCount);

        Assert.Equal(19, coverage.SignalCount);
        Assert.Equal(18, coverage.DesignSignalCount);
        Assert.Equal(1, coverage.LiveOnlyCount);
        Assert.Equal(15, coverage.DesignAttributeCount);
        Assert.Equal(1, coverage.DataSetResolvedAttributeCount);
        Assert.Equal(1, coverage.DataSetSyntheticFallbackCount);
        Assert.Equal(1, coverage.CatalogUnresolvedCount);
        Assert.Equal(16, coverage.StaticDataSetMandatoryCount);
        Assert.Equal(16, coverage.MandatoryPrimaryCount);
        Assert.Equal(16, coverage.OperationalCandidateCount);
        Assert.Equal(2, coverage.EngineeringOnlyCount);
        Assert.Equal(1, coverage.ReportBackedSignalCount);
        Assert.Equal(1, coverage.MultiDataSetSignalCount);
        Assert.Equal(1, coverage.MultiReportSignalCount);
        Assert.Equal(14, coverage.ReconciledDesignCount);
        Assert.Equal(4, coverage.UnreconciledDesignCount);

        Assert.Equal(1, coverage.ExactCount);
        Assert.Equal(1, coverage.CompatibleCount);
        Assert.Equal(1, coverage.RecoveredByProbeCount);
        Assert.Equal(1, coverage.RecoveredByAlternateProbeCount);
        Assert.Equal(1, coverage.RecoveredByAlternateDiscoveryCount);
        Assert.Equal(5, coverage.VerifiedPresentCount);
        Assert.Equal(1, coverage.DesignOnlyCount);
        Assert.Equal(1, coverage.FunctionalConstraintMismatchCount);
        Assert.Equal(1, coverage.TypeMismatchCount);
        Assert.Equal(1, coverage.AmbiguousCount);
        Assert.Equal(1, coverage.InvalidTargetCount);
        Assert.Equal(1, coverage.UnreadableCount);
        Assert.Equal(1, coverage.ConfirmedAbsentCount);
        Assert.Equal(1, coverage.TransportFailureCount);
        Assert.Equal(1, coverage.UnresolvedDesignCount);
        Assert.True(coverage.HasConfirmedAbsence);

        Assert.Equal(14, coverage.MandatoryPrimaryReconciledCount);
        Assert.Equal(2, coverage.MandatoryPrimaryUnreconciledCount);
        Assert.Equal(5, coverage.MandatoryPrimaryVerifiedPresentCount);
        Assert.Equal(1, coverage.MandatoryPrimaryDesignOnlyCount);
        Assert.Equal(1, coverage.MandatoryPrimaryConfirmedAbsentCount);
        Assert.Equal(1, coverage.MandatoryPrimaryTransportFailureCount);
        Assert.True(coverage.HasConfirmedMandatoryPrimaryAbsence);
    }

    [Fact]
    public void Empty_Catalog_Has_Zero_Coverage_And_No_Confirmed_Absence()
    {
        var coverage = new Iec61850SignalCatalogDocument().GetCoverageDiagnostics();

        Assert.Equal(0, coverage.SignalCount);
        Assert.Equal(0, coverage.DesignSignalCount);
        Assert.Equal(0, coverage.MandatoryPrimaryCount);
        Assert.Equal(0, coverage.UnreconciledDesignCount);
        Assert.Equal(0, coverage.ConfirmedAbsentCount);
        Assert.False(coverage.HasConfirmedAbsence);
        Assert.False(coverage.HasConfirmedMandatoryPrimaryAbsence);
    }

    [Fact]
    public void Selection_Is_Deterministic_And_Does_Not_Mutate_Source_Order()
    {
        var third = Signal("IEDLD0/GGIO1$ST$Ind3$stVal", liveStatus: Iec61850DesignLiveStatus.Exact);
        var first = Signal("IEDLD0/GGIO1$ST$Ind1$stVal", liveStatus: Iec61850DesignLiveStatus.Exact);
        var liveOnly = Signal("IEDLD0/GGIO1$ST$Live$stVal", Iec61850SignalCatalogResolutionStatus.LiveOnly, Iec61850DesignLiveStatus.LiveOnly);
        var second = Signal("IEDLD0/GGIO1$ST$Ind2$stVal", liveStatus: Iec61850DesignLiveStatus.Exact);
        var catalog = new Iec61850SignalCatalogDocument
        {
            Signals = new[] { third, first, liveOnly, second }
        };

        var selected = catalog.Select(Iec61850SignalCatalogSelection.All);

        Assert.Equal(new[]
        {
            first.CanonicalMmsReference,
            second.CanonicalMmsReference,
            third.CanonicalMmsReference,
            liveOnly.CanonicalMmsReference
        }, selected.Select(signal => signal.CanonicalMmsReference).ToArray());
        Assert.Same(third, catalog.Signals[0]);
        Assert.Same(liveOnly, catalog.Signals[2]);
    }

    private static Iec61850SignalDescriptor Signal(
        string canonicalMmsReference,
        Iec61850SignalCatalogResolutionStatus resolutionStatus = Iec61850SignalCatalogResolutionStatus.DesignAttribute,
        Iec61850DesignLiveStatus? liveStatus = null,
        bool mandatoryPrimary = false,
        bool engineeringOnly = false,
        IReadOnlyList<Iec61850SignalDataSetMembership>? dataSets = null,
        IReadOnlyList<Iec61850SignalReportMembership>? reports = null)
        => new()
        {
            DesignReference = resolutionStatus == Iec61850SignalCatalogResolutionStatus.LiveOnly
                ? string.Empty
                : canonicalMmsReference.Replace('$', '.'),
            ObservedReference = resolutionStatus == Iec61850SignalCatalogResolutionStatus.LiveOnly
                ? canonicalMmsReference.Replace('$', '.')
                : string.Empty,
            CanonicalMmsReference = canonicalMmsReference,
            EffectiveMmsReference = canonicalMmsReference,
            FunctionalConstraint = "ST",
            Cdc = "SPS",
            LogicalNodeClass = "GGIO",
            SemanticRole = mandatoryPrimary
                ? Iec61850DataAttributeSemanticRole.PrimaryValue
                : Iec61850DataAttributeSemanticRole.Other,
            IsStaticDataSetMandatory = mandatoryPrimary,
            IsOperationalCandidate = mandatoryPrimary,
            IsEngineeringOnly = engineeringOnly,
            ResolutionStatus = resolutionStatus,
            LiveStatus = liveStatus,
            DataSetMemberships = dataSets ?? (mandatoryPrimary
                ? new[] { new Iec61850SignalDataSetMembership { DataSetReference = "IEDLD0/LLN0.dsMandatory", MemberIndex = 0 } }
                : Array.Empty<Iec61850SignalDataSetMembership>()),
            ReportMemberships = reports ?? Array.Empty<Iec61850SignalReportMembership>()
        };
}
