namespace AR.Iec61850.Mms;

public sealed class MmsDiscoverySnapshot
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DomainVariables { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<string>> DomainVariableLists { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public int DomainCount => DomainVariables.Count;
    public int RawVariableCount => DomainVariables.Values.Sum(x => x.Count);
    public int DataSetCount => DomainVariableLists.Values.Sum(x => x.Count);
}
