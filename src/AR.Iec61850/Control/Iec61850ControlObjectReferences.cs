using AR.Iec61850.Mms;

namespace AR.Iec61850.Control;

internal sealed class Iec61850ControlObjectReferences
{
    private static readonly HashSet<string> ServiceLeaves = new(StringComparer.OrdinalIgnoreCase)
    {
        "ctlModel", "ctlVal", "ctlNum", "stSeld", "Oper", "SBO", "SBOw", "Cancel", "origin", "orCat", "orIdent", "T", "Test", "Check"
    };

    private Iec61850ControlObjectReferences(string domain, string logicalNode, IReadOnlyList<string> dataObjectSegments)
    {
        Domain = domain;
        LogicalNode = logicalNode;
        DataObjectSegments = dataObjectSegments;
        DataObjectPath = string.Join('.', dataObjectSegments);
        MmsDataObjectPath = string.Join('$', dataObjectSegments);
        ObjectReference = $"{Domain}/{LogicalNode}.{DataObjectPath}";
    }

    public string Domain { get; }
    public string LogicalNode { get; }
    public IReadOnlyList<string> DataObjectSegments { get; }
    public string DataObjectPath { get; }
    public string MmsDataObjectPath { get; }
    public string ObjectReference { get; }

    public MmsObjectReference CtlModel => Build("CF", "ctlModel");
    public MmsObjectReference SboTimeout => Build("CF", "sboTimeout");
    public MmsObjectReference OperTimeout => Build("CF", "operTimeout");
    public MmsObjectReference Oper => Build("CO", "Oper");
    public MmsObjectReference Sbo => Build("CO", "SBO");
    public MmsObjectReference SboWithValue => Build("CO", "SBOw");
    public MmsObjectReference Cancel => Build("CO", "Cancel");

    public MmsObjectReference Build(string functionalConstraint, params string[] suffix)
    {
        var parts = new[] { LogicalNode, functionalConstraint }
            .Concat(DataObjectSegments)
            .Concat(suffix)
            .ToArray();
        return new MmsObjectReference(Domain, string.Join('$', parts), functionalConstraint);
    }

    public static Iec61850ControlObjectReferences Parse(string objectReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectReference);
        var normalized = objectReference.Trim().Replace('$', '.');
        var slash = normalized.IndexOf('/');
        if (slash <= 0 || slash == normalized.Length - 1)
            throw new ArgumentException("Control object reference must use LD/LN.DO form.", nameof(objectReference));

        var domain = normalized[..slash].Trim();
        var path = normalized[(slash + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (path.Length < 2)
            throw new ArgumentException("Control object reference must identify a Data Object, for example LD0/CSWI1.Pos.", nameof(objectReference));

        if (path.Any(segment => ServiceLeaves.Contains(segment)))
            throw new ArgumentException("Control service leaves are not control objects. Use the controllable Data Object root, for example LD0/CSWI1.Pos.", nameof(objectReference));

        if (path.Any(segment => segment.Contains('/')) || string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Control object reference contains invalid path segments.", nameof(objectReference));

        return new Iec61850ControlObjectReferences(domain, path[0], path.Skip(1).ToArray());
    }

    public bool MatchesOperateReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var normalized = reference.Replace('.', '$').Replace('/', '$').Trim('$');
        var expected = $"{Domain}${LogicalNode}$CO${MmsDataObjectPath}$Oper";
        return normalized.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith($"{LogicalNode}$CO${MmsDataObjectPath}$Oper", StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesReportedReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var normalized = reference.Replace('$', '.').Trim();
        var objectNormalized = ObjectReference.Replace('$', '.');
        return normalized.Equals(objectNormalized, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(objectNormalized + ".", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith($"/{LogicalNode}.{DataObjectPath}", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains($"/{LogicalNode}.CO.{DataObjectPath}.", StringComparison.OrdinalIgnoreCase);
    }
}
