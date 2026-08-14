using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850SignalCatalogP20Tests
{
    [Fact]
    public void Design_Bcr_DataSet_Produces_Typed_Primary_Quality_Timestamp_And_Report_Membership()
    {
        var design = BuildBcrDesign(includeAttributes: true);

        var catalog = Iec61850SignalCatalogBuilder.Build(design);

        Assert.Equal("iec61850-signal-catalog-v1", catalog.SchemaVersion);
        Assert.Equal(4, catalog.SignalCount);
        Assert.Equal(4, catalog.DesignSignalCount);
        Assert.Equal(4, catalog.StaticDataSetMandatoryCount);
        Assert.Equal(1, catalog.OperationalCandidateCount);
        Assert.Equal(0, catalog.VerifiedPresentCount);
        Assert.Equal(0, catalog.ConfirmedAbsentCount);

        var primary = Assert.NotNull(catalog.FindByCanonicalMmsReference("iedld0/MMTR1$ST$SupWh$actVal"));
        Assert.Equal("IEDLD0/MMTR1.SupWh.actVal", primary.DesignReference);
        Assert.Equal("IEDLD0/MMTR1$ST$SupWh$actVal", primary.EffectiveMmsReference);
        Assert.Equal("ST", primary.FunctionalConstraint);
        Assert.Equal("BCR", primary.Cdc);
        Assert.Equal("INT64", primary.SclBType);
        Assert.Equal("IEDLD0", primary.MmsDomain);
        Assert.Equal("LD0", primary.LogicalDevice);
        Assert.Equal("MMTR1", primary.LogicalNode);
        Assert.Equal("MMTR", primary.LogicalNodeClass);
        Assert.Equal("SupWh", primary.DataObject);
        Assert.Equal("actVal", primary.DataAttributePath);
        Assert.Equal(Iec61850DataAttributeSemanticRole.PrimaryValue, primary.SemanticRole);
        Assert.True(primary.IsOperationalCandidate);
        Assert.True(primary.IsStaticDataSetMandatory);
        Assert.False(primary.IsEngineeringOnly);
        Assert.Equal(Iec61850SignalCatalogResolutionStatus.DesignAttribute, primary.ResolutionStatus);
        Assert.Null(primary.LiveStatus);
        Assert.False(primary.IsVerifiedPresent);

        var membership = Assert.Single(primary.DataSetMemberships);
        Assert.Equal("IEDLD0/LLN0.dsEnergy", membership.DataSetReference);
        Assert.Equal(7, membership.MemberIndex);
        Assert.Equal("IEDLD0/MMTR1.SupWh", membership.OriginalMemberReference);
        Assert.Equal("IEDLD0/MMTR1.SupWh", membership.CanonicalMemberReference);
        Assert.True(membership.IsPrimaryValueForMember);

        var report = Assert.Single(primary.ReportMemberships);
        Assert.Equal("IEDLD0/LLN0.BR.Energy", report.ReportControlReference);
        Assert.True(report.Buffered);
        Assert.Equal("ENERGY_REPORT", report.ReportId);

        Assert.Equal("IEDLD0/MMTR1.SupWh.actVal", primary.PrimaryValueReference);
        Assert.Equal("IEDLD0/MMTR1$ST$SupWh$actVal", primary.PrimaryValueMmsReference);
        Assert.Equal("IEDLD0/MMTR1.SupWh.q", primary.QualityReference);
        Assert.Equal("IEDLD0/MMTR1$ST$SupWh$q", primary.QualityMmsReference);
        Assert.Equal("IEDLD0/MMTR1.SupWh.t", primary.TimestampReference);
        Assert.Equal("IEDLD0/MMTR1$ST$SupWh$t", primary.TimestampMmsReference);
        Assert.Contains(primary.Evidence, x => x.Kind == Iec61850SignalEvidenceKind.DesignModel);
        Assert.Contains(primary.Evidence, x => x.Kind == Iec61850SignalEvidenceKind.DataSetSemanticBinding);
        Assert.Contains(primary.Evidence, x => x.Kind == Iec61850SignalEvidenceKind.ReportControlMembership);

        Assert.Equal(Iec61850DataAttributeSemanticRole.FrozenValue,
            catalog.FindByCanonicalMmsReference("IEDLD0/MMTR1$ST$SupWh$frVal")!.SemanticRole);
        Assert.Equal(Iec61850DataAttributeSemanticRole.Quality,
            catalog.FindByCanonicalMmsReference("IEDLD0/MMTR1$ST$SupWh$q")!.SemanticRole);
        Assert.Equal(Iec61850DataAttributeSemanticRole.Timestamp,
            catalog.FindByCanonicalMmsReference("IEDLD0/MMTR1$ST$SupWh$t")!.SemanticRole);
    }

    [Fact]
    public void Shallow_Bcr_DataSet_Produces_Synthetic_Typed_Fallback_Without_Mutating_Design_Model()
    {
        var design = BuildBcrDesign(includeAttributes: false);
        var dataObject = design.LogicalDevices.Single().LogicalNodes.Single().DataObjects.Single();
        Assert.Empty(dataObject.Attributes);

        var catalog = Iec61850SignalCatalogBuilder.Build(design);

        Assert.Equal(4, catalog.SignalCount);
        Assert.All(catalog.Signals, signal =>
        {
            Assert.Equal(Iec61850SignalCatalogResolutionStatus.DataSetSyntheticFallback, signal.ResolutionStatus);
            Assert.True(signal.IsStaticDataSetMandatory);
            Assert.False(signal.IsEngineeringOnly);
        });

        var primary = Assert.NotNull(catalog.FindByCanonicalMmsReference("IEDLD0/MMTR1$ST$SupWh$actVal"));
        Assert.Equal("INT64", primary.SclBType);
        Assert.Equal(Iec61850DataAttributeSemanticRole.PrimaryValue, primary.SemanticRole);
        Assert.True(primary.IsOperationalCandidate);
        Assert.Equal("IEDLD0/MMTR1.SupWh.q", primary.QualityReference);
        Assert.Equal("IEDLD0/MMTR1.SupWh.t", primary.TimestampReference);
        Assert.Equal(7, Assert.Single(primary.DataSetMemberships).MemberIndex);

        Assert.Empty(dataObject.Attributes);
    }

    [Fact]
    public void Reconciliation_Overlay_Preserves_Canonical_And_Effective_Alternate_Discovery_Evidence()
    {
        const string canonical = "IEDLD0/MMXU1$MX$TotW$mag$f";
        const string effective = "IEDLD0/MMXU1$MX$TotW$instMag$f";
        var design = BuildSingleSignalDesign(
            "IEDLD0/MMXU1.TotW.mag.f",
            canonical,
            "MX",
            "FLOAT32",
            "MV");
        var reconciliation = new Iec61850DesignLiveReconciliationDocument
        {
            Points = new[]
            {
                new Iec61850DesignLivePointReconciliation
                {
                    Reference = "IEDLD0/MMXU1.TotW.mag.f",
                    MmsReference = canonical,
                    CanonicalMmsReference = canonical,
                    EffectiveMmsReference = effective,
                    AlternateStrategy = Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling,
                    FunctionalConstraint = "MX",
                    SclBType = "FLOAT32",
                    Status = Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery,
                    ObservedReference = "IEDLD0/MMXU1.TotW.instMag.f",
                    ObservedMmsReference = effective,
                    ObservedFunctionalConstraint = "MX",
                    Evidence = new[] { "Known semantic sibling was present in native discovery." }
                }
            }
        };

        var catalog = Iec61850SignalCatalogBuilder.Build(design, reconciliation);

        var signal = Assert.Single(catalog.Signals);
        Assert.Equal(canonical, signal.CanonicalMmsReference);
        Assert.Equal(effective, signal.EffectiveMmsReference);
        Assert.Equal(effective, signal.ObservedMmsReference);
        Assert.Equal(Iec61850DesignLiveStatus.RecoveredByAlternateDiscovery, signal.LiveStatus);
        Assert.Equal(Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling, signal.AlternateStrategy);
        Assert.True(signal.IsVerifiedPresent);
        Assert.False(signal.IsConfirmedAbsent);
        Assert.True(signal.IsOperationalCandidate);
        Assert.Equal(Iec61850DataAttributeSemanticRole.PrimaryValue, signal.SemanticRole);
        Assert.Contains(signal.Evidence, x => x.Kind == Iec61850SignalEvidenceKind.AlternateDiscovery);
        Assert.Same(signal, catalog.FindByEffectiveMmsReference(effective.ToLowerInvariant()));
        Assert.Equal(1, catalog.VerifiedPresentCount);
    }

    [Fact]
    public void Same_Signal_In_Multiple_DataSets_Is_One_Descriptor_With_All_Memberships()
    {
        const string reference = "IEDLD0/GGIO1.Ind1.stVal";
        const string mmsReference = "IEDLD0/GGIO1$ST$Ind1$stVal";
        var design = BuildSingleSignalDesign(reference, mmsReference, "ST", "BOOLEAN", "SPS");
        design = new LiveIedModelDiscoveryDocument
        {
            Source = design.Source,
            IedName = design.IedName,
            LogicalDevices = design.LogicalDevices,
            DataSets = new[]
            {
                BuildDataSet("IEDLD0/LLN0.dsA", 0, "IEDLD0/GGIO1.Ind1", "ST", "IEDLD0/LLN0.BR.A"),
                BuildDataSet("IEDLD0/LLN0.dsB", 3, "IEDLD0/GGIO1.Ind1", "ST", "IEDLD0/LLN0.BR.B")
            },
            ReportControls = new[]
            {
                BuildReport("IEDLD0/LLN0.BR.A", "IEDLD0/LLN0.dsA", "A"),
                BuildReport("IEDLD0/LLN0.BR.B", "IEDLD0/LLN0.dsB", "B")
            }
        };

        var catalog = Iec61850SignalCatalogBuilder.Build(design);

        var signal = Assert.Single(catalog.Signals);
        Assert.Equal(2, signal.DataSetMemberships.Count);
        Assert.Equal(new[] { 0, 3 }, signal.DataSetMemberships.Select(x => x.MemberIndex).ToArray());
        Assert.Equal(2, signal.ReportMemberships.Count);
        Assert.True(signal.IsStaticDataSetMandatory);
        Assert.True(signal.IsOperationalCandidate);
        Assert.Equal(Iec61850DataAttributeSemanticRole.PrimaryValue, signal.SemanticRole);
    }

    [Fact]
    public void LiveOnly_Reconciliation_Point_Is_Appended_Without_Inventing_Design_Metadata()
    {
        const string liveReference = "IEDLD0/GGIO1.Ind99.stVal";
        const string liveMmsReference = "IEDLD0/GGIO1$ST$Ind99$stVal";
        var design = new LiveIedModelDiscoveryDocument
        {
            Source = "SclWorkspace",
            IedName = "IED"
        };
        var reconciliation = new Iec61850DesignLiveReconciliationDocument
        {
            Points = new[]
            {
                new Iec61850DesignLivePointReconciliation
                {
                    Reference = liveReference,
                    MmsReference = liveMmsReference,
                    EffectiveMmsReference = liveMmsReference,
                    FunctionalConstraint = "ST",
                    SclBType = "BOOLEAN",
                    Status = Iec61850DesignLiveStatus.LiveOnly,
                    ObservedReference = liveReference,
                    ObservedMmsReference = liveMmsReference,
                    ObservedFunctionalConstraint = "ST",
                    Evidence = new[] { "Native live discovery contains this attribute, but design does not." }
                }
            }
        };

        var catalog = Iec61850SignalCatalogBuilder.Build(design, reconciliation);

        var signal = Assert.Single(catalog.Signals);
        Assert.Equal(0, catalog.DesignSignalCount);
        Assert.Equal(1, catalog.LiveOnlyCount);
        Assert.Equal(string.Empty, signal.DesignReference);
        Assert.Equal(liveReference, signal.ObservedReference);
        Assert.Equal(liveMmsReference, signal.CanonicalMmsReference);
        Assert.Equal(Iec61850SignalCatalogResolutionStatus.LiveOnly, signal.ResolutionStatus);
        Assert.Equal(Iec61850DesignLiveStatus.LiveOnly, signal.LiveStatus);
        Assert.False(signal.IsEngineeringOnly);
        Assert.Contains(signal.Evidence, x => x.Kind == Iec61850SignalEvidenceKind.LiveDiscovery);
    }

    [Fact]
    public void Confirmed_Absent_Remains_A_Typed_Reconciliation_Verdict_Not_A_Catalog_Inference()
    {
        const string reference = "IEDLD0/GGIO1.Ind1.stVal";
        const string mmsReference = "IEDLD0/GGIO1$ST$Ind1$stVal";
        var design = BuildSingleSignalDesign(reference, mmsReference, "ST", "BOOLEAN", "SPS");
        var reconciliation = new Iec61850DesignLiveReconciliationDocument
        {
            Points = new[]
            {
                new Iec61850DesignLivePointReconciliation
                {
                    Reference = reference,
                    MmsReference = mmsReference,
                    CanonicalMmsReference = mmsReference,
                    FunctionalConstraint = "ST",
                    SclBType = "BOOLEAN",
                    Status = Iec61850DesignLiveStatus.Absent,
                    Probe = new Iec61850ExactProbeEvidence
                    {
                        Status = Iec61850ExactProbeStatus.Absent,
                        MmsReference = mmsReference,
                        FunctionalConstraint = "ST",
                        FailureCode = 10,
                        Message = "object-non-existent"
                    },
                    Evidence = new[] { "Protocol-level exact verification confirmed absence." }
                }
            }
        };

        var catalog = Iec61850SignalCatalogBuilder.Build(design, reconciliation);

        var signal = Assert.Single(catalog.Signals);
        Assert.True(signal.IsConfirmedAbsent);
        Assert.False(signal.IsVerifiedPresent);
        Assert.Equal(Iec61850DesignLiveStatus.Absent, signal.LiveStatus);
        Assert.Equal(1, catalog.ConfirmedAbsentCount);
        Assert.Contains(signal.Evidence, x => x.Kind == Iec61850SignalEvidenceKind.ReconciliationDiagnostic);
    }

    private static LiveIedModelDiscoveryDocument BuildBcrDesign(bool includeAttributes)
    {
        var attributes = includeAttributes
            ? new[]
            {
                Attribute("IEDLD0/MMTR1.SupWh.actVal", "actVal", "IEDLD0/MMTR1$ST$SupWh$actVal", "ST", "INT64"),
                Attribute("IEDLD0/MMTR1.SupWh.frVal", "frVal", "IEDLD0/MMTR1$ST$SupWh$frVal", "ST", "INT64"),
                Attribute("IEDLD0/MMTR1.SupWh.q", "q", "IEDLD0/MMTR1$ST$SupWh$q", "ST", "Quality"),
                Attribute("IEDLD0/MMTR1.SupWh.t", "t", "IEDLD0/MMTR1$ST$SupWh$t", "ST", "Timestamp")
            }
            : Array.Empty<LiveIedDataAttributeModel>();

        const string dataSetReference = "IEDLD0/LLN0.dsEnergy";
        const string reportReference = "IEDLD0/LLN0.BR.Energy";
        return new LiveIedModelDiscoveryDocument
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
                                    Attributes = attributes
                                }
                            }
                        }
                    }
                }
            },
            DataSets = new[]
            {
                BuildDataSet(dataSetReference, 7, "IEDLD0/MMTR1.SupWh", "ST", reportReference)
            },
            ReportControls = new[]
            {
                new LiveIedReportControlModel
                {
                    Reference = reportReference,
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "Energy",
                    Buffered = true,
                    DataSetReference = dataSetReference,
                    ReportId = "ENERGY_REPORT"
                }
            }
        };
    }

    private static LiveIedModelDiscoveryDocument BuildSingleSignalDesign(
        string reference,
        string mmsReference,
        string fc,
        string sclBType,
        string cdc)
    {
        var slash = reference.IndexOf('/');
        var domain = reference[..slash];
        var logicalPath = reference[(slash + 1)..];
        var firstDot = logicalPath.IndexOf('.');
        var logicalNode = logicalPath[..firstDot];
        var objectAndAttribute = logicalPath[(firstDot + 1)..];
        var dataObject = Iec61850ReferenceParts.TopDataObjectName(objectAndAttribute);
        var attributePath = Iec61850ReferenceParts.DataAttributePath(objectAndAttribute);

        return new LiveIedModelDiscoveryDocument
        {
            Source = "SclWorkspace",
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = domain,
                    Inst = "LD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = logicalNode,
                            LnClass = Iec61850ReferenceParts.ParseLogicalNodeName(logicalNode).LnClass,
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = $"{domain}/{logicalNode}.{dataObject}",
                                    Name = dataObject,
                                    InferredCdc = cdc,
                                    Attributes = new[]
                                    {
                                        Attribute(reference, attributePath, mmsReference, fc, sclBType)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static LiveIedDataSetModel BuildDataSet(
        string dataSetReference,
        int memberIndex,
        string memberReference,
        string fc,
        string reportReference)
        => new()
        {
            Reference = dataSetReference,
            Domain = "IEDLD0",
            LogicalNode = "LLN0",
            Name = dataSetReference[(dataSetReference.LastIndexOf('.') + 1)..],
            MemberCount = 1,
            Members = new[]
            {
                new LiveIedDataSetMemberModel
                {
                    Index = memberIndex,
                    Reference = memberReference,
                    FunctionalConstraint = fc
                }
            },
            UsedByReportControls = new[] { reportReference }
        };

    private static LiveIedReportControlModel BuildReport(string reference, string dataSetReference, string reportId)
        => new()
        {
            Reference = reference,
            Domain = "IEDLD0",
            LogicalNode = "LLN0",
            Name = reportId,
            Buffered = true,
            DataSetReference = dataSetReference,
            ReportId = reportId
        };

    private static LiveIedDataAttributeModel Attribute(
        string reference,
        string attributePath,
        string mmsReference,
        string fc,
        string sclBType)
        => new()
        {
            ObjectReference = reference,
            AttributePath = attributePath,
            FunctionalConstraint = fc,
            MmsReference = mmsReference,
            MmsItemName = mmsReference[(mmsReference.IndexOf('/') + 1)..],
            SclBType = sclBType,
            Source = "SCL.DataTypeTemplates",
            TypeSource = "SCL.DataTypeTemplates",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
}
