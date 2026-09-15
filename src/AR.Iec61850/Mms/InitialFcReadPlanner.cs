using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

public sealed class InitialFcReadLeafBinding
{
    public string Reference { get; init; } = string.Empty;
    public string AttributePath { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
}

public sealed class InitialFcReadDataObjectBinding
{
    public string Name { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public IReadOnlyList<InitialFcReadLeafBinding> Leaves { get; init; } = Array.Empty<InitialFcReadLeafBinding>();
}

public sealed class InitialFcReadTarget
{
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string MmsItemName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public IReadOnlyList<InitialFcReadDataObjectBinding> DataObjects { get; init; } = Array.Empty<InitialFcReadDataObjectBinding>();

    public MmsObjectReference ToObjectReference()
        => new(Domain, MmsItemName, FunctionalConstraint);

    public string MmsReference => string.IsNullOrWhiteSpace(Domain)
        ? MmsItemName
        : $"{Domain}/{MmsItemName}";
}

public sealed class InitialFcReadBatch
{
    public int Index { get; init; }
    public IReadOnlyList<InitialFcReadTarget> Targets { get; init; } = Array.Empty<InitialFcReadTarget>();
    public IReadOnlyList<MmsObjectReference> References => Targets.Select(target => target.ToObjectReference()).ToArray();
}

public sealed class InitialFcReadPlan
{
    public int MaximumVariableReferencesPerRead { get; init; } = MmsReadBatchCodec.MaximumVariableReferencesPerRead;
    public int MaximumOutstandingReads { get; init; } = 1;
    public IReadOnlyList<InitialFcReadTarget> Targets { get; init; } = Array.Empty<InitialFcReadTarget>();
    public IReadOnlyList<InitialFcReadBatch> Batches { get; init; } = Array.Empty<InitialFcReadBatch>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool IsValid => Errors.Count == 0 && Targets.Count > 0 && Batches.Count > 0;
    public string Summary => $"Initial FC-root Read plan: targets={Targets.Count}, batches={Batches.Count}, maxVariablesPerRead={MaximumVariableReferencesPerRead}, maxOutstanding={MaximumOutstandingReads}.";
}

/// <summary>
/// Shared bounded planner for initial FC-root reads. It can consume either the native
/// live MMS directory or an SCL-projected model, but produces the same ordered plan.
/// Planning is side-effect free; execution remains an explicit later action.
/// </summary>
public static class InitialFcReadPlanner
{
    public static InitialFcReadPlan FromLiveDirectory(
        MmsIedModelDirectory directory,
        int maximumVariableReferencesPerRead = MmsReadBatchCodec.MaximumVariableReferencesPerRead)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var targets = directory.Points
            .Where(point =>
                !string.IsNullOrWhiteSpace(point.Domain) &&
                !string.IsNullOrWhiteSpace(point.LogicalNode) &&
                !string.IsNullOrWhiteSpace(point.FunctionalConstraint))
            .GroupBy(point => new
            {
                point.Domain,
                point.LogicalNode,
                FunctionalConstraint = NormalizeFc(point.FunctionalConstraint)
            })
            .Select(group => new InitialFcReadTarget
            {
                Domain = group.Key.Domain,
                LogicalNode = group.Key.LogicalNode,
                FunctionalConstraint = group.Key.FunctionalConstraint,
                MmsItemName = BuildFcRootItem(group.Key.LogicalNode, group.Key.FunctionalConstraint),
                Source = "LiveMmsDirectory"
            })
            .OrderBy(target => target.Domain, StringComparer.Ordinal)
            .ThenBy(target => target.LogicalNode, StringComparer.Ordinal)
            .ThenBy(target => target.FunctionalConstraint, StringComparer.Ordinal)
            .ToArray();

        return Build(targets, maximumVariableReferencesPerRead);
    }

