using AR.Iec61850.Discovery;

namespace AR.Iec61850.Engineering.Canonical;

public static class CanonicalLiveModelAdapter
{
    public static CanonicalIedModel FromLiveDiscovery(LiveIedModelDiscoveryDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var identity = source.IedIdentity;
        var resolvedName = !string.IsNullOrWhiteSpace(identity.IedName)
            ? identity.IedName.Trim()
            : source.IedName.Trim();
        var confidence = MapConfidence(identity.Confidence);
        if (confidence == CanonicalConfidence.Unknown && !string.IsNullOrWhiteSpace(resolvedName))
            confidence = CanonicalConfidence.Medium;

        return Build(
            source,
            new CanonicalIngressContext
            {
                Ingress = CanonicalIngressKind.LiveMmsDiscovery,
                SourceName = source.Source,
                IedName = resolvedName,
                AccessPointName = source.AccessPointName,
                IdentitySource = CanonicalEvidenceSource.LiveMms,
                IdentityConfidence = confidence,
                IdentityAmbiguous = identity.IsAmbiguous,
                IdentityCandidates = identity.CandidateNames.ToArray(),
                IdentityEvidence = identity.Evidence.ToArray(),
                FactSource = CanonicalEvidenceSource.LiveMms,
                DomainAliases = EmptyAliases
            });
    }

    internal static CanonicalIedModel FromSclProjection(
        LiveIedModelDiscoveryDocument projection,
        string sourceName,
        string sourceEdition,
        string iedName,
        string accessPointName,
        IReadOnlyDictionary<string, string> domainAliases,
        IEnumerable<string>? originalTypeAliases = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(domainAliases);

        return Build(
            projection,
            new CanonicalIngressContext
            {
                Ingress = CanonicalIngressKind.SclFile,
                SourceName = sourceName,
                SourceEdition = sourceEdition,
                IedName = iedName,
                AccessPointName = accessPointName,
                IdentitySource = CanonicalEvidenceSource.SclDeclared,
                IdentityConfidence = CanonicalConfidence.Exact,
                IdentityEvidence = ["IED@name from the selected SCL IED."],
                FactSource = CanonicalEvidenceSource.SclDeclared,
                DomainAliases = domainAliases,
                OriginalTypeAliases = originalTypeAliases?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>()
            });
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyAliases =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static CanonicalIedModel Build(
        LiveIedModelDiscoveryDocument source,
        CanonicalIngressContext context)
    {
        var strings = new CanonicalStringTableBuilder();
        var accessPoints = new List<CanonicalAccessPointRow>(1);
        var logicalDevices = new List<CanonicalLogicalDeviceRow>(source.LogicalDevices.Count);
        var logicalNodes = new List<CanonicalLogicalNodeRow>();
        var dataObjects = new List<CanonicalDataObjectRow>();
        var signals = new List<CanonicalSignalRow>();

        accessPoints.Add(new CanonicalAccessPointRow(0, strings.Intern(context.AccessPointName)));

        foreach (var sourceLd in source.LogicalDevices)
        {
            var ldId = logicalDevices.Count;
            var domain = RemapDomain(sourceLd.MmsDomain, context.DomainAliases);
            logicalDevices.Add(new CanonicalLogicalDeviceRow(
                ldId,
                0,
                strings.Intern(sourceLd.Inst),
                strings.Intern(domain)));

            foreach (var sourceLn in sourceLd.LogicalNodes)
            {
                var lnId = logicalNodes.Count;
                logicalNodes.Add(new CanonicalLogicalNodeRow(
                    lnId,
                    ldId,
                    strings.Intern(sourceLn.Name),
                    strings.Intern(sourceLn.Prefix),
                    strings.Intern(sourceLn.LnClass),
                    strings.Intern(sourceLn.LnInst),
                    strings.Intern(sourceLn.ProposedLnTypeId)));

                foreach (var sourceDo in sourceLn.DataObjects)
                {
                    var doId = dataObjects.Count;
                    dataObjects.Add(new CanonicalDataObjectRow(
                        doId,
                        lnId,
                        strings.Intern(sourceDo.Name),
                        strings.Intern(RemapReference(sourceDo.Reference, context.DomainAliases)),
                        strings.Intern(sourceDo.InferredCdc),
                        MapConfidence(sourceDo.ConfidenceLevel),
                        strings.Intern(sourceDo.ProposedDoTypeId)));

                    foreach (var sourceDa in sourceDo.Attributes)
                    {
                        signals.Add(new CanonicalSignalRow(
                            signals.Count,
                            doId,
                            strings.Intern(RemapReference(sourceDa.ObjectReference, context.DomainAliases)),
                            strings.Intern(sourceDa.AttributePath),
                            strings.Intern(sourceDa.FunctionalConstraint),
                            strings.Intern(sourceDa.SclBType),
                            strings.Intern(sourceDa.MmsType),
                            strings.Intern(sourceDa.MmsTypeSignature),
                            new CanonicalProvenance(
                                context.FactSource,
                                MapConfidence(sourceDa.TypeConfidence))));
                    }
                }
            }
        }

        var dataSets = new CanonicalDataSet[source.DataSets.Count];
        for (var i = 0; i < source.DataSets.Count; i++)
        {
            var dataSet = source.DataSets[i];
            var members = new CanonicalDataSetMember[dataSet.Members.Count];
            for (var memberIndex = 0; memberIndex < dataSet.Members.Count; memberIndex++)
            {
                var member = dataSet.Members[memberIndex];
                members[memberIndex] = new CanonicalDataSetMember(
                    member.Index,
                    RemapReference(member.Reference, context.DomainAliases),
                    member.FunctionalConstraint,
                    new CanonicalProvenance(context.FactSource, MapConfidence(member.Confidence)));
            }

            dataSets[i] = new CanonicalDataSet
            {
                Reference = RemapReference(dataSet.Reference, context.DomainAliases),
                MmsDomain = RemapDomain(dataSet.Domain, context.DomainAliases),
                LogicalNode = dataSet.LogicalNode,
                Name = dataSet.Name,
                IsDeletable = dataSet.IsDeletable.HasValue
                    ? CanonicalFact<bool>.Known(dataSet.IsDeletable.Value, context.FactSource)
                    : CanonicalFact<bool>.Unknown(context.FactSource),
                Members = members
            };
        }

        var reportControls = new CanonicalReportControl[source.ReportControls.Count];
        for (var i = 0; i < source.ReportControls.Count; i++)
        {
            var report = source.ReportControls[i];
            reportControls[i] = new CanonicalReportControl
            {
                Reference = RemapReference(report.Reference, context.DomainAliases),
                MmsDomain = RemapDomain(report.Domain, context.DomainAliases),
                LogicalNode = report.LogicalNode,
                Name = report.Name,
                Buffered = report.Buffered,
                DataSetReference = RemapReference(report.DataSetReference, context.DomainAliases),
                ReportId = report.ReportId,
                ConfRev = report.ConfRev,
                TriggerOptions = report.TriggerOptions,
                OptionalFields = report.OptionalFields,
                BufferTimeMs = report.BufferTimeMs,
                IntegrityPeriodMs = report.IntegrityPeriodMs,
                Provenance = new CanonicalProvenance(context.FactSource, CanonicalConfidence.Exact)
            };
        }

        var diagnostics = source.Warnings.Select(warning => new CanonicalDiagnostic
        {
            Code = warning.Code,
            Reference = RemapReference(warning.Reference, context.DomainAliases),
            Message = warning.Message
        }).ToArray();

        return new CanonicalIedModel
        {
            GeneratedAtUtc = source.GeneratedAtUtc,
            Identity = new CanonicalIedIdentity
            {
                Name = context.IedName,
                Provenance = new CanonicalProvenance(context.IdentitySource, context.IdentityConfidence),
                IsAmbiguous = context.IdentityAmbiguous,
                CandidateNames = context.IdentityCandidates,
                Evidence = context.IdentityEvidence
            },
            Communication = new CanonicalCommunicationContext
            {
                Host = string.IsNullOrWhiteSpace(source.Host)
                    ? CanonicalFact<string>.Unknown(context.FactSource)
                    : CanonicalFact<string>.Known(source.Host, context.FactSource),
                Port = source.Port > 0
                    ? CanonicalFact<int>.Known(source.Port, context.FactSource)
                    : CanonicalFact<int>.Unknown(context.FactSource),
                AccessPointName = string.IsNullOrWhiteSpace(context.AccessPointName)
                    ? CanonicalFact<string>.Unknown(context.FactSource)
                    : CanonicalFact<string>.Known(context.AccessPointName, context.FactSource)
            },
            Source = new CanonicalSourceEnvelope
            {
                Ingress = context.Ingress,
                SourceName = context.SourceName,
                SourceEdition = context.SourceEdition,
                OriginalTypeAliases = context.OriginalTypeAliases
            },
            Strings = strings.Freeze(),
            AccessPoints = accessPoints.ToArray(),
            LogicalDevices = logicalDevices.ToArray(),
            LogicalNodes = logicalNodes.ToArray(),
            DataObjects = dataObjects.ToArray(),
            Signals = signals.ToArray(),
            DataSets = dataSets,
            ReportControls = reportControls,
            Diagnostics = diagnostics
        };
    }

    private static string RemapDomain(string value, IReadOnlyDictionary<string, string> aliases)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return aliases.TryGetValue(normalized, out var exact) ? exact : normalized;
    }

