using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Mms;

/// <summary>
/// Builds the existing bounded FC-root Read contract directly from the shared canonical
/// model. The planner is intentionally allocation-conscious on high-cardinality signal
/// inventories: parent/child relationships are indexed once, then traversed without
/// repeatedly scanning the full signal table.
/// </summary>
public static class CanonicalInitialFcReadPlanner
{
    public static InitialFcReadPlan FromCanonicalModel(
        CanonicalIedModel model,
        IEnumerable<string>? allowedDomains = null,
        int maximumVariableReferencesPerRead = MmsReadBatchCodec.MaximumVariableReferencesPerRead)
    {
        ArgumentNullException.ThrowIfNull(model);

        var allowed = allowedDomains is null
            ? null
            : new HashSet<string>(
                allowedDomains
                    .Where(domain => !string.IsNullOrWhiteSpace(domain))
                    .Select(domain => domain.Trim()),
                StringComparer.Ordinal);

        var warnings = new List<string>();
        var logicalNodesByDevice = BuildLogicalNodeIndex(model, warnings);
        var dataObjectsByNode = BuildDataObjectIndex(model, warnings);
        var signalsByDataObject = BuildSignalIndex(model, warnings);
        var targets = new List<InitialFcReadTarget>();

        foreach (var logicalDevice in model.LogicalDevices)
        {
            if (!IsValidId(logicalDevice.Id, logicalNodesByDevice.Length))
            {
                warnings.Add($"Canonical logical-device row id {logicalDevice.Id} is outside the dense snapshot range and was skipped.");
                continue;
            }

            var domain = model.Strings.Resolve(logicalDevice.MmsDomain).Trim();
            if (domain.Length == 0 || (allowed is not null && !allowed.Contains(domain)))
                continue;

            var logicalNodes = logicalNodesByDevice[logicalDevice.Id];
            if (logicalNodes is null)
                continue;

            foreach (var logicalNode in logicalNodes)
            {
                if (!IsValidId(logicalNode.Id, dataObjectsByNode.Length))
                {
                    warnings.Add($"Canonical logical-node row id {logicalNode.Id} is outside the dense snapshot range and was skipped.");
                    continue;
                }

                var logicalNodeName = model.Strings.Resolve(logicalNode.Name).Trim();
                if (logicalNodeName.Length == 0)
                    continue;

                var dataObjects = dataObjectsByNode[logicalNode.Id];
                if (dataObjects is null)
                    continue;

                var constraints = CollectFunctionalConstraints(model, dataObjects, signalsByDataObject);
                foreach (var functionalConstraint in constraints)
                {
                    var bindings = new List<InitialFcReadDataObjectBinding>(dataObjects.Count);
                    foreach (var dataObject in dataObjects)
                    {
                        var binding = BuildDataObjectBinding(
                            model,
                            dataObject,
                            functionalConstraint,
                            signalsByDataObject,
                            warnings);
                        if (binding.Leaves.Count > 0)
                            bindings.Add(binding);
                    }

                    if (bindings.Count == 0)
                        continue;

                    targets.Add(new InitialFcReadTarget
                    {
                        Domain = domain,
                        LogicalNode = logicalNodeName,
                        FunctionalConstraint = functionalConstraint,
                        MmsItemName = $"{logicalNodeName}${functionalConstraint}",
                        Source = $"Canonical:{model.Source.Ingress}",
                        DataObjects = bindings.ToArray()
                    });
                }
            }
        }

        var ordered = targets
            .OrderBy(target => target.Domain, StringComparer.Ordinal)
            .ThenBy(target => target.LogicalNode, StringComparer.Ordinal)
            .ThenBy(target => target.FunctionalConstraint, StringComparer.Ordinal)
            .ToArray();

        var plan = InitialFcReadPlanner.Build(ordered, maximumVariableReferencesPerRead);
        return warnings.Count == 0 ? plan : WithWarnings(plan, warnings);
    }

    private static List<CanonicalLogicalNodeRow>?[] BuildLogicalNodeIndex(
        CanonicalIedModel model,
        ICollection<string> warnings)
    {
        var result = new List<CanonicalLogicalNodeRow>?[model.LogicalDevices.Length];
        foreach (var row in model.LogicalNodes)
        {
            if (!IsValidId(row.LogicalDeviceId, result.Length))
            {
                warnings.Add($"Canonical logical node id {row.Id} references missing logical-device id {row.LogicalDeviceId}; row was skipped.");
                continue;
            }

            (result[row.LogicalDeviceId] ??= new List<CanonicalLogicalNodeRow>()).Add(row);
        }

        return result;
    }

