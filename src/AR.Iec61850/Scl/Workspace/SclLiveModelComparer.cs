using System.Globalization;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Scl.Workspace;

public enum SclLiveModelFindingKind
{
    IdentityMismatch,
    MissingLiveAttribute,
    UnexpectedLiveAttribute,
    FunctionalConstraintMismatch,
    TypeMismatch,
    MissingLiveDataSet,
    UnexpectedLiveDataSet,
    DataSetMemberCountMismatch,
    MissingLiveReportControl,
    UnexpectedLiveReportControl,
    ReportControlModeMismatch,
    ReportDataSetMismatch,
    ReportConfigurationRevisionMismatch
}

public sealed class SclLiveModelComparisonFinding
{
    public string Severity { get; init; } = "Info";
    public SclLiveModelFindingKind Kind { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Expected { get; init; } = string.Empty;
    public string Observed { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class SclLiveModelComparisonResult
{
    public string ExpectedIedName { get; init; } = string.Empty;
    public string ObservedIedName { get; init; } = string.Empty;
    public int ExpectedAttributeCount { get; init; }
    public int ObservedAttributeCount { get; init; }
    public int MatchedAttributeCount { get; init; }
    public IReadOnlyList<SclLiveModelComparisonFinding> Findings { get; init; }
        = Array.Empty<SclLiveModelComparisonFinding>();

    public int BlockingFindingCount
        => Findings.Count(x => string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase));

    public bool IsCompatible => BlockingFindingCount == 0;
    public bool CanUseDesignModel => IsCompatible;
    public bool RequiresFullDiscovery => !IsCompatible;
}

public static class SclLiveModelComparer
{
    public static SclLiveModelComparisonResult Compare(
        LiveIedModelDiscoveryDocument expected,
        LiveIedModelDiscoveryDocument observed)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);

        var findings = new List<SclLiveModelComparisonFinding>();
        CompareIdentity(expected, observed, findings);

        var expectedAttributes = FlattenAttributes(expected);
        var observedAttributes = FlattenAttributes(observed);
        var matchedAttributes = 0;

        foreach (var expectedPair in expectedAttributes.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!observedAttributes.TryGetValue(expectedPair.Key, out var observedAttribute))
            {
                findings.Add(Finding(
                    "Error",
                    SclLiveModelFindingKind.MissingLiveAttribute,
                    expectedPair.Value.Reference,
                    expectedPair.Value.DisplayType,
                    string.Empty,
                    $"SCL attribute '{expectedPair.Value.Reference}' is missing from the observed live model."));
                continue;
            }

            matchedAttributes++;
            CompareAttribute(expectedPair.Value, observedAttribute, findings);
        }

        foreach (var observedPair in observedAttributes.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (expectedAttributes.ContainsKey(observedPair.Key))
                continue;

            findings.Add(Finding(
                "Info",
                SclLiveModelFindingKind.UnexpectedLiveAttribute,
                observedPair.Value.Reference,
                string.Empty,
                observedPair.Value.DisplayType,
                $"Live attribute '{observedPair.Value.Reference}' is not present in the SCL design model."));
        }

        CompareDataSets(expected, observed, findings);
        CompareReportControls(expected, observed, findings);

        return new SclLiveModelComparisonResult
        {
            ExpectedIedName = expected.IedName,
            ObservedIedName = observed.IedName,
            ExpectedAttributeCount = expectedAttributes.Count,
            ObservedAttributeCount = observedAttributes.Count,
            MatchedAttributeCount = matchedAttributes,
            Findings = findings
                .OrderByDescending(x => SeverityRank(x.Severity))
                .ThenBy(x => x.Kind)
                .ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static void CompareIdentity(
        LiveIedModelDiscoveryDocument expected,
        LiveIedModelDiscoveryDocument observed,
        ICollection<SclLiveModelComparisonFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(expected.IedName) ||
            string.IsNullOrWhiteSpace(observed.IedName) ||
            Same(expected.IedName, observed.IedName) ||
            Same(expected.IedName, "TEMPLATE"))
        {
            return;
        }

        findings.Add(Finding(
            "Error",
            SclLiveModelFindingKind.IdentityMismatch,
            expected.AccessPointName,
            expected.IedName,
            observed.IedName,
            $"The connected live IED identity '{observed.IedName}' does not match SCL IED '{expected.IedName}'."));
    }

