using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// Model-backed projection context for structured IEC 61850 report members.
/// The static DataSet member remains the protocol identity. This context maps only
/// below that exact member boundary and never chooses one sibling phase as primary.
/// </summary>
public sealed class MmsReportSemanticProjectionContext
{
    private readonly IReadOnlyList<MemberSchema> _members;

    private MmsReportSemanticProjectionContext(IReadOnlyList<MemberSchema> members)
        => _members = members;

    public static MmsReportSemanticProjectionContext Create(LiveIedModelDiscoveryDocument model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var bindings = Iec61850DataSetSemanticBindingResolver.Resolve(model);
        var dataObjects = model.LogicalDevices
            .SelectMany(device => device.LogicalNodes)
            .SelectMany(node => node.DataObjects)
            .ToArray();
        var members = new List<MemberSchema>();

        foreach (var dataSet in bindings.DataSets)
        {
            foreach (var binding in dataSet.Members)
            {
                var rootReference = NormalizeReference(
                    string.IsNullOrWhiteSpace(binding.CanonicalReference)
                        ? binding.OriginalReference
                        : binding.CanonicalReference);
                if (string.IsNullOrWhiteSpace(rootReference))
                    continue;

                var dataObject = dataObjects
                    .Where(candidate => IsInside(rootReference, NormalizeReference(candidate.Reference)))
                    .OrderByDescending(candidate => NormalizeReference(candidate.Reference).Length)
                    .FirstOrDefault();
                if (dataObject is null)
                    continue;

                var root = new SchemaNode(rootReference);
                foreach (var attribute in dataObject.Attributes)
                {
                    var attributeReference = NormalizeReference(attribute.ObjectReference);
                    if (!IsDescendant(attributeReference, rootReference))
                        continue;
                    if (!IsFunctionalConstraintCompatible(binding.FunctionalConstraint, attribute.FunctionalConstraint))
                        continue;

                    var relative = attributeReference[(rootReference.Length + 1)..];
                    if (string.IsNullOrWhiteSpace(relative))
                        continue;

                    var current = root;
                    var accumulated = rootReference;
                    foreach (var part in relative.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        accumulated = $"{accumulated}.{part}";
                        current = current.GetOrAdd(part, accumulated);
                    }
                }

                if (root.Children.Count == 0)
                    continue;

                members.Add(new MemberSchema(
                    dataSet.DataSetReference,
                    binding.Index,
                    rootReference,
                    binding.FunctionalConstraint,
                    root));
            }
        }

        return new MmsReportSemanticProjectionContext(members);
    }