    private static List<CanonicalDataObjectRow>?[] BuildDataObjectIndex(
        CanonicalIedModel model,
        ICollection<string> warnings)
    {
        var result = new List<CanonicalDataObjectRow>?[model.LogicalNodes.Length];
        foreach (var row in model.DataObjects)
        {
            if (!IsValidId(row.LogicalNodeId, result.Length))
            {
                warnings.Add($"Canonical data-object id {row.Id} references missing logical-node id {row.LogicalNodeId}; row was skipped.");
                continue;
            }

            (result[row.LogicalNodeId] ??= new List<CanonicalDataObjectRow>()).Add(row);
        }

        return result;
    }

    private static List<CanonicalSignalRow>?[] BuildSignalIndex(
        CanonicalIedModel model,
        ICollection<string> warnings)
    {
        var result = new List<CanonicalSignalRow>?[model.DataObjects.Length];
        foreach (var row in model.Signals)
        {
            if (!IsValidId(row.DataObjectId, result.Length))
            {
                warnings.Add($"Canonical signal id {row.Id} references missing data-object id {row.DataObjectId}; row was skipped.");
                continue;
            }

            (result[row.DataObjectId] ??= new List<CanonicalSignalRow>()).Add(row);
        }

        return result;
    }

    private static IReadOnlyList<string> CollectFunctionalConstraints(
        CanonicalIedModel model,
        IReadOnlyList<CanonicalDataObjectRow> dataObjects,
        IReadOnlyList<CanonicalSignalRow>?[] signalsByDataObject)
    {
        var constraints = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var dataObject in dataObjects)
        {
            if (!IsValidId(dataObject.Id, signalsByDataObject.Length))
                continue;

            var signals = signalsByDataObject[dataObject.Id];
            if (signals is null)
                continue;

            foreach (var signal in signals)
            {
                var functionalConstraint = NormalizeFc(model.Strings.Resolve(signal.FunctionalConstraint));
                if (functionalConstraint.Length > 0)
                    constraints.Add(functionalConstraint);
            }
        }

        return constraints.ToArray();
    }

    private static InitialFcReadDataObjectBinding BuildDataObjectBinding(
        CanonicalIedModel model,
        CanonicalDataObjectRow dataObject,
        string functionalConstraint,
        IReadOnlyList<CanonicalSignalRow>?[] signalsByDataObject,
        ICollection<string> warnings)
    {
        if (!IsValidId(dataObject.Id, signalsByDataObject.Length))
        {
            warnings.Add($"Canonical data-object row id {dataObject.Id} is outside the dense snapshot range and was skipped.");
            return new InitialFcReadDataObjectBinding();
        }

        var sourceSignals = signalsByDataObject[dataObject.Id];
        if (sourceSignals is null)
            return new InitialFcReadDataObjectBinding();

        var leaves = new List<InitialFcReadLeafBinding>(sourceSignals.Count);
        foreach (var signal in sourceSignals)
        {
            if (!string.Equals(
                    NormalizeFc(model.Strings.Resolve(signal.FunctionalConstraint)),
                    functionalConstraint,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var basicType = model.Strings.Resolve(signal.BasicType);
            if (string.Equals(basicType, "Struct", StringComparison.OrdinalIgnoreCase))
                continue;

            leaves.Add(new InitialFcReadLeafBinding
            {
                Reference = model.Strings.Resolve(signal.ObjectReference),
                AttributePath = model.Strings.Resolve(signal.AttributePath),
                FunctionalConstraint = functionalConstraint,
                SclBType = basicType
            });
        }

        return new InitialFcReadDataObjectBinding
        {
            Name = model.Strings.Resolve(dataObject.Name),
            Reference = model.Strings.Resolve(dataObject.Reference),
            Leaves = leaves.ToArray()
        };
    }

    private static InitialFcReadPlan WithWarnings(
        InitialFcReadPlan plan,
        IEnumerable<string> additionalWarnings)
        => new()
        {
            MaximumVariableReferencesPerRead = plan.MaximumVariableReferencesPerRead,
            MaximumOutstandingReads = plan.MaximumOutstandingReads,
            Targets = plan.Targets,
            Batches = plan.Batches,
            Errors = plan.Errors,
            Warnings = plan.Warnings.Concat(additionalWarnings).Distinct(StringComparer.Ordinal).ToArray()
        };

    private static bool IsValidId(int id, int length)
        => id >= 0 && id < length;

    private static string NormalizeFc(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();
}
