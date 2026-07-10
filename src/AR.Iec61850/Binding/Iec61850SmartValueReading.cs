using System.Globalization;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Binding;

public sealed record Iec61850DiscoveredIdentity(
    string IedName,
    string Host,
    IReadOnlyDictionary<string, string> LogicalDeviceAliases,
    string Source,
    string Confidence)
{
    public string DisplayName => string.IsNullOrWhiteSpace(IedName) ? Host : IedName;
}

public static class Iec61850IdentityResolver
{
    public static Iec61850DiscoveredIdentity Resolve(LiveIedModelDiscoveryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ResolveFromDomains(document.LogicalDevices.Select(x => x.MmsDomain), document.Host, document.IedName);
    }

    public static Iec61850DiscoveredIdentity ResolveFromDomains(IEnumerable<string> domains, string host, string? fallbackName = null)
    {
        var identity = LiveIedIdentityResolver.Resolve(domains, host, fallbackName: fallbackName);

        return new Iec61850DiscoveredIdentity(
            identity.IedName,
            host,
            identity.LogicalDeviceAliases,
            identity.Source,
            identity.Confidence.ToString());
    }

    public static string DisplayLogicalDevice(Iec61850DiscoveredIdentity identity, string mmsDomain)
        => identity.LogicalDeviceAliases.TryGetValue(mmsDomain, out var alias) && !string.IsNullOrWhiteSpace(alias)
            ? alias
            : mmsDomain;
}

public sealed record Iec61850SmartReadTarget(string Reference, string FunctionalConstraint, string Purpose, int Priority);

public static class Iec61850SmartReadPlanBuilder
{
    public static IReadOnlyList<Iec61850SmartReadTarget> BuildForLogicalNode(LiveIedLogicalNodeModel logicalNode, int maxDataObjects = 64)
    {
        ArgumentNullException.ThrowIfNull(logicalNode);
        return logicalNode.DataObjects
            .SelectMany(BuildForDataObject)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxDataObjects * 12))
            .ToArray();
    }

    public static IReadOnlyList<Iec61850SmartReadTarget> BuildForDataObject(LiveIedDataObjectModel dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        var schema = Iec61850DataObjectSchemaBuilder.FromLiveDataObject(dataObject).ToRootNode();
        var targets = new List<Iec61850SmartReadTarget>();
        AddReadableSchemaTargets(schema, targets);

        return targets
            .Where(x => !string.IsNullOrWhiteSpace(x.Reference) && !string.IsNullOrWhiteSpace(x.FunctionalConstraint))
            .GroupBy(x => x.Reference + "|" + x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderBy(t => t.Priority).First())
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddReadableSchemaTargets(Iec61850ValueSchemaNode node, ICollection<Iec61850SmartReadTarget> targets)
    {
        if (IsReadableSchemaNode(node))
            targets.Add(new Iec61850SmartReadTarget(node.Reference, node.FunctionalConstraint, node.SemanticKind, ReadPriority(node)));

        foreach (var child in node.Children)
            AddReadableSchemaTargets(child, targets);
    }

    private static bool IsReadableSchemaNode(Iec61850ValueSchemaNode node)
    {
        if (node.Children.Count > 0 && node.SemanticKind is "SchemaGroup" or "DataObject" or "ControlOperation")
            return false;
        if (string.IsNullOrWhiteSpace(node.Reference) || string.IsNullOrWhiteSpace(node.FunctionalConstraint))
            return false;
        if (node.Source is "QualityTemplate" or "TimestampTemplate" or "CdcControlTemplate" or "OriginTemplate" or "SchemaGroup" or "DataObjectSchema")
            return false;
        if (node.Reference.Contains('[', StringComparison.Ordinal))
            return false;
        return true;
    }

    private static int ReadPriority(Iec61850ValueSchemaNode node)
    {
        var name = node.Name.ToUpperInvariant();
        return name switch
        {
            "STVAL" => 0,
            "GENERAL" => 1,
            "DIRGENERAL" => 2,
            "PHSA" => 3,
            "DIRPHSA" => 4,
            "PHSB" => 5,
            "DIRPHSB" => 6,
            "PHSC" => 7,
            "DIRPHSC" => 8,
            "CVAL" => 10,
            "INSTCVAL" => 11,
            "MAG" => 12,
            "F" => 13,
            "ANG" => 14,
            "Q" => 40,
            "T" => 41,
            "STSELD" => 50,
            "CTLMODEL" => 60,
            _ => FunctionalConstraintPriority(node.FunctionalConstraint) + 100
        };
    }

    private static int FunctionalConstraintPriority(string fc)
        => fc.ToUpperInvariant() switch
        {
            "ST" => 0,
            "MX" => 20,
            "CF" => 80,
            "DC" => 90,
            "CO" => 120,
            _ => 200
        };
}

public sealed record Iec61850PresentationValueNode(
    string Name,
    string FunctionalConstraint,
    string Type,
    string Value,
    string Status,
    IReadOnlyList<Iec61850PresentationValueNode> Children);

public sealed record Iec61850SmartValueSummary(string Value, string Marker, string Reason);