    private static void CompareAttribute(
        AttributeDescriptor expected,
        AttributeDescriptor observed,
        ICollection<SclLiveModelComparisonFinding> findings)
    {
        if (!string.IsNullOrWhiteSpace(expected.FunctionalConstraint) &&
            !string.IsNullOrWhiteSpace(observed.FunctionalConstraint) &&
            !Same(expected.FunctionalConstraint, observed.FunctionalConstraint))
        {
            findings.Add(Finding(
                "Error",
                SclLiveModelFindingKind.FunctionalConstraintMismatch,
                expected.Reference,
                expected.FunctionalConstraint,
                observed.FunctionalConstraint,
                $"Functional constraint mismatch for '{expected.Reference}': SCL={expected.FunctionalConstraint}, live={observed.FunctionalConstraint}."));
        }

        if (!string.IsNullOrWhiteSpace(expected.ComparableType) &&
            !string.IsNullOrWhiteSpace(observed.ComparableType) &&
            !Same(expected.ComparableType, observed.ComparableType))
        {
            findings.Add(Finding(
                "Error",
                SclLiveModelFindingKind.TypeMismatch,
                expected.Reference,
                expected.DisplayType,
                observed.DisplayType,
                $"Type mismatch for '{expected.Reference}': SCL={expected.DisplayType}, live={observed.DisplayType}."));
        }
    }

