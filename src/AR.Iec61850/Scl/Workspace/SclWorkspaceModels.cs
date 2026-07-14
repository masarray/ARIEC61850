using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Scl.Workspace;

public sealed class SclWorkspaceOpenOptions
{
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public bool IncludeAccessPointsWithoutServer { get; init; } = true;
}

public sealed class SclMmsEndpoint
{
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public string SubNetworkName { get; init; } = string.Empty;
    public string SubNetworkType { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public bool IsValidIpAddress { get; init; }
    public IReadOnlyDictionary<string, string> AddressParameters { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool HasUsableAddress => IsValidIpAddress && Port is > 0 and <= 65535;
    public string IdentityKey => $"{IedName}/{AccessPointName}";
    public string EndpointText => HasUsableAddress ? $"{IpAddress}:{Port}" : "unassigned";
}

public sealed class SclWorkspaceFinding
{
    public string Severity { get; init; } = "Info";
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ObjectReference { get; init; } = string.Empty;
}

public sealed class SclWorkspaceDocument
{
    public string SourceName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string SourceSha256 { get; init; } = string.Empty;
    public SclDocument Document { get; init; } = new();
    public SclEngineeringProfile EngineeringProfile { get; init; } = new();
    public IReadOnlyList<SclMmsEndpoint> MmsEndpoints { get; init; } = Array.Empty<SclMmsEndpoint>();
    public IReadOnlyList<SclIedWorkspace> Ieds { get; init; } = Array.Empty<SclIedWorkspace>();
    public IReadOnlyList<SclWorkspaceFinding> Findings { get; init; } = Array.Empty<SclWorkspaceFinding>();

    public bool HasBlockingFindings
        => Findings.Any(x => string.Equals(x.Severity, "High", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase));
}

public sealed class SclIedWorkspace
{
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string IedType { get; init; } = string.Empty;
    public string ConfigVersion { get; init; } = string.Empty;
    public IReadOnlyList<SclMmsEndpoint> Endpoints { get; init; } = Array.Empty<SclMmsEndpoint>();
    public SclMmsEndpoint? PreferredEndpoint { get; init; }
    public LiveIedModelDiscoveryDocument DesignModel { get; init; } = new();
    public IReadOnlyList<SclDataSet> DataSets { get; init; } = Array.Empty<SclDataSet>();
    public IReadOnlyList<SclReportControl> ReportControls { get; init; } = Array.Empty<SclReportControl>();
    public IReadOnlyList<SclGooseStream> GooseStreams { get; init; } = Array.Empty<SclGooseStream>();
    public IReadOnlyList<SclSampledValuesStream> SampledValuesStreams { get; init; } = Array.Empty<SclSampledValuesStream>();
    public IReadOnlyList<SclWorkspaceFinding> Findings { get; init; } = Array.Empty<SclWorkspaceFinding>();

    public string WorkspaceKey => string.IsNullOrWhiteSpace(AccessPointName)
        ? IedName
        : $"{IedName}/{AccessPointName}";

    public bool CanBrowseOffline => DesignModel.Coverage.LogicalDeviceCount > 0;
    public bool RequiresEndpointBinding => PreferredEndpoint?.HasUsableAddress != true;
}
