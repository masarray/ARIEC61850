using AR.Iec61850.Ethernet;

namespace AR.Iec61850.Scl;

public sealed class SclDocument
{
    public string SourceName { get; init; } = string.Empty;
    public string NamespaceUri { get; init; } = string.Empty;
    public string HeaderId { get; init; } = string.Empty;
    public string HeaderVersion { get; init; } = string.Empty;
    public string HeaderRevision { get; init; } = string.Empty;
    public SclEdition Edition { get; init; }
    public IReadOnlyList<SclIed> Ieds { get; init; } = Array.Empty<SclIed>();
    public IReadOnlyList<SclDataSet> DataSets { get; init; } = Array.Empty<SclDataSet>();
    public IReadOnlyList<SclGooseStream> GooseStreams { get; init; } = Array.Empty<SclGooseStream>();
    public IReadOnlyList<SclSampledValuesStream> SampledValuesStreams { get; init; } = Array.Empty<SclSampledValuesStream>();
    public IReadOnlyList<SclReportControl> ReportControls { get; init; } = Array.Empty<SclReportControl>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SclConflict> Conflicts { get; init; } = Array.Empty<SclConflict>();
}

public enum SclEdition
{
    Unknown,
    Edition1,
    Edition2,
    Edition21
}

public sealed class SclIed
{
    public string Name { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string ConfigVersion { get; init; } = string.Empty;
}

public sealed class SclDataSet
{
    public string Key { get; init; } = string.Empty;
    public string IedName { get; init; } = string.Empty;
    public string LdInst { get; init; } = string.Empty;
    public string LogicalNodePath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public IReadOnlyList<SclDataSetEntry> Entries { get; init; } = Array.Empty<SclDataSetEntry>();
}

public sealed class SclDataSetEntry
{
    public int Index { get; init; }
    public string SignalReference { get; init; } = string.Empty;
    public string IedName { get; init; } = string.Empty;
    public string LdInst { get; init; } = string.Empty;
    public string Prefix { get; init; } = string.Empty;
    public string LnClass { get; init; } = string.Empty;
    public string LnInst { get; init; } = string.Empty;
    public string DoName { get; init; } = string.Empty;
    public string DaName { get; init; } = string.Empty;
    public string Fc { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public string BType { get; init; } = string.Empty;
    public string TypeId { get; init; } = string.Empty;
    public string EnumType { get; init; } = string.Empty;
    public bool IsQuality { get; init; }
    public bool IsTimestamp { get; init; }
}

public abstract class SclProcessBusStream
{
    public string Kind { get; init; } = string.Empty;
    public string IedName { get; init; } = string.Empty;
    public string LdInst { get; init; } = string.Empty;
    public string ControlName { get; init; } = string.Empty;
    public string ControlBlockReference { get; init; } = string.Empty;
    public string DataSetName { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public uint ConfigurationRevision { get; init; }
    public SclStreamAddress Address { get; init; } = new();
    public IReadOnlyList<SclDataSetEntry> Entries { get; init; } = Array.Empty<SclDataSetEntry>();
}

public sealed class SclGooseStream : SclProcessBusStream
{
    public string GoId { get; init; } = string.Empty;
    public uint MinTimeMilliseconds { get; init; }
    public uint MaxTimeMilliseconds { get; init; }
}

public sealed class SclSampledValuesStream : SclProcessBusStream
{
    public string SvId { get; init; } = string.Empty;
    public string SmvId { get; init; } = string.Empty;
    public ushort SampleRate { get; init; }
    public string SampleMode { get; init; } = string.Empty;
    public ushort NoAsdu { get; init; } = 1;
}

public sealed class SclReportControl
{
    public string IedName { get; init; } = string.Empty;
    public string LdInst { get; init; } = string.Empty;
    public string LogicalNodePath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public string DataSetName { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string ControlBlockReference { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public bool Indexed { get; init; } = true;
    public uint ConfigurationRevision { get; init; }
    public uint BufferTimeMilliseconds { get; init; }
    public uint IntegrityPeriodMilliseconds { get; init; }
    public IReadOnlyList<SclDataSetEntry> Entries { get; init; } = Array.Empty<SclDataSetEntry>();
}

public sealed class SclStreamAddress
{
    public string AppIdText { get; init; } = string.Empty;
    public ushort? AppId { get; init; }
    public string DestinationMacText { get; init; } = string.Empty;
    public MacAddress? DestinationMac { get; init; }
    public ushort? VlanId { get; init; }
    public byte? VlanPriority { get; init; }

    public VlanTag? ToVlanTag()
    {
        if (!VlanId.HasValue)
            return null;

        return new VlanTag(VlanPriority ?? 0, VlanId.Value);
    }
}

public sealed class SclConflict
{
    public string Kind { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