    private static void CompareDataSets(
        LiveIedModelDiscoveryDocument expected,
        LiveIedModelDiscoveryDocument observed,
        ICollection<SclLiveModelComparisonFinding> findings)
    {
        var expectedIndex = expected.DataSets
            .GroupBy(x => DataSetKey(x, expected.IedName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var observedIndex = observed.DataSets
            .GroupBy(x => DataSetKey(x, observed.IedName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var pair in expectedIndex)
        {
            if (!observedIndex.TryGetValue(pair.Key, out var observedDataSet))
            {
                findings.Add(Finding(
                    "Error",
                    SclLiveModelFindingKind.MissingLiveDataSet,
                    pair.Value.Reference,
                    pair.Value.MemberCount.ToString(CultureInfo.InvariantCulture),
                    string.Empty,
                    $"SCL DataSet '{pair.Value.Reference}' is missing from the observed live model."));
                continue;
            }

            if (pair.Value.MemberCount != observedDataSet.MemberCount)
            {
                findings.Add(Finding(
                    "Error",
                    SclLiveModelFindingKind.DataSetMemberCountMismatch,
                    pair.Value.Reference,
                    pair.Value.MemberCount.ToString(CultureInfo.InvariantCulture),
                    observedDataSet.MemberCount.ToString(CultureInfo.InvariantCulture),
                    $"DataSet member-count mismatch for '{pair.Value.Reference}': SCL={pair.Value.MemberCount}, live={observedDataSet.MemberCount}."));
            }
        }

        foreach (var pair in observedIndex)
        {
            if (expectedIndex.ContainsKey(pair.Key))
                continue;

            findings.Add(Finding(
                "Info",
                SclLiveModelFindingKind.UnexpectedLiveDataSet,
                pair.Value.Reference,
                string.Empty,
                pair.Value.MemberCount.ToString(CultureInfo.InvariantCulture),
                $"Live DataSet '{pair.Value.Reference}' is not present in the SCL design model."));
        }
    }

    private static void CompareReportControls(
        LiveIedModelDiscoveryDocument expected,
        LiveIedModelDiscoveryDocument observed,
        ICollection<SclLiveModelComparisonFinding> findings)
    {
        var expectedIndex = expected.ReportControls
            .GroupBy(x => ReportKey(x, expected.IedName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var observedIndex = observed.ReportControls
            .GroupBy(x => ReportKey(x, observed.IedName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var pair in expectedIndex)
        {
            if (!observedIndex.TryGetValue(pair.Key, out var observedReport))
            {
                findings.Add(Finding(
                    "Error",
                    SclLiveModelFindingKind.MissingLiveReportControl,
                    pair.Value.Reference,
                    pair.Value.Buffered ? "BRCB" : "URCB",
                    string.Empty,
                    $"SCL report control '{pair.Value.Reference}' is missing from the observed live model."));
                continue;
            }

            if (pair.Value.Buffered != observedReport.Buffered)
            {
                findings.Add(Finding(
                    "Error",
                    SclLiveModelFindingKind.ReportControlModeMismatch,
                    pair.Value.Reference,
                    pair.Value.Buffered ? "BRCB" : "URCB",
                    observedReport.Buffered ? "BRCB" : "URCB",
                    $"Report-control mode mismatch for '{pair.Value.Reference}'."));
            }

            if (!string.IsNullOrWhiteSpace(pair.Value.DataSetReference) &&
                !string.IsNullOrWhiteSpace(observedReport.DataSetReference) &&
                !Same(ReferenceTail(pair.Value.DataSetReference), ReferenceTail(observedReport.DataSetReference)))
            {
                findings.Add(Finding(
                    "Error",
                    SclLiveModelFindingKind.ReportDataSetMismatch,
                    pair.Value.Reference,
                    pair.Value.DataSetReference,
                    observedReport.DataSetReference,
                    $"Report DataSet mismatch for '{pair.Value.Reference}'."));
            }

            if (TryParsePositiveUInt(pair.Value.ConfRev, out var expectedConfRev) &&
                TryParsePositiveUInt(observedReport.ConfRev, out var observedConfRev) &&
                expectedConfRev != observedConfRev)
            {
                findings.Add(Finding(
                    "Error",
                    SclLiveModelFindingKind.ReportConfigurationRevisionMismatch,
                    pair.Value.Reference,
                    expectedConfRev.ToString(CultureInfo.InvariantCulture),
                    observedConfRev.ToString(CultureInfo.InvariantCulture),
                    $"Report confRev mismatch for '{pair.Value.Reference}': SCL={expectedConfRev}, live={observedConfRev}."));
            }
        }

        foreach (var pair in observedIndex)
        {
            if (expectedIndex.ContainsKey(pair.Key))
                continue;

            findings.Add(Finding(
                "Info",
                SclLiveModelFindingKind.UnexpectedLiveReportControl,
                pair.Value.Reference,
                string.Empty,
                pair.Value.Buffered ? "BRCB" : "URCB",
                $"Live report control '{pair.Value.Reference}' is not present in the SCL design model."));
        }
    }

    private static Dictionary<string, AttributeDescriptor> FlattenAttributes(
        LiveIedModelDiscoveryDocument document)
    {
        var result = new Dictionary<string, AttributeDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var logicalDevice in document.LogicalDevices)
        {
            var ldKey = LogicalDeviceKey(logicalDevice, document.IedName);
            foreach (var logicalNode in logicalDevice.LogicalNodes)
            {
                foreach (var dataObject in logicalNode.DataObjects)
                {
                    foreach (var attribute in dataObject.Attributes)
                    {
                        var key = $"{ldKey}|{logicalNode.Name}|{dataObject.Name}|{attribute.AttributePath}";
                        result.TryAdd(key, new AttributeDescriptor(
                            key,
                            $"{logicalDevice.MmsDomain}/{logicalNode.Name}.{dataObject.Name}.{attribute.AttributePath}",
                            attribute.FunctionalConstraint,
                            ComparableType(attribute),
                            DisplayType(attribute)));
                    }
                }
            }
        }
        return result;
    }

    private static string DataSetKey(LiveIedDataSetModel dataSet, string iedName)
        => $"{NormalizeDomain(dataSet.Domain, iedName)}|{dataSet.LogicalNode}|{dataSet.Name}";

    private static string ReportKey(LiveIedReportControlModel report, string iedName)
        => $"{NormalizeDomain(report.Domain, iedName)}|{report.LogicalNode}|{report.Name}";

    private static string LogicalDeviceKey(LiveIedLogicalDeviceModel logicalDevice, string iedName)
    {
        if (!string.IsNullOrWhiteSpace(logicalDevice.Inst) &&
            !Same(logicalDevice.Inst, logicalDevice.MmsDomain))
        {
            return logicalDevice.Inst.Trim();
        }
        return NormalizeDomain(logicalDevice.MmsDomain, iedName);
    }

    private static string NormalizeDomain(string domain, string iedName)
    {
        var trimmed = domain.Trim();
        if (!string.IsNullOrWhiteSpace(iedName) &&
            trimmed.StartsWith(iedName, StringComparison.OrdinalIgnoreCase) &&
            trimmed.Length > iedName.Length)
        {
            return trimmed[iedName.Length..];
        }
        return trimmed;
    }

    private static string ComparableType(LiveIedDataAttributeModel attribute)
    {
        var type = FirstNonEmpty(attribute.SclBType, attribute.MmsType);
        if (string.IsNullOrWhiteSpace(type))
            type = attribute.MmsTypeSignature;
        if (type.Contains(':'))
            type = type[..type.IndexOf(':')];

        return type.Trim().ToUpperInvariant() switch
        {
            "BOOL" => "BOOLEAN",
            "INTEGER" or "INT" => "INT32",
            "UNSIGNED" => "INT32U",
            "FLOATINGPOINT" or "FLOAT" => "FLOAT32",
            "BITSTRING" => "BIT-STRING",
            _ => type.Trim().ToUpperInvariant()
        };
    }

    private static string DisplayType(LiveIedDataAttributeModel attribute)
        => FirstNonEmpty(attribute.MmsTypeSignature, FirstNonEmpty(attribute.SclBType, attribute.MmsType));

    private static string ReferenceTail(string reference)
    {
        var value = reference.Trim();
        var slash = value.IndexOf('/');
        return slash >= 0 && slash + 1 < value.Length ? value[(slash + 1)..] : value;
    }

    private static bool TryParsePositiveUInt(string text, out uint value)
        => uint.TryParse(text, out value) && value > 0;

    private static string FirstNonEmpty(string first, string second)
        => string.IsNullOrWhiteSpace(first) ? second : first;

    private static SclLiveModelComparisonFinding Finding(
        string severity,
        SclLiveModelFindingKind kind,
        string reference,
        string expected,
        string observed,
        string message)
        => new()
        {
            Severity = severity,
            Kind = kind,
            Reference = reference,
            Expected = expected,
            Observed = observed,
            Message = message
        };

    private static int SeverityRank(string severity)
        => string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase) ? 2 : 1;

    private static bool Same(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record AttributeDescriptor(
        string Key,
        string Reference,
        string FunctionalConstraint,
        string ComparableType,
        string DisplayType);
}