    internal bool TryExpand(
        string dataSetReference,
        MmsReportValue reportValue,
        out IReadOnlyList<MmsReportValue> expanded,
        out string reason)
    {
        expanded = Array.Empty<MmsReportValue>();
        reason = string.Empty;

        if (reportValue.Value is null || reportValue.Value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
        {
            reason = "report value is not a structured MMS value";
            return false;
        }

        var schema = ResolveMember(dataSetReference, reportValue);
        if (schema is null)
        {
            reason = "no unique semantic DataSet member schema matched the report value";
            return false;
        }

        var leaves = new List<ExpandedLeaf>();
        if (!TryFlatten(schema.Root, reportValue.Value, leaves, out reason))
            return false;
        if (leaves.Count == 0)
        {
            reason = "semantic structure contained no scalar descendants";
            return false;
        }

        expanded = leaves
            .Select(leaf => new MmsReportValue
            {
                Index = reportValue.Index,
                Member = new MmsDataSetDirectoryMember
                {
                    UserReference = leaf.Reference,
                    FunctionalConstraint = schema.FunctionalConstraint,
                    Source = "SemanticDataSetProjection",
                    Confidence = 100
                },
                Value = leaf.Value,
                DataReference = leaf.Reference,
                ReasonForInclusion = reportValue.ReasonForInclusion
            })
            .ToArray();
        reason = $"expanded {schema.MemberReference} into {expanded.Count} scalar semantic descendant(s)";
        return true;
    }

    private MemberSchema? ResolveMember(string dataSetReference, MmsReportValue reportValue)
    {
        var memberReference = NormalizeReference(reportValue.MemberReference);
        var normalizedDataSet = NormalizeDataSetReference(dataSetReference);

        var exact = _members
            .Where(candidate => candidate.Index == reportValue.Index)
            .Where(candidate => string.IsNullOrWhiteSpace(normalizedDataSet)
                || string.Equals(NormalizeDataSetReference(candidate.DataSetReference), normalizedDataSet, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => string.IsNullOrWhiteSpace(memberReference)
                || string.Equals(candidate.MemberReference, memberReference, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exact.Length == 1)
            return exact[0];

        // DatSet can be omitted by OptFlds. Fall back only when index + exact static
        // member reference is unique across the entire design model.
        var byIdentity = _members
            .Where(candidate => candidate.Index == reportValue.Index)
            .Where(candidate => string.Equals(candidate.MemberReference, memberReference, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return byIdentity.Length == 1 ? byIdentity[0] : null;
    }

    private static bool TryFlatten(
        SchemaNode schema,
        MmsDataValue value,
        ICollection<ExpandedLeaf> leaves,
        out string reason)
    {
        if (schema.Children.Count == 0)
        {
            if (value.Kind is MmsDataKind.Structure or MmsDataKind.Array)
            {
                reason = $"semantic leaf {schema.Reference} received nested MMS {value.Kind}";
                return false;
            }

            leaves.Add(new ExpandedLeaf(schema.Reference, value));
            reason = string.Empty;
            return true;
        }

        if (value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
        {
            reason = $"semantic structure {schema.Reference} expected nested MMS value but received {value.Kind}";
            return false;
        }
        if (schema.Children.Count != value.Children.Count)
        {
            reason = $"semantic structure {schema.Reference} child-count mismatch: schema={schema.Children.Count}, report={value.Children.Count}";
            return false;
        }

        for (var index = 0; index < schema.Children.Count; index++)
        {
            if (!TryFlatten(schema.Children[index], value.Children[index], leaves, out reason))
                return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsInside(string reference, string parent)
        => string.Equals(reference, parent, StringComparison.OrdinalIgnoreCase)
            || IsDescendant(reference, parent);

    private static bool IsDescendant(string reference, string parent)
        => !string.IsNullOrWhiteSpace(reference)
            && !string.IsNullOrWhiteSpace(parent)
            && reference.Length > parent.Length
            && reference.StartsWith(parent + ".", StringComparison.OrdinalIgnoreCase);

    private static bool IsFunctionalConstraintCompatible(string memberFc, string attributeFc)
        => string.IsNullOrWhiteSpace(memberFc)
            || string.IsNullOrWhiteSpace(attributeFc)
            || string.Equals(memberFc.Trim(), attributeFc.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('$', '.').Replace("..", ".", StringComparison.Ordinal);

    private static string NormalizeDataSetReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        var slash = normalized.IndexOf('/');
        if (slash < 0 || slash >= normalized.Length - 1)
            return normalized.Replace('$', '.');

        return normalized[..(slash + 1)] + normalized[(slash + 1)..].Replace('$', '.');
    }

    private sealed class SchemaNode
    {
        private readonly Dictionary<string, SchemaNode> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SchemaNode> _children = new();

        public SchemaNode(string reference)
            => Reference = reference;

        public string Reference { get; }
        public IReadOnlyList<SchemaNode> Children => _children;

        public SchemaNode GetOrAdd(string name, string reference)
        {
            if (_byName.TryGetValue(name, out var existing))
                return existing;

            var created = new SchemaNode(reference);
            _byName[name] = created;
            _children.Add(created);
            return created;
        }
    }

    private sealed record MemberSchema(
        string DataSetReference,
        int Index,
        string MemberReference,
        string FunctionalConstraint,
        SchemaNode Root);

    private sealed record ExpandedLeaf(string Reference, MmsDataValue Value);
}

/// <summary>
/// Backward-compatible overlay for structures that the established report projector
/// intentionally leaves raw. Existing known CDC projections remain untouched.
/// </summary>
public static class MmsSemanticReportValueProjector
{
    public static MmsReportValueProjection Project(
        MmsReportFrame frame,
        MmsReportSemanticProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(context);

        var baseline = MmsReportValueProjector.Project(frame);
        var replacementParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var semanticUpdates = new List<MmsReportSignalUpdate>();
        var semanticWarnings = new List<string>();

        foreach (var reportValue in frame.Values)
        {
            if (reportValue.Value is null || reportValue.Value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
                continue;

            var parentReference = reportValue.MemberReference;
            var rawPrefix = $"REPORT_RAW_STRUCT: {parentReference} ";
            if (!baseline.Warnings.Any(warning => warning.StartsWith(rawPrefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (!context.TryExpand(frame.Header.DataSetReference, reportValue, out var expanded, out var expansionReason))
            {
                semanticWarnings.Add($"REPORT_SEMANTIC_FALLBACK: {parentReference} remained raw; {expansionReason}. Exact scalar MMS fallback remains eligible.");
                continue;
            }

            var synthetic = new MmsReportFrame
            {
                ReceivedAt = frame.ReceivedAt,
                Header = frame.Header,
                Values = expanded,
                DecoderMode = frame.DecoderMode,
                Message = frame.Message
            };
            var projected = MmsReportValueProjector.Project(synthetic);
            if (projected.Updates.Count == 0 || projected.Warnings.Any(warning => warning.StartsWith("REPORT_RAW_STRUCT:", StringComparison.OrdinalIgnoreCase)))
            {
                semanticWarnings.Add($"REPORT_SEMANTIC_FALLBACK: {parentReference} expansion was not publishable; baseline raw projection was preserved.");
                continue;
            }

            replacementParents.Add(Normalize(parentReference));
            semanticUpdates.AddRange(projected.Updates.Select(update => new MmsReportSignalUpdate
            {
                Reference = update.Reference,
                FunctionalConstraint = update.FunctionalConstraint,
                DisplayName = update.DisplayName,
                Source = update.Source,
                Value = update.Value,
                Quality = update.Quality,
                Timestamp = update.Timestamp,
                Reason = update.Reason,
                UpdatedAt = update.UpdatedAt,
                HasValue = update.HasValue,
                HasQuality = update.HasQuality,
                HasTimestamp = update.HasTimestamp,
                IsProjectedChild = true,
                ProjectionStatus = "semantic-structured-leaf"
            }));
            semanticWarnings.Add($"REPORT_SEMANTIC_STRUCT: {parentReference} {expansionReason}; static DataSet membership identity was preserved.");
        }

        if (replacementParents.Count == 0)
        {
            return new MmsReportValueProjection
            {
                Updates = baseline.Updates,
                Warnings = baseline.Warnings.Concat(semanticWarnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        var updates = baseline.Updates
            .Where(update => !replacementParents.Contains(Normalize(update.Reference)))
            .Concat(semanticUpdates)
            .GroupBy(update => Normalize(update.Reference) + "|" + update.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(update => update.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var warnings = baseline.Warnings
            .Where(warning => !replacementParents.Any(parent => warning.StartsWith($"REPORT_RAW_STRUCT: {parent} ", StringComparison.OrdinalIgnoreCase)))
            .Concat(semanticWarnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MmsReportValueProjection
        {
            Updates = updates,
            Warnings = warnings
        };
    }

    private static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('$', '.').Replace("..", ".", StringComparison.Ordinal);
}
