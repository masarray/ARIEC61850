namespace AR.Iec61850.Engineering.Canonical;

/// <summary>
/// Compacts the reference-heavy metadata around the already value-type/symbol-backed hot
/// signal tables. Existing symbol ids are preserved exactly; newly encountered metadata
/// strings are appended to the same immutable string table and every repeated metadata
/// value is rewritten to one shared string instance.
/// </summary>
public static class CanonicalModelMemoryCompactor
{
    public static CanonicalIedModel Compact(CanonicalIedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var pool = new CanonicalMetadataStringPool(model.Strings);
        var identity = new CanonicalIedIdentity
        {
            Name = pool.Intern(model.Identity.Name),
            Provenance = model.Identity.Provenance,
            IsAmbiguous = model.Identity.IsAmbiguous,
            CandidateNames = model.Identity.CandidateNames.Select(pool.Intern).ToArray(),
            Evidence = model.Identity.Evidence.Select(pool.Intern).ToArray()
        };

        var dataSets = new CanonicalDataSet[model.DataSets.Length];
        for (var index = 0; index < model.DataSets.Length; index++)
        {
            var source = model.DataSets[index];
            dataSets[index] = new CanonicalDataSet
            {
                Reference = pool.Intern(source.Reference),
                MmsDomain = pool.Intern(source.MmsDomain),
                LogicalNode = pool.Intern(source.LogicalNode),
                Name = pool.Intern(source.Name),
                IsDeletable = source.IsDeletable,
                Members = source.Members.Select(member => new CanonicalDataSetMember(
                    member.Index,
                    pool.Intern(member.Reference),
                    pool.Intern(member.FunctionalConstraint),
                    member.Provenance)).ToArray()
            };
        }

        var reports = new CanonicalReportControl[model.ReportControls.Length];
        for (var index = 0; index < model.ReportControls.Length; index++)
        {
            var source = model.ReportControls[index];
            reports[index] = new CanonicalReportControl
            {
                Reference = pool.Intern(source.Reference),
                MmsDomain = pool.Intern(source.MmsDomain),
                LogicalNode = pool.Intern(source.LogicalNode),
                Name = pool.Intern(source.Name),
                Buffered = source.Buffered,
                DataSetReference = pool.Intern(source.DataSetReference),
                ReportId = pool.Intern(source.ReportId),
                ConfRev = pool.Intern(source.ConfRev),
                TriggerOptions = pool.Intern(source.TriggerOptions),
                OptionalFields = pool.Intern(source.OptionalFields),
                BufferTimeMs = pool.Intern(source.BufferTimeMs),
                IntegrityPeriodMs = pool.Intern(source.IntegrityPeriodMs),
                Provenance = source.Provenance
            };
        }

        var diagnostics = model.Diagnostics.Select(diagnostic => new CanonicalDiagnostic
        {
            Code = pool.Intern(diagnostic.Code),
            Reference = pool.Intern(diagnostic.Reference),
            Message = pool.Intern(diagnostic.Message)
        }).ToArray();

        var sourceEnvelope = new CanonicalSourceEnvelope
        {
            Ingress = model.Source.Ingress,
            SourceName = pool.Intern(model.Source.SourceName),
            SourceEdition = pool.Intern(model.Source.SourceEdition),
            OriginalTypeAliases = model.Source.OriginalTypeAliases.Select(pool.Intern).ToArray()
        };

        var communication = new CanonicalCommunicationContext
        {
            Host = CompactFact(model.Communication.Host, pool),
            Port = model.Communication.Port,
            AccessPointName = CompactFact(model.Communication.AccessPointName, pool)
        };

        return new CanonicalIedModel
        {
            SchemaVersion = pool.Intern(model.SchemaVersion),
            GeneratedAtUtc = model.GeneratedAtUtc,
            Identity = identity,
            Communication = communication,
            Source = sourceEnvelope,
            Strings = pool.Freeze(),

            // These are already compact value-type tables whose text is represented by
            // CanonicalSymbol. Reuse their arrays rather than copying millions of rows.
            AccessPoints = model.AccessPoints,
            LogicalDevices = model.LogicalDevices,
            LogicalNodes = model.LogicalNodes,
            DataObjects = model.DataObjects,
            Signals = model.Signals,

            DataSets = dataSets,
            ReportControls = reports,
            Diagnostics = diagnostics
        };
    }

    private static CanonicalFact<string> CompactFact(
        CanonicalFact<string> fact,
        CanonicalMetadataStringPool pool)
    {
        if (!fact.IsKnown || fact.Value is null)
            return fact;

        return new CanonicalFact<string>(fact.State, pool.Intern(fact.Value), fact.Provenance);
    }

    private sealed class CanonicalMetadataStringPool
    {
        private readonly List<string> _values;
        private readonly Dictionary<string, string> _canonical;

        public CanonicalMetadataStringPool(CanonicalStringTable existing)
        {
            _values = new List<string>(existing.Values.Count);
            _canonical = new Dictionary<string, string>(existing.Values.Count, StringComparer.Ordinal);

            foreach (var value in existing.Values)
            {
                _values.Add(value);
                _canonical.TryAdd(value, value);
            }
        }

        public string Intern(string? value)
        {
            var normalized = value ?? string.Empty;
            if (_canonical.TryGetValue(normalized, out var existing))
                return existing;

            _canonical.Add(normalized, normalized);
            _values.Add(normalized);
            return normalized;
        }

        public CanonicalStringTable Freeze()
            => new(_values.ToArray());
    }
}