public static class Iec61850SmartValueSummaryEngine
{
    public static Iec61850SmartValueSummary Summarize(Iec61850PresentationValueNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var quality = FindChild(node, "q");
        var timestamp = FindChild(node, "t");
        var marker = MarkerFromQualityAndTime(quality, timestamp, node.Status);

        var summary = SummaryByNameAndCdc(node);
        if (string.IsNullOrWhiteSpace(summary) || summary == "-")
            summary = FirstMeaningfulChildValue(node);

        if (string.IsNullOrWhiteSpace(summary))
            summary = node.Value;

        return new Iec61850SmartValueSummary(string.IsNullOrWhiteSpace(summary) ? "-" : summary, marker, marker.Length == 0 ? "" : "quality/time/status");
    }

    private static string SummaryByNameAndCdc(Iec61850PresentationValueNode node)
    {
        var type = node.Type.ToUpperInvariant();
        var name = node.Name.ToUpperInvariant();

        if (name is "A" or "PHV" or "PPV" or "W" or "VAR" or "VA" or "PF")
            return PhaseSummary(node);

        if (type is "DPC" or "DPS" or "SPC" or "SPS" or "INC" or "INS" or "ENC" or "ENS" || name is "POS" or "STR" or "OP" or "BEH" or "MOD" or "HEALTH" or "LOC")
            return ValueOf(node, "stVal") ?? ValueOf(node, "general") ?? node.Value;

        if (type is "MV")
            return ValueOfPath(node, "instMag", "f") ?? ValueOfPath(node, "mag", "f") ?? ValueOf(node, "f") ?? node.Value;

        if (name.StartsWith("TOT", StringComparison.OrdinalIgnoreCase) || name is "HZ")
            return ValueOfPath(node, "instMag", "f") ?? ValueOfPath(node, "mag", "f") ?? ValueOf(node, "f") ?? node.Value;

        return string.Empty;
    }

    private static string PhaseSummary(Iec61850PresentationValueNode node)
    {
        var phases = new[] { "phsA", "phsB", "phsC", "neut", "res" };
        var values = phases
            .Select(phase => FindChild(node, phase))
            .Where(child => child != null)
            .Select(child => VectorSummary(child!))
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "-")
            .ToArray();
        return values.Length == 0 ? VectorSummary(node) : string.Join(", ", values);
    }

    private static string VectorSummary(Iec61850PresentationValueNode node)
    {
        var mag = ValueOfPath(node, "cVal", "mag", "f")
            ?? ValueOfPath(node, "instCVal", "mag", "f")
            ?? ValueOfPath(node, "mag", "f")
            ?? ValueOf(node, "mag")
            ?? ValueOf(node, "f");
        var ang = ValueOfPath(node, "cVal", "ang", "f")
            ?? ValueOfPath(node, "instCVal", "ang", "f")
            ?? ValueOfPath(node, "ang", "f")
            ?? ValueOf(node, "ang");

        if (!string.IsNullOrWhiteSpace(mag) && !string.IsNullOrWhiteSpace(ang))
            return $"{mag} ∠ {EnsureDegree(ang)}";
        if (!string.IsNullOrWhiteSpace(mag))
            return mag;
        return node.Value;
    }

    private static string? ValueOf(Iec61850PresentationValueNode node, string childName)
        => FindChild(node, childName)?.Value is { Length: > 0 } value && value != "-" ? value : null;

    private static string? ValueOfPath(Iec61850PresentationValueNode node, params string[] path)
    {
        var current = node;
        foreach (var segment in path)
        {
            var next = FindChild(current, segment);
            if (next == null)
                return null;
            current = next;
        }
        return string.IsNullOrWhiteSpace(current.Value) || current.Value == "-" ? null : current.Value;
    }

    private static Iec61850PresentationValueNode? FindChild(Iec61850PresentationValueNode node, string childName)
        => node.Children.FirstOrDefault(x => x.Name.Equals(childName, StringComparison.OrdinalIgnoreCase));

    private static string FirstMeaningfulChildValue(Iec61850PresentationValueNode node)
    {
        foreach (var name in new[] { "stVal", "general", "mag", "f", "cVal", "instCVal", "q", "t" })
        {
            var child = FindChild(node, name);
            if (child == null)
                continue;
            var value = Summarize(child).Value;
            if (!string.IsNullOrWhiteSpace(value) && value != "-" && !value.StartsWith("Struct(", StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return string.Empty;
    }

    private static string MarkerFromQualityAndTime(Iec61850PresentationValueNode? quality, Iec61850PresentationValueNode? timestamp, string status)
    {
        if (status.Contains("failed", StringComparison.OrdinalIgnoreCase) || status.Contains("mismatch", StringComparison.OrdinalIgnoreCase))
            return "!";

        if (quality != null)
        {
            var validity = ValueOf(quality, "Validity") ?? quality.Value;
            if (!string.IsNullOrWhiteSpace(validity) && !validity.Equals("good", StringComparison.OrdinalIgnoreCase) && validity != "-")
                return "!";
            if (BoolChild(quality, "OldData") || BoolChild(quality, "Test") || BoolChild(quality, "OperatorBlocked"))
                return "!";
        }

        if (timestamp != null && (BoolChild(timestamp, "ClockFailure") || BoolChild(timestamp, "ClockNotSynchronized")))
            return "!";

        return string.Empty;
    }

    private static bool BoolChild(Iec61850PresentationValueNode node, string name)
        => ValueOf(node, name)?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    private static string EnsureDegree(string value)
        => value.Contains('°', StringComparison.Ordinal) ? value : value + "°";
}
