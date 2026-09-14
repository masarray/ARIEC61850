namespace AR.Iec61850.Engineering.Canonical;

/// <summary>
/// How a semantic fact entered the canonical model. This is intentionally small because
/// provenance stamps can occur on very large signal inventories.
/// </summary>
public enum CanonicalEvidenceSource : byte
{
    Unknown = 0,
    LiveMms = 1,
    SclDeclared = 2,
    Inferred = 3,
    UserOverride = 4,
    ProfileDefault = 5
}

public enum CanonicalConfidence : byte
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Exact = 4
}

/// <summary>
/// Absence is not false. An older source profile may also be unable to represent a fact.
/// </summary>
public enum CanonicalKnowledgeState : byte
{
    Unknown = 0,
    Known = 1,
    NotRepresentableInSourceProfile = 2
}

public enum CanonicalIngressKind : byte
{
    Unknown = 0,
    LiveMmsDiscovery = 1,
    SclFile = 2
}

public readonly record struct CanonicalProvenance(
    CanonicalEvidenceSource Source,
    CanonicalConfidence Confidence);

public readonly record struct CanonicalFact<T>(
    CanonicalKnowledgeState State,
    T? Value,
    CanonicalProvenance Provenance)
{
    public bool IsKnown => State == CanonicalKnowledgeState.Known;

    public static CanonicalFact<T> Known(
        T value,
        CanonicalEvidenceSource source,
        CanonicalConfidence confidence = CanonicalConfidence.Exact)
        => new(CanonicalKnowledgeState.Known, value, new CanonicalProvenance(source, confidence));

    public static CanonicalFact<T> Unknown(CanonicalEvidenceSource source = CanonicalEvidenceSource.Unknown)
        => new(CanonicalKnowledgeState.Unknown, default, new CanonicalProvenance(source, CanonicalConfidence.Unknown));

    public static CanonicalFact<T> NotRepresentable(CanonicalEvidenceSource source)
        => new(CanonicalKnowledgeState.NotRepresentableInSourceProfile, default, new CanonicalProvenance(source, CanonicalConfidence.Exact));
}

/// <summary>
/// Index into a per-model immutable string table. High-cardinality signal rows store
/// integer symbols rather than duplicate object references and repeated FC/type strings.
/// </summary>
public readonly record struct CanonicalSymbol(int Id)
{
    public static CanonicalSymbol Empty => new(0);
    public bool IsEmpty => Id == 0;
}

public sealed class CanonicalStringTable
{
    private readonly string[] _values;

    internal CanonicalStringTable(string[] values)
        => _values = values;

    public int Count => _values.Length;

    public string Resolve(CanonicalSymbol symbol)
        => symbol.Id >= 0 && symbol.Id < _values.Length ? _values[symbol.Id] : string.Empty;

    public IReadOnlyList<string> Values => _values;
}

public sealed class CanonicalIedIdentity
{
    public string Name { get; init; } = string.Empty;
    public CanonicalProvenance Provenance { get; init; }
    public bool IsAmbiguous { get; init; }
    public string[] CandidateNames { get; init; } = Array.Empty<string>();
    public string[] Evidence { get; init; } = Array.Empty<string>();
}

public readonly record struct CanonicalAccessPointRow(
    int Id,
    CanonicalSymbol Name);

public readonly record struct CanonicalLogicalDeviceRow(
    int Id,
    int AccessPointId,
    CanonicalSymbol Inst,
    CanonicalSymbol MmsDomain);

public readonly record struct CanonicalLogicalNodeRow(
    int Id,
    int LogicalDeviceId,
    CanonicalSymbol Name,
    CanonicalSymbol Prefix,
    CanonicalSymbol LnClass,
    CanonicalSymbol LnInst,
    CanonicalSymbol SourceTypeAlias);

public readonly record struct CanonicalDataObjectRow(
    int Id,
    int LogicalNodeId,
    CanonicalSymbol Name,
    CanonicalSymbol Reference,
    CanonicalSymbol Cdc,
    CanonicalConfidence CdcConfidence,
    CanonicalSymbol SourceTypeAlias);