    public static InitialFcReadPlan FromSclModel(
        LiveIedModelDiscoveryDocument design,
        IEnumerable<string>? allowedDomains = null,
        int maximumVariableReferencesPerRead = MmsReadBatchCodec.MaximumVariableReferencesPerRead)
    {
        ArgumentNullException.ThrowIfNull(design);
        var allowed = allowedDomains is null
            ? null
            : new HashSet<string>(
                allowedDomains.Where(domain => !string.IsNullOrWhiteSpace(domain)).Select(domain => domain.Trim()),
                StringComparer.Ordinal);

        var targets = new List<InitialFcReadTarget>();
        foreach (var logicalDevice in design.LogicalDevices)
        {
            var domain = (logicalDevice.MmsDomain ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(domain) || (allowed is not null && !allowed.Contains(domain)))
                continue;

            foreach (var logicalNode in logicalDevice.LogicalNodes)
            {
                var logicalNodeName = (logicalNode.Name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(logicalNodeName))
                    continue;

                var constraints = logicalNode.DataObjects
                    .SelectMany(dataObject => dataObject.Attributes)
                    .Select(attribute => NormalizeFc(attribute.FunctionalConstraint))
                    .Where(fc => !string.IsNullOrWhiteSpace(fc))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(fc => fc, StringComparer.Ordinal)
                    .ToArray();

                foreach (var fc in constraints)
                {
                    var dataObjects = logicalNode.DataObjects
                        .Select(dataObject => BuildDataObjectBinding(dataObject, fc))
                        .Where(binding => binding.Leaves.Count > 0)
                        .ToArray();
                    if (dataObjects.Length == 0)
                        continue;

                    targets.Add(new InitialFcReadTarget
                    {
                        Domain = domain,
                        LogicalNode = logicalNodeName,
                        FunctionalConstraint = fc,
                        MmsItemName = BuildFcRootItem(logicalNodeName, fc),
                        Source = "SclDataTypeTemplates",
                        DataObjects = dataObjects
                    });
                }
            }
        }

        return Build(
            targets
                .OrderBy(target => target.Domain, StringComparer.Ordinal)
                .ThenBy(target => target.LogicalNode, StringComparer.Ordinal)
                .ThenBy(target => target.FunctionalConstraint, StringComparer.Ordinal)
                .ToArray(),
            maximumVariableReferencesPerRead);
    }

    public static InitialFcReadPlan Build(
        IEnumerable<InitialFcReadTarget> targets,
        int maximumVariableReferencesPerRead = MmsReadBatchCodec.MaximumVariableReferencesPerRead)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var errors = new List<string>();
        var warnings = new List<string>();

        if (maximumVariableReferencesPerRead < 1 || maximumVariableReferencesPerRead > MmsReadBatchCodec.MaximumVariableReferencesPerRead)
        {
            errors.Add($"maximumVariableReferencesPerRead must be between 1 and {MmsReadBatchCodec.MaximumVariableReferencesPerRead}; received {maximumVariableReferencesPerRead}.");
        }

        var materialized = targets
            .Where(target => target is not null)
            .ToArray();

        var validTargets = new List<InitialFcReadTarget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in materialized)
        {
            var fc = NormalizeFc(target.FunctionalConstraint);
            var item = string.IsNullOrWhiteSpace(target.MmsItemName)
                ? BuildFcRootItem(target.LogicalNode, fc)
                : target.MmsItemName.Trim();

            if (string.IsNullOrWhiteSpace(target.Domain) ||
                string.IsNullOrWhiteSpace(target.LogicalNode) ||
                string.IsNullOrWhiteSpace(fc) ||
                string.IsNullOrWhiteSpace(item))
            {
                warnings.Add("An incomplete FC-root target was excluded from the initial Read plan.");
                continue;
            }

            var key = string.Concat(target.Domain.Trim(), "\u001F", item);
            if (!seen.Add(key))
            {
                warnings.Add($"Duplicate FC-root target '{target.Domain}/{item}' was collapsed.");
                continue;
            }

            validTargets.Add(new InitialFcReadTarget
            {
                Domain = target.Domain.Trim(),
                LogicalNode = target.LogicalNode.Trim(),
                FunctionalConstraint = fc,
                MmsItemName = item,
                Source = target.Source,
                DataObjects = target.DataObjects
            });
        }

        if (validTargets.Count == 0)
            errors.Add("No valid FC-root Read targets were produced.");

        var batches = new List<InitialFcReadBatch>();
        if (errors.Count == 0)
        {
            for (var offset = 0; offset < validTargets.Count; offset += maximumVariableReferencesPerRead)
            {
                batches.Add(new InitialFcReadBatch
                {
                    Index = batches.Count,
                    Targets = validTargets.Skip(offset).Take(maximumVariableReferencesPerRead).ToArray()
                });
            }
        }

