using AR.Iec61850.Discovery;

namespace AR.Iec61850.Binding;

public enum Iec61850BindingConfidence
{
    Low,
    Medium,
    High,
    Exact
}

public sealed class Iec61850ValueSchemaNode
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public string BType { get; init; } = string.Empty;
    public string MmsType { get; init; } = string.Empty;
    public string SemanticKind { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public Iec61850BindingConfidence Confidence { get; init; } = Iec61850BindingConfidence.Low;
    public IReadOnlyList<Iec61850ValueSchemaNode> Children { get; init; } = Array.Empty<Iec61850ValueSchemaNode>();

    public bool HasChildren => Children.Count > 0;
}

public sealed class Iec61850DataObjectSchema
{
    public string Name { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public Iec61850BindingConfidence Confidence { get; init; } = Iec61850BindingConfidence.Low;
    public IReadOnlyList<Iec61850ValueSchemaNode> Attributes { get; init; } = Array.Empty<Iec61850ValueSchemaNode>();

    public Iec61850ValueSchemaNode ToRootNode()
        => new()
        {
            Name = Name,
            Path = Name,
            Reference = Reference,
            FunctionalConstraint = string.Empty,
            Cdc = Cdc,
            BType = string.IsNullOrWhiteSpace(Cdc) ? "DataObject" : Cdc,
            MmsType = "DataObject",
            SemanticKind = "DataObject",
            Source = "DataObjectSchema",
            Confidence = Confidence,
            Children = Attributes
        };
}

public static class Iec61850DataObjectSchemaBuilder
{
    public static Iec61850DataObjectSchema FromLiveDataObject(LiveIedDataObjectModel dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        var meaningfulAttributes = dataObject.Attributes
            .Where(attribute => ShouldIncludeAsDaNode(dataObject, attribute))
            .ToArray();

        var children = BuildTree(dataObject, meaningfulAttributes).ToArray();
        return new Iec61850DataObjectSchema
        {
            Name = dataObject.Name,
            Reference = dataObject.Reference,
            Cdc = dataObject.InferredCdc,
            Confidence = ToBindingConfidence(dataObject.ConfidenceLevel),
            Attributes = OrderByCdc(dataObject.InferredCdc, children).ToArray()
        };
    }

