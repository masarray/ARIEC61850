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
        => TryExpand(
            dataSetReference,
            reportValue,
            out expanded,
            out _,
            out reason);

    internal bool TryExpand(
        string dataSetReference,
        MmsReportValue reportValue,
        out IReadOnlyList<MmsReportValue> expanded,
        out string resolvedMemberReference,
        out string reason)
    {
        expanded = Array.Empty<MmsReportValue>();
        resolvedMemberReference = string.Empty;
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
        resolvedMemberReference = schema.MemberReference;
        reason = $"expanded {schema.MemberReference} into {expanded.Count} scalar semantic descendant(s)";
        return true;
    }

    private MemberSchema? ResolveMember(string dataSetReference, MmsReportValue reportValue)
    {
        var memberReference = NormalizeReference(reportValue.MemberReference);
        var normalizedDataSet = NormalizeDataSetReference(dataSetReference);

        // InformationReport values can be sparse. In that case the decoder-side value index
        // is not a safe substitute for the authoritative static DataSet member index. When the
        // report carries an exact member reference, that engineering identity is stronger than
        // the transient value position and must be resolved independently of reportValue.Index.
        // Keep this fail-closed: duplicate static memberships with the same reference do not
        // collapse unless the DataSet identity makes the reference unique.
        if (!string.IsNullOrWhiteSpace(memberReference))
        {
            var byExactReference = _members
                .Where(candidate => string.Equals(
                    candidate.MemberReference,
                    memberReference,
                    StringComparison.OrdinalIgnoreCase))
                .Where(candidate => string.IsNullOrWhiteSpace(normalizedDataSet)
                    || string.Equals(
                        NormalizeDataSetReference(candidate.DataSetReference),
                        normalizedDataSet,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return byExactReference.Length == 1 ? byExactReference[0] : null;
        }

        // Some report encodings omit the member reference. Only then is the static DataSet
        // index used, and only when it identifies exactly one member in the supplied DataSet
        // scope (or globally when OptFlds omitted the DataSet reference as well).
        var byIndex = _members
            .Where(candidate => candidate.Index == reportValue.Index)
            .Where(candidate => string.IsNullOrWhiteSpace(normalizedDataSet)
                || string.Equals(
                    NormalizeDataSetReference(candidate.DataSetReference),
                    normalizedDataSet,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return byIndex.Length == 1 ? byIndex[0] : null;
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
/// Model-backed overlay for structured report members. Exact static DataSet/SCL schema is
/// authoritative when it can expand the structure safely; the established generic projector
/// remains the fail-closed fallback when no unique semantic schema matches.
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
        var semanticReplacementPositions = new HashSet<int>();
        var semanticUpdates = new List<MmsReportSignalUpdate>();
        var semanticWarnings = new List<string>();

        for (var valuePosition = 0; valuePosition < frame.Values.Count; valuePosition++)
        {
            var reportValue = frame.Values[valuePosition];
            if (reportValue.Value is null || reportValue.Value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
                continue;

            var reportedMemberReference = reportValue.MemberReference;
            var rawPrefix = $"REPORT_RAW_STRUCT: {reportedMemberReference} ";
            var baselineWasRaw = baseline.Warnings.Any(warning =>
                warning.StartsWith(rawPrefix, StringComparison.OrdinalIgnoreCase));

            // Static DataSet identity + exact SCL/live schema is stronger evidence than a
            // generic shape heuristic. Try semantic expansion first for every structured
            // member, including structures the baseline recognizes as instMag/mag pairs.
            // If the exact schema cannot prove the mapping, preserve baseline behavior.
            if (!context.TryExpand(
                    frame.Header.DataSetReference,
                    reportValue,
                    out var expanded,
                    out var resolvedMemberReference,
                    out var expansionReason))
            {
                if (baselineWasRaw)
                {
                    semanticWarnings.Add($"REPORT_SEMANTIC_FALLBACK: {reportedMemberReference} remained raw; {expansionReason}. Exact scalar MMS fallback remains eligible.");
                }
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
                if (baselineWasRaw)
                {
                    semanticWarnings.Add($"REPORT_SEMANTIC_FALLBACK: {reportedMemberReference} expansion was not publishable; baseline raw projection was preserved.");
                }
                continue;
            }

            // Replace the generic projection by report-value position, not by a guessed parent
            // reference. Some valid InformationReports omit member identity and are resolved
            // only by the exact static DataSet + member index. In that case the generic
            // projector can emit unrooted heuristic leaves, so descendant-name filtering is
            // neither sufficient nor safe. A successful semantic projection owns this report
            // value completely; generic projection remains available only for other values.
            semanticReplacementPositions.Add(valuePosition);
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
            semanticWarnings.Add($"REPORT_SEMANTIC_STRUCT: {resolvedMemberReference} {expansionReason}; exact static DataSet schema overrode generic structured-value heuristics.");
        }

        if (semanticReplacementPositions.Count == 0)
        {
            return new MmsReportValueProjection
            {
                Updates = baseline.Updates,
                Warnings = baseline.Warnings.Concat(semanticWarnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        // Re-project only report values that were not replaced semantically. This preserves
        // normal generic scalar/companion behavior for unrelated members while guaranteeing
        // that no heuristic output from a successfully resolved structured member survives,
        // even when the wire report omitted MemberReference entirely.
        var retainedFrame = new MmsReportFrame
        {
            ReceivedAt = frame.ReceivedAt,
            Header = frame.Header,
            Values = frame.Values
                .Where((_, index) => !semanticReplacementPositions.Contains(index))
                .ToArray(),
            DecoderMode = frame.DecoderMode,
            Message = frame.Message
        };
        var retainedBaseline = MmsReportValueProjector.Project(retainedFrame);

        var updates = retainedBaseline.Updates
            .Concat(semanticUpdates)
            .GroupBy(update => Normalize(update.Reference) + "|" + update.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            // q/t companions are intentionally delivered before scalar values. Consumers can
            // therefore attach report-native quality/timestamp to semantic value leaves without
            // inventing defaults or issuing a separate MMS read.
            .OrderBy(update => CompanionPriority(update.Reference))
            .ThenBy(update => update.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var warnings = retainedBaseline.Warnings
            .Concat(semanticWarnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MmsReportValueProjection
        {
            Updates = updates,
            Warnings = warnings
        };
    }

    private static int CompanionPriority(string reference)
    {
        var normalized = Normalize(reference);
        return normalized.EndsWith(".q", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".t", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;
    }

    private static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('$', '.').Replace("..", ".", StringComparison.Ordinal);
}