        return new InitialFcReadPlan
        {
            MaximumVariableReferencesPerRead = maximumVariableReferencesPerRead,
            MaximumOutstandingReads = 1,
            Targets = validTargets,
            Batches = batches,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static InitialFcReadDataObjectBinding BuildDataObjectBinding(
        LiveIedDataObjectModel dataObject,
        string functionalConstraint)
    {
        var leaves = dataObject.Attributes
            .Where(attribute =>
                string.Equals(
                    NormalizeFc(attribute.FunctionalConstraint),
                    functionalConstraint,
                    StringComparison.Ordinal) &&
                !string.Equals(attribute.SclBType, "Struct", StringComparison.OrdinalIgnoreCase))
            .Select(attribute => new InitialFcReadLeafBinding
            {
                Reference = attribute.ObjectReference,
                AttributePath = attribute.AttributePath,
                FunctionalConstraint = functionalConstraint,
                SclBType = attribute.SclBType
            })
            .ToArray();

        return new InitialFcReadDataObjectBinding
        {
            Name = dataObject.Name,
            Reference = dataObject.Reference,
            Leaves = leaves
        };
    }

    private static string BuildFcRootItem(string logicalNode, string functionalConstraint)
        => string.IsNullOrWhiteSpace(logicalNode) || string.IsNullOrWhiteSpace(functionalConstraint)
            ? string.Empty
            : $"{logicalNode.Trim()}${NormalizeFc(functionalConstraint)}";

    private static string NormalizeFc(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();
}

public sealed class InitialFcProjectedLeaf
{
    public string Reference { get; init; } = string.Empty;
    public string AttributePath { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
    public MmsDataValue Value { get; init; } = null!;
}

public sealed class InitialFcValueProjectionResult
{
    public InitialFcReadTarget Target { get; init; } = new();
    public IReadOnlyList<InitialFcProjectedLeaf> Leaves { get; init; } = Array.Empty<InitialFcProjectedLeaf>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public bool IsExact => Errors.Count == 0 && Leaves.Count > 0;
    public string Summary => $"FC-root projection {Target.MmsReference}: leaves={Leaves.Count}, errors={Errors.Count}.";
}

/// <summary>
/// Projects an FC-root structure into SCL leaves only when structure cardinality is exact.
/// Arrays or mismatched child counts remain explicit errors; no positional guessing occurs.
/// </summary>
public static class InitialFcValueProjector
{
    public static InitialFcValueProjectionResult Project(InitialFcReadTarget target, MmsDataValue value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);
        var leaves = new List<InitialFcProjectedLeaf>();
        var errors = new List<string>();

        if (target.DataObjects.Count == 0)
        {
            errors.Add("No SCL DataObject shape is attached to this FC-root target.");
            return Build(target, leaves, errors);
        }

        if (value.Kind != MmsDataKind.Structure)
        {
            errors.Add($"FC-root value must be Structure for deterministic SCL projection; received {value.Kind}.");
            return Build(target, leaves, errors);
        }

        if (value.Children.Count != target.DataObjects.Count)
        {
            errors.Add($"FC-root DataObject count mismatch: SCL expects {target.DataObjects.Count}, MMS returned {value.Children.Count}.");
            return Build(target, leaves, errors);
        }

        for (var index = 0; index < target.DataObjects.Count; index++)
        {
            var designObject = target.DataObjects[index];
            var flattened = new List<MmsDataValue>();
            if (!TryFlattenStructureOnly(value.Children[index], flattened, out var flattenError))
            {
                errors.Add($"{designObject.Reference}: {flattenError}");
                continue;
            }

            if (flattened.Count != designObject.Leaves.Count)
            {
                errors.Add($"{designObject.Reference}: SCL expects {designObject.Leaves.Count} leaf value(s), MMS returned {flattened.Count} after structure flattening.");
                continue;
            }

            for (var leafIndex = 0; leafIndex < designObject.Leaves.Count; leafIndex++)
            {
                var binding = designObject.Leaves[leafIndex];
                leaves.Add(new InitialFcProjectedLeaf
                {
                    Reference = binding.Reference,
                    AttributePath = binding.AttributePath,
                    FunctionalConstraint = binding.FunctionalConstraint,
                    SclBType = binding.SclBType,
                    Value = flattened[leafIndex]
                });
            }
        }

        return Build(target, leaves, errors);
    }

    private static bool TryFlattenStructureOnly(
        MmsDataValue value,
        ICollection<MmsDataValue> leaves,
        out string error)
    {
        error = string.Empty;
        if (value.Kind == MmsDataKind.Array)
        {
            error = "MMS Array encountered; array cardinality is not inferred from flattened SCL paths.";
            return false;
        }

        if (value.Kind == MmsDataKind.Structure)
        {
            foreach (var child in value.Children)
            {
                if (!TryFlattenStructureOnly(child, leaves, out error))
                    return false;
            }
            return true;
        }

        if (value.Kind == MmsDataKind.Unknown)
        {
            error = "Unknown MMS Data encountered.";
            return false;
        }

        leaves.Add(value);
        return true;
    }

    private static InitialFcValueProjectionResult Build(
        InitialFcReadTarget target,
        IReadOnlyCollection<InitialFcProjectedLeaf> leaves,
        IReadOnlyCollection<string> errors)
        => new()
        {
            Target = target,
            Leaves = leaves.ToArray(),
            Errors = errors.ToArray()
        };
}