/// <summary>
/// Compact leaf row. A model with very large signal counts should grow this table rather
/// than allocate one heavyweight mutable object per signal.
/// </summary>
public readonly record struct CanonicalSignalRow(
    int Id,
    int DataObjectId,
    CanonicalSymbol ObjectReference,
    CanonicalSymbol AttributePath,
    CanonicalSymbol FunctionalConstraint,
    CanonicalSymbol BasicType,
    CanonicalSymbol MmsType,
    CanonicalSymbol MmsTypeSignature,
    CanonicalProvenance Provenance);

public sealed class CanonicalDataSet
{
    public string Reference { get; init; } = string.Empty;
    public string MmsDomain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public CanonicalFact<bool> IsDeletable { get; init; } = CanonicalFact<bool>.Unknown();
    public CanonicalDataSetMember[] Members { get; init; } = Array.Empty<CanonicalDataSetMember>();
}

public readonly record struct CanonicalDataSetMember(
    int Index,
    string Reference,
    string FunctionalConstraint,
    CanonicalProvenance Provenance);

public sealed class CanonicalReportControl
{
    public string Reference { get; init; } = string.Empty;
    public string MmsDomain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public string ConfRev { get; init; } = string.Empty;
    public string TriggerOptions { get; init; } = string.Empty;
    public string OptionalFields { get; init; } = string.Empty;
    public string BufferTimeMs { get; init; } = string.Empty;
    public string IntegrityPeriodMs { get; init; } = string.Empty;
    public CanonicalProvenance Provenance { get; init; }
}

public sealed class CanonicalCommunicationContext
{
    public CanonicalFact<string> Host { get; init; } = CanonicalFact<string>.Unknown();
    public CanonicalFact<int> Port { get; init; } = CanonicalFact<int>.Unknown();
    public CanonicalFact<string> AccessPointName { get; init; } = CanonicalFact<string>.Unknown();
}

public sealed class CanonicalSourceEnvelope
{
    public CanonicalIngressKind Ingress { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public string SourceEdition { get; init; } = string.Empty;
    public string[] OriginalTypeAliases { get; init; } = Array.Empty<string>();
}

public sealed class CanonicalDiagnostic
{
    public string Code { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Immutable snapshot boundary shared by live discovery and Open-SCL ingress. XML DOMs,
/// sockets and UI state are deliberately absent. Arrays plus an interned string table keep
/// the hot semantic inventory compact and safe to hand between worker and UI layers.
/// </summary>
public sealed class CanonicalIedModel
{
    public string SchemaVersion { get; init; } = "canonical-ied-model-v1";
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public CanonicalIedIdentity Identity { get; init; } = new();
    public CanonicalCommunicationContext Communication { get; init; } = new();
    public CanonicalSourceEnvelope Source { get; init; } = new();
    public CanonicalStringTable Strings { get; init; } = new([string.Empty]);
    public CanonicalAccessPointRow[] AccessPoints { get; init; } = Array.Empty<CanonicalAccessPointRow>();
    public CanonicalLogicalDeviceRow[] LogicalDevices { get; init; } = Array.Empty<CanonicalLogicalDeviceRow>();
    public CanonicalLogicalNodeRow[] LogicalNodes { get; init; } = Array.Empty<CanonicalLogicalNodeRow>();
    public CanonicalDataObjectRow[] DataObjects { get; init; } = Array.Empty<CanonicalDataObjectRow>();
    public CanonicalSignalRow[] Signals { get; init; } = Array.Empty<CanonicalSignalRow>();
    public CanonicalDataSet[] DataSets { get; init; } = Array.Empty<CanonicalDataSet>();
    public CanonicalReportControl[] ReportControls { get; init; } = Array.Empty<CanonicalReportControl>();
    public CanonicalDiagnostic[] Diagnostics { get; init; } = Array.Empty<CanonicalDiagnostic>();

    public int SignalCount => Signals.Length;
}

internal sealed class CanonicalStringTableBuilder
{
    private readonly List<string> _values = [string.Empty];
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

    public CanonicalStringTableBuilder()
        => _ids[string.Empty] = 0;

    public CanonicalSymbol Intern(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (_ids.TryGetValue(normalized, out var existing))
            return new CanonicalSymbol(existing);

        var id = _values.Count;
        _values.Add(normalized);
        _ids.Add(normalized, id);
        return new CanonicalSymbol(id);
    }

    public CanonicalStringTable Freeze()
        => new(_values.ToArray());
}