    private static bool ShouldIncludeAsDaNode(LiveIedDataObjectModel dataObject, LiveIedDataAttributeModel attribute)
    {
        var path = NormalizePath(attribute.AttributePath);
        if (string.IsNullOrWhiteSpace(path))
            return dataObject.Attributes.Count <= 1;

        // A live server can expose a top-level DO aggregate such as Pos as a readable struct.
        // When richer leaf DA points are available, do not use that aggregate for semantic display;
        // it is positional MMS data and must not be guessed as stVal/q/t.
        if (string.Equals(path, dataObject.Name, StringComparison.OrdinalIgnoreCase) && dataObject.Attributes.Any(x => !string.IsNullOrWhiteSpace(x.AttributePath) && !string.Equals(x.AttributePath, dataObject.Name, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private static IEnumerable<Iec61850ValueSchemaNode> BuildTree(LiveIedDataObjectModel dataObject, IReadOnlyList<LiveIedDataAttributeModel> attributes)
    {
        var roots = new List<MutableSchemaNode>();
        foreach (var attribute in attributes)
        {
            var path = NormalizePath(attribute.AttributePath);
            if (string.IsNullOrWhiteSpace(path))
                path = string.IsNullOrWhiteSpace(dataObject.Name) ? "value" : dataObject.Name;

            var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
                continue;

            var current = roots;
            MutableSchemaNode? node = null;
            var currentPath = string.Empty;
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                currentPath = string.IsNullOrWhiteSpace(currentPath) ? segment : currentPath + "." + segment;
                node = current.FirstOrDefault(x => string.Equals(x.Name, segment, StringComparison.OrdinalIgnoreCase));
                if (node == null)
                {
                    var isLeaf = index == segments.Length - 1;
                    node = new MutableSchemaNode
                    {
                        Name = segment,
                        Path = currentPath,
                        Reference = CombineReference(dataObject.Reference, currentPath),
                        FunctionalConstraint = isLeaf ? attribute.FunctionalConstraint : string.Empty,
                        Cdc = dataObject.InferredCdc,
                        BType = isLeaf ? InferBType(segment, attribute.SclBType, attribute.MmsType, dataObject.InferredCdc) : "Struct",
                        MmsType = isLeaf ? (string.IsNullOrWhiteSpace(attribute.MmsType) ? attribute.TypeDiscoveryStatus : attribute.MmsType) : "Structure",
                        SemanticKind = isLeaf ? InferSemanticKind(segment, attribute.SclBType, attribute.MmsType, dataObject.InferredCdc, attribute.FunctionalConstraint) : "SchemaGroup",
                        Source = isLeaf ? attribute.TypeSource : "SchemaGroup",
                        Confidence = isLeaf ? ToBindingConfidence(attribute.TypeConfidence) : Iec61850BindingConfidence.Medium
                    };
                    current.Add(node);
                }

                if (index == segments.Length - 1)
                {
                    node.Reference = attribute.ObjectReference;
                    node.FunctionalConstraint = attribute.FunctionalConstraint;
                    node.BType = InferBType(segment, attribute.SclBType, attribute.MmsType, dataObject.InferredCdc);
                    node.MmsType = string.IsNullOrWhiteSpace(attribute.MmsType) ? attribute.TypeDiscoveryStatus : attribute.MmsType;
                    node.SemanticKind = InferSemanticKind(segment, attribute.SclBType, attribute.MmsType, dataObject.InferredCdc, attribute.FunctionalConstraint);
                    node.Source = attribute.TypeSource;
                    node.Confidence = ToBindingConfidence(attribute.TypeConfidence);
                }

                current = node.Children;
            }
        }

        AddSyntheticChildren(dataObject.InferredCdc, roots);
        return OrderByCdc(dataObject.InferredCdc, roots.Select(x => x.ToImmutable()));
    }

    private static void AddSyntheticChildren(string cdc, List<MutableSchemaNode> roots)
    {
        foreach (var node in roots.ToArray())
            AddSyntheticChildrenRecursive(cdc, node);

        if (cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) || cdc.Equals("SPC", StringComparison.OrdinalIgnoreCase))
        {
            EnsureControlOperation(roots, "SBOw", cdc);
            EnsureControlOperation(roots, "Oper", cdc);
            EnsureControlOperation(roots, "Cancel", cdc);
        }
    }

    private static void AddSyntheticChildrenRecursive(string cdc, MutableSchemaNode node)
    {
        if (IsQualityNode(node.Name, node.SemanticKind))
        {
            node.Children.Clear();
            node.Children.AddRange(QualityChildren(node));
            return;
        }

        if (IsTimestampNode(node.Name, node.SemanticKind))
        {
            node.Children.Clear();
            node.Children.AddRange(TimestampChildren(node));
            return;
        }

        if (IsOriginNode(node.Name))
            EnsureOriginChildren(node);

        if (IsControlOperationNode(node.Name))
            EnsureControlOperationChildren(node, cdc);

        foreach (var child in node.Children.ToArray())
            AddSyntheticChildrenRecursive(cdc, child);
    }

    private static void EnsureControlOperation(List<MutableSchemaNode> roots, string name, string cdc)
    {
        var existing = roots.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new MutableSchemaNode
            {
                Name = name,
                Path = name,
                FunctionalConstraint = "CO",
                Cdc = cdc,
                BType = "Struct",
                MmsType = "Structure",
                SemanticKind = "ControlOperation",
                Source = "CdcTemplate",
                Confidence = Iec61850BindingConfidence.Medium
            };
            roots.Add(existing);
        }

        EnsureControlOperationChildren(existing, cdc);
    }

    private static void EnsureControlOperationChildren(MutableSchemaNode node, string cdc)
    {
        var names = new[] { "ctlVal", "origin", "ctlNum", "T", "Test", "Check" };
        foreach (var name in names)
        {
            if (node.Children.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            var path = string.IsNullOrWhiteSpace(node.Path) ? name : node.Path + "." + name;
            node.Children.Add(new MutableSchemaNode
            {
                Name = name,
                Path = path,
                Reference = CombineReference(node.Reference, name),
                FunctionalConstraint = "CO",
                Cdc = cdc,
                BType = name switch
                {
                    "ctlVal" => cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) ? "Dbpos" : "BOOLEAN",
                    "origin" => "Struct",
                    "ctlNum" => "INT8U",
                    "T" => "Timestamp",
                    "Test" => "BOOLEAN",
                    "Check" => "Check",
                    _ => "Unknown"
                },
                MmsType = name is "origin" ? "Structure" : string.Empty,
                SemanticKind = name switch
                {
                    "ctlVal" => "ControlValue",
                    "origin" => "Origin",
                    "T" => "Timestamp",
                    "Check" => "Check",
                    _ => name
                },
                Source = "CdcControlTemplate",
                Confidence = Iec61850BindingConfidence.Medium
            });
        }

        var origin = node.Children.FirstOrDefault(x => IsOriginNode(x.Name));
        if (origin != null)
            EnsureOriginChildren(origin);
    }

    private static void EnsureOriginChildren(MutableSchemaNode origin)
    {
        var names = new[] { "orCat", "orIdent" };
        foreach (var name in names)
        {
            if (origin.Children.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            origin.Children.Add(new MutableSchemaNode
            {
                Name = name,
                Path = string.IsNullOrWhiteSpace(origin.Path) ? name : origin.Path + "." + name,
                Reference = CombineReference(origin.Reference, name),
                FunctionalConstraint = origin.FunctionalConstraint,
                Cdc = origin.Cdc,
                BType = name == "orCat" ? "Enum" : "OctetString",
                SemanticKind = name == "orCat" ? "OriginCategory" : "OriginIdentity",
                Source = "OriginTemplate",
                Confidence = Iec61850BindingConfidence.Medium
            });
        }
    }

    private static IEnumerable<MutableSchemaNode> QualityChildren(MutableSchemaNode parent)
    {
        var children = new[]
        {
            "Validity", "Quality Details", "Overflow", "OutOfRange", "BadReference", "Oscillatory", "Failure", "OldData", "Inconsistent", "Inaccurate", "Source", "Test", "OperatorBlocked"
        };

        foreach (var name in children)
        {
            yield return new MutableSchemaNode
            {
                Name = name,
                Path = string.IsNullOrWhiteSpace(parent.Path) ? name : parent.Path + "." + name,
                Reference = CombineReference(parent.Reference, name),
                FunctionalConstraint = parent.FunctionalConstraint,
                Cdc = parent.Cdc,
                BType = name == "Validity" || name == "Source" ? "Enum" : name == "Quality Details" ? "Group" : "BOOLEAN",
                SemanticKind = name == "Quality Details" ? "QualityGroup" : "QualityFlag",
                Source = "QualityTemplate",
                Confidence = Iec61850BindingConfidence.Exact
            };
        }
    }

    private static IEnumerable<MutableSchemaNode> TimestampChildren(MutableSchemaNode parent)
    {
        var children = new[]
        {
            "LeapSecondsKnown", "ClockFailure", "ClockNotSynchronized", "TimeAccuracy"
        };

        foreach (var name in children)
        {
            yield return new MutableSchemaNode
            {
                Name = name,
                Path = string.IsNullOrWhiteSpace(parent.Path) ? name : parent.Path + "." + name,
                Reference = CombineReference(parent.Reference, name),
                FunctionalConstraint = parent.FunctionalConstraint,
                Cdc = parent.Cdc,
                BType = name == "TimeAccuracy" ? "Enum" : "BOOLEAN",
                SemanticKind = "TimestampQualityFlag",
                Source = "TimestampTemplate",
                Confidence = Iec61850BindingConfidence.Exact
            };
        }
    }

    internal static IEnumerable<Iec61850ValueSchemaNode> OrderByCdc(string cdc, IEnumerable<Iec61850ValueSchemaNode> nodes)
    {
        var order = CdcOrder(cdc);
        return nodes
            .OrderBy(x => OrderIndex(order, x.Name))
            .ThenBy(x => FunctionalConstraintOrder(x.FunctionalConstraint))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> CdcOrder(string cdc)
    {
        if (cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) || cdc.Equals("SPC", StringComparison.OrdinalIgnoreCase))
            return ["origin", "ctlNum", "stVal", "q", "t", "stSeld", "SBO", "SBOw", "Oper", "Cancel", "ctlModel", "sboClass", "sboTimeout", "operTimeout"];

        if (cdc.Equals("ACT", StringComparison.OrdinalIgnoreCase))
            return ["general", "phsA", "phsB", "phsC", "neut", "q", "t", "Oper", "SBOw", "Cancel"];

        if (cdc.Equals("ACD", StringComparison.OrdinalIgnoreCase))
            return ["general", "dirGeneral", "phsA", "dirPhsA", "phsB", "dirPhsB", "phsC", "dirPhsC", "neut", "dirNeut", "q", "t"];

        if (cdc.Equals("WYE", StringComparison.OrdinalIgnoreCase) || cdc.Equals("DEL", StringComparison.OrdinalIgnoreCase))
            return ["phsA", "phsB", "phsC", "neut", "res", "q", "t", "range", "rangeAng", "angRef"];

        if (cdc.Equals("MV", StringComparison.OrdinalIgnoreCase) || cdc.Equals("CMV", StringComparison.OrdinalIgnoreCase))
            return ["instCVal", "cVal", "instMag", "mag", "ang", "q", "t", "range", "rangeAng"];

        return ["stVal", "mag", "q", "t", "ctlModel", "Oper", "SBOw", "Cancel"];
    }

    private static int OrderIndex(IReadOnlyList<string> order, string name)
    {
        var index = order.ToList().FindIndex(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 10_000 : index;
    }

    private static int FunctionalConstraintOrder(string fc)
        => fc.ToUpperInvariant() switch
        {
            "ST" => 0,
            "MX" => 1,
            "CO" => 2,
            "SP" => 3,
            "SG" => 4,
            "SE" => 5,
            "CF" => 6,
            "DC" => 7,
            "EX" => 8,
            _ => 100
        };

    private static string NormalizePath(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace('$', '.');

    private static string CombineReference(string? reference, string path)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return path;
        if (string.IsNullOrWhiteSpace(path))
            return reference.Trim();
        if (path.StartsWith("[", StringComparison.Ordinal))
            return reference.Trim() + path;
        return reference.TrimEnd('.') + "." + path.TrimStart('.');
    }

    private static string InferBType(string name, string sclBType, string mmsType, string cdc)
    {
        if (!string.IsNullOrWhiteSpace(sclBType) && !sclBType.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return sclBType.Trim();

        if (IsQualityNode(name, string.Empty))
            return "Quality";
        if (IsTimestampNode(name, string.Empty))
            return "Timestamp";
        if (name.Equals("stVal", StringComparison.OrdinalIgnoreCase) && cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase))
            return "Dbpos";
        if (name.Equals("stVal", StringComparison.OrdinalIgnoreCase))
            return "BOOLEAN";
        if (name.Equals("ctlModel", StringComparison.OrdinalIgnoreCase))
            return "Enum";
        if (name.Equals("origin", StringComparison.OrdinalIgnoreCase) || IsControlOperationNode(name))
            return "Struct";
        if (name.Equals("ctlNum", StringComparison.OrdinalIgnoreCase))
            return "INT8U";
        if (name.Equals("Test", StringComparison.OrdinalIgnoreCase))
            return "BOOLEAN";
        if (name.Equals("Check", StringComparison.OrdinalIgnoreCase))
            return "Check";
        if (!string.IsNullOrWhiteSpace(mmsType))
            return mmsType.Trim();
        return "Unknown";
    }

    private static string InferSemanticKind(string name, string sclBType, string mmsType, string cdc, string fc)
    {
        if (IsQualityNode(name, sclBType))
            return "Quality";
        if (IsTimestampNode(name, sclBType))
            return "Timestamp";
        if (name.Equals("ctlModel", StringComparison.OrdinalIgnoreCase))
            return "ControlModel";
        if (name.Equals("stVal", StringComparison.OrdinalIgnoreCase))
            return cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) ? "DoublePointStatus" : "StatusValue";
        if (name.Equals("ctlVal", StringComparison.OrdinalIgnoreCase))
            return cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) ? "DoublePointControlValue" : "ControlValue";
        if (IsControlOperationNode(name))
            return "ControlOperation";
        if (IsOriginNode(name))
            return "Origin";
        if (name.Equals("orCat", StringComparison.OrdinalIgnoreCase))
            return "OriginCategory";
        if (name.Equals("Check", StringComparison.OrdinalIgnoreCase))
            return "Check";
        if (fc.Equals("CO", StringComparison.OrdinalIgnoreCase))
            return "ControlAttribute";
        return string.IsNullOrWhiteSpace(sclBType) ? "Value" : sclBType.Trim();
    }

    private static bool IsQualityNode(string name, string semanticKind)
        => name.Equals("q", StringComparison.OrdinalIgnoreCase) || semanticKind.Equals("Quality", StringComparison.OrdinalIgnoreCase) || semanticKind.Equals("Quality", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimestampNode(string name, string semanticKind)
        => name.Equals("t", StringComparison.OrdinalIgnoreCase) || semanticKind.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Tm", StringComparison.OrdinalIgnoreCase);

    private static bool IsControlOperationNode(string name)
        => name.Equals("SBOw", StringComparison.OrdinalIgnoreCase) || name.Equals("Oper", StringComparison.OrdinalIgnoreCase) || name.Equals("Cancel", StringComparison.OrdinalIgnoreCase);

    private static bool IsOriginNode(string name)
        => name.Equals("origin", StringComparison.OrdinalIgnoreCase) || name.Equals("Origin", StringComparison.OrdinalIgnoreCase);

    private static Iec61850BindingConfidence ToBindingConfidence(LiveIedDiscoveryConfidenceLevel confidence)
        => confidence switch
        {
            LiveIedDiscoveryConfidenceLevel.Exact => Iec61850BindingConfidence.Exact,
            LiveIedDiscoveryConfidenceLevel.High => Iec61850BindingConfidence.High,
            LiveIedDiscoveryConfidenceLevel.Medium => Iec61850BindingConfidence.Medium,
            _ => Iec61850BindingConfidence.Low
        };


    private static IEnumerable<Iec61850ValueSchemaNode> OrderChildNodes(string parentName, string parentSemanticKind, string cdc, IEnumerable<Iec61850ValueSchemaNode> children)
    {
        if (IsControlOperationNode(parentName) || parentSemanticKind.Equals("ControlOperation", StringComparison.OrdinalIgnoreCase))
            return OrderByExplicit(["ctlVal", "origin", "ctlNum", "T", "Test", "Check"], children);

        if (IsOriginNode(parentName) || parentSemanticKind.Equals("Origin", StringComparison.OrdinalIgnoreCase))
            return OrderByExplicit(["orCat", "orIdent"], children);

        if (IsQualityNode(parentName, parentSemanticKind))
            return OrderByExplicit(["Validity", "Quality Details", "Overflow", "OutOfRange", "BadReference", "Oscillatory", "Failure", "OldData", "Inconsistent", "Inaccurate", "Source", "Test", "OperatorBlocked"], children);

        if (IsTimestampNode(parentName, parentSemanticKind))
            return OrderByExplicit(["LeapSecondsKnown", "ClockFailure", "ClockNotSynchronized", "TimeAccuracy"], children);

        return OrderByCdc(cdc, children);
    }

    private static IEnumerable<Iec61850ValueSchemaNode> OrderByExplicit(IReadOnlyList<string> order, IEnumerable<Iec61850ValueSchemaNode> nodes)
        => nodes
            .OrderBy(x => OrderIndex(order, x.Name))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);

    private sealed class MutableSchemaNode
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public string FunctionalConstraint { get; set; } = string.Empty;
        public string Cdc { get; set; } = string.Empty;
        public string BType { get; set; } = string.Empty;
        public string MmsType { get; set; } = string.Empty;
        public string SemanticKind { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public Iec61850BindingConfidence Confidence { get; set; } = Iec61850BindingConfidence.Low;
        public List<MutableSchemaNode> Children { get; } = new();

        public Iec61850ValueSchemaNode ToImmutable()
            => new()
            {
                Name = Name,
                Path = Path,
                Reference = Reference,
                FunctionalConstraint = FunctionalConstraint,
                Cdc = Cdc,
                BType = BType,
                MmsType = MmsType,
                SemanticKind = SemanticKind,
                Source = Source,
                Confidence = Confidence,
                Children = OrderChildNodes(Name, SemanticKind, Cdc, Children.Select(x => x.ToImmutable())).ToArray()
            };
    }
}