    private static string RemapReference(string value, IReadOnlyDictionary<string, string> aliases)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || aliases.Count == 0)
            return normalized;

        var slash = normalized.IndexOf('/');
        var domain = slash >= 0 ? normalized[..slash] : normalized;
        if (!aliases.TryGetValue(domain, out var exact))
            return normalized;

        return slash >= 0 ? exact + normalized[slash..] : exact;
    }

    private static CanonicalConfidence MapConfidence(LiveIedDiscoveryConfidenceLevel confidence)
        => confidence switch
        {
            LiveIedDiscoveryConfidenceLevel.Exact => CanonicalConfidence.Exact,
            LiveIedDiscoveryConfidenceLevel.High => CanonicalConfidence.High,
            LiveIedDiscoveryConfidenceLevel.Medium => CanonicalConfidence.Medium,
            LiveIedDiscoveryConfidenceLevel.Low => CanonicalConfidence.Low,
            _ => CanonicalConfidence.Unknown
        };

    private sealed class CanonicalIngressContext
    {
        public CanonicalIngressKind Ingress { get; init; }
        public string SourceName { get; init; } = string.Empty;
        public string SourceEdition { get; init; } = string.Empty;
        public string IedName { get; init; } = string.Empty;
        public string AccessPointName { get; init; } = string.Empty;
        public CanonicalEvidenceSource IdentitySource { get; init; }
        public CanonicalConfidence IdentityConfidence { get; init; }
        public bool IdentityAmbiguous { get; init; }
        public string[] IdentityCandidates { get; init; } = Array.Empty<string>();
        public string[] IdentityEvidence { get; init; } = Array.Empty<string>();
        public CanonicalEvidenceSource FactSource { get; init; }
        public IReadOnlyDictionary<string, string> DomainAliases { get; init; } = EmptyAliases;
        public string[] OriginalTypeAliases { get; init; } = Array.Empty<string>();
    }
}
