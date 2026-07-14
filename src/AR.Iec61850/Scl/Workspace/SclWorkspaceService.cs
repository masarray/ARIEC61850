using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Scl.Workspace;

public sealed class SclWorkspaceService
{
    private static readonly string[] IpParameterNames =
    {
        "IP", "IPv4", "IPv6", "IP-Address", "IPAddress"
    };

    private static readonly string[] PortParameterNames =
    {
        "MMS-Port", "MMS_PORT", "IP-Port", "TCP-Port", "Port"
    };

    public Task<SclWorkspaceDocument> OpenAsync(
        string filePath,
        SclWorkspaceOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(() => Open(filePath, options, cancellationToken), cancellationToken);
    }

    public SclWorkspaceDocument Open(
        string filePath,
        SclWorkspaceOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(filePath);
        var bytes = File.ReadAllBytes(fullPath);
        cancellationToken.ThrowIfCancellationRequested();
        var document = SclXmlDocumentLoader.Load(fullPath);
        return Build(
            document,
            Path.GetFileName(fullPath),
            fullPath,
            ComputeSha256(bytes),
            options,
            cancellationToken);
    }

    public SclWorkspaceDocument Parse(
        string xml,
        string sourceName = "",
        SclWorkspaceOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        cancellationToken.ThrowIfCancellationRequested();

        var document = SclXmlDocumentLoader.Parse(xml);
        return Build(
            document,
            sourceName,
            sourcePath: string.Empty,
            sourceSha256: ComputeSha256(Encoding.UTF8.GetBytes(xml)),
            options,
            cancellationToken);
    }

    public SclWorkspaceDocument Build(
        XDocument document,
        string sourceName = "",
        string sourcePath = "",
        string sourceSha256 = "",
        SclWorkspaceOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new SclWorkspaceOpenOptions();

        var parsed = new SclParser().Parse(document, sourceName);
        var engineeringProfile = new SclEngineeringProfileBuilder().Build(document, sourceName);
        var endpointResolution = ResolveMmsEndpoints(document);
        var findings = new List<SclWorkspaceFinding>();
        findings.AddRange(engineeringProfile.Findings.Select(ToWorkspaceFinding));
        findings.AddRange(endpointResolution.Findings);

        var descriptors = BuildWorkspaceDescriptors(parsed, engineeringProfile, options).ToArray();
        var workspaces = new List<SclIedWorkspace>(descriptors.Length);

        foreach (var descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matchingEndpoints = endpointResolution.Endpoints
                .Where(x => Same(x.IedName, descriptor.IedName) &&
                            (string.IsNullOrWhiteSpace(descriptor.AccessPointName) ||
                             Same(x.AccessPointName, descriptor.AccessPointName)))
                .OrderByDescending(x => x.HasUsableAddress)
                .ThenBy(x => x.SubNetworkName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.IpAddress, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var preferredEndpoint = matchingEndpoints.FirstOrDefault(x => x.HasUsableAddress);

            var designModel = BuildDesignModel(
                document,
                sourceName,
                descriptor.IedName,
                descriptor.AccessPointName,
                preferredEndpoint);

            var workspaceFindings = findings
                .Where(x => AppliesToWorkspace(x, descriptor.IedName, descriptor.AccessPointName))
                .ToList();

            if (preferredEndpoint is null)
            {
                workspaceFindings.Add(new SclWorkspaceFinding
                {
                    Severity = "Warning",
                    Code = "SCL_MMS_ENDPOINT_UNASSIGNED",
                    ObjectReference = BuildWorkspaceKey(descriptor.IedName, descriptor.AccessPointName),
                    Message = string.IsNullOrWhiteSpace(descriptor.AccessPointName)
                        ? $"IED '{descriptor.IedName}' has no usable MMS IP endpoint. The offline model remains available and an endpoint can be bound later."
                        : $"IED '{descriptor.IedName}' access point '{descriptor.AccessPointName}' has no usable MMS IP endpoint. The offline model remains available and an endpoint can be bound later."
                });
            }

            workspaceFindings.AddRange(designModel.Warnings.Select(x => new SclWorkspaceFinding
            {
                Severity = "Warning",
                Code = x.Code,
                ObjectReference = x.Reference,
                Message = x.Message
            }));

            var ied = parsed.Ieds.FirstOrDefault(x => Same(x.Name, descriptor.IedName)) ?? new SclIed { Name = descriptor.IedName };
            workspaces.Add(new SclIedWorkspace
            {
                IedName = descriptor.IedName,
                AccessPointName = descriptor.AccessPointName,
                Manufacturer = ied.Manufacturer,
                IedType = ied.Type,
                ConfigVersion = ied.ConfigVersion,
                Endpoints = matchingEndpoints,
                PreferredEndpoint = preferredEndpoint,
                DesignModel = designModel,
                DataSets = parsed.DataSets.Where(x => Same(x.IedName, descriptor.IedName)).ToArray(),
                ReportControls = parsed.ReportControls.Where(x => Same(x.IedName, descriptor.IedName)).ToArray(),
                GooseStreams = parsed.GooseStreams.Where(x => Same(x.IedName, descriptor.IedName)).ToArray(),
                SampledValuesStreams = parsed.SampledValuesStreams.Where(x => Same(x.IedName, descriptor.IedName)).ToArray(),
                Findings = workspaceFindings
                    .GroupBy(FindingKey, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .OrderByDescending(x => SeverityRank(x.Severity))
                    .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.ObjectReference, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
        }

        return new SclWorkspaceDocument
        {
            SourceName = sourceName,
            SourcePath = sourcePath,
            SourceSha256 = sourceSha256,
            Document = parsed,
            EngineeringProfile = engineeringProfile,
            MmsEndpoints = endpointResolution.Endpoints,
            Ieds = workspaces,
            Findings = findings
                .GroupBy(FindingKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderByDescending(x => SeverityRank(x.Severity))
                .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ObjectReference, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public SclLiveModelComparisonResult CompareLive(
        SclIedWorkspace workspace,
        LiveIedModelDiscoveryDocument liveModel)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(liveModel);
        return SclLiveModelComparer.Compare(workspace.DesignModel, liveModel);
    }

    private static LiveIedModelDiscoveryDocument BuildDesignModel(
        XDocument document,
        string sourceName,
        string iedName,
        string accessPointName,
        SclMmsEndpoint? endpoint)
    {
        var isolated = new XDocument(document);
        var root = isolated.Root ?? throw new InvalidDataException("SCL document has no root element.");

        foreach (var otherIed in root.Elements().Where(x => Is(x, "IED") && !Same(Attr(x, "name"), iedName)).ToArray())
            otherIed.Remove();

        var selectedIed = root.Elements().FirstOrDefault(x => Is(x, "IED") && Same(Attr(x, "name"), iedName));
        if (selectedIed is null)
            throw new InvalidDataException($"IED '{iedName}' was not found in the SCL document.");

        if (!string.IsNullOrWhiteSpace(accessPointName))
        {
            foreach (var otherAccessPoint in selectedIed.Elements()
                         .Where(x => Is(x, "AccessPoint") && !Same(Attr(x, "name"), accessPointName))
                         .ToArray())
            {
                otherAccessPoint.Remove();
            }
        }

        var projected = SclLiveModelProjectionBuilder.Build(isolated, sourceName);
        return new LiveIedModelDiscoveryDocument
        {
            SchemaVersion = projected.SchemaVersion,
            GeneratedAtUtc = projected.GeneratedAtUtc,
            Source = projected.Source,
            Host = endpoint?.IpAddress ?? string.Empty,
            Port = endpoint?.Port ?? 102,
            IedName = iedName,
            IedIdentity = projected.IedIdentity,
            AccessPointName = accessPointName,
            Summary = projected.Summary,
            Coverage = projected.Coverage,
            LogicalDevices = projected.LogicalDevices,
            FileDirectory = projected.FileDirectory,
            DataSets = projected.DataSets,
            ReportControls = projected.ReportControls,
            GooseControlBlocks = projected.GooseControlBlocks,
            SampledValueControlBlocks = projected.SampledValueControlBlocks,
            SettingGroupControls = projected.SettingGroupControls,
            LogControls = projected.LogControls,
            TypeTemplates = projected.TypeTemplates,
            VariableTypeDiscoveries = projected.VariableTypeDiscoveries,
            Warnings = projected.Warnings
        };
    }

    private static IEnumerable<WorkspaceDescriptor> BuildWorkspaceDescriptors(
        SclDocument parsed,
        SclEngineeringProfile engineeringProfile,
        SclWorkspaceOpenOptions options)
    {
        var requestedIedName = options.IedName.Trim();
        var requestedAccessPointName = options.AccessPointName.Trim();

        foreach (var ied in parsed.Ieds.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(requestedIedName) && !Same(ied.Name, requestedIedName))
                continue;

            var accessPoints = engineeringProfile.AccessPoints
                .Where(x => Same(x.IedName, ied.Name))
                .Where(x => options.IncludeAccessPointsWithoutServer || x.HasServer)
                .Where(x => string.IsNullOrWhiteSpace(requestedAccessPointName) || Same(x.Name, requestedAccessPointName))
                .OrderByDescending(x => x.HasServer)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (accessPoints.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(requestedAccessPointName))
                    yield return new WorkspaceDescriptor(ied.Name, string.Empty);
                continue;
            }

            foreach (var accessPoint in accessPoints)
                yield return new WorkspaceDescriptor(ied.Name, accessPoint.Name);
        }
    }

    private static EndpointResolution ResolveMmsEndpoints(XDocument document)
    {
        var root = document.Root ?? throw new InvalidDataException("SCL document has no root element.");
        var endpoints = new List<SclMmsEndpoint>();
        var findings = new List<SclWorkspaceFinding>();
        var iedAccessPoints = root.Elements()
            .Where(x => Is(x, "IED"))
            .SelectMany(ied => ied.Elements()
                .Where(x => Is(x, "AccessPoint"))
                .Select(ap => BuildWorkspaceKey(Attr(ied, "name"), Attr(ap, "name"))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var communication = root.Elements().FirstOrDefault(x => Is(x, "Communication"));
        if (communication is null)
            return new EndpointResolution(endpoints, findings);

        foreach (var subNetwork in communication.Elements().Where(x => Is(x, "SubNetwork")))
        {
            var subNetworkName = Attr(subNetwork, "name");
            var subNetworkType = Attr(subNetwork, "type");
            foreach (var connectedAp in subNetwork.Elements().Where(x => Is(x, "ConnectedAP")))
            {
                var iedName = Attr(connectedAp, "iedName");
                var accessPointName = Attr(connectedAp, "apName");
                var identity = BuildWorkspaceKey(iedName, accessPointName);
                if (!iedAccessPoints.Contains(identity))
                {
                    findings.Add(new SclWorkspaceFinding
                    {
                        Severity = "Warning",
                        Code = "SCL_CONNECTED_AP_UNRESOLVED",
                        ObjectReference = identity,
                        Message = $"ConnectedAP '{identity}' does not match an IED AccessPoint definition."
                    });
                }

                var address = connectedAp.Elements().FirstOrDefault(x => Is(x, "Address"));
                var parameters = ReadAddressParameters(address);
                var ipText = FindParameter(parameters, IpParameterNames);
                var port = ResolvePort(parameters, identity, findings);
                var isValidIp = IPAddress.TryParse(ipText, out var parsedIp);
                var canonicalIp = isValidIp ? parsedIp!.ToString() : ipText.Trim();

                if (string.IsNullOrWhiteSpace(ipText))
                {
                    findings.Add(new SclWorkspaceFinding
                    {
                        Severity = "Warning",
                        Code = "SCL_MMS_IP_MISSING",
                        ObjectReference = identity,
                        Message = $"ConnectedAP '{identity}' has no direct MMS IP address. Nested GSE/SMV addresses are intentionally not used as MMS endpoints."
                    });
                }
                else if (!isValidIp)
                {
                    findings.Add(new SclWorkspaceFinding
                    {
                        Severity = "High",
                        Code = "SCL_MMS_IP_INVALID",
                        ObjectReference = identity,
                        Message = $"ConnectedAP '{identity}' has invalid MMS IP address '{ipText}'."
                    });
                }

                endpoints.Add(new SclMmsEndpoint
                {
                    IedName = iedName,
                    AccessPointName = accessPointName,
                    SubNetworkName = subNetworkName,
                    SubNetworkType = subNetworkType,
                    IpAddress = canonicalIp,
                    Port = port,
                    IsValidIpAddress = isValidIp,
                    AddressParameters = parameters
                });
            }
        }

        foreach (var group in endpoints
                     .Where(x => x.HasUsableAddress)
                     .GroupBy(x => $"{x.IpAddress}|{x.Port}", StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Select(endpoint => endpoint.IdentityKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            var identities = group.Select(x => x.IdentityKey).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            findings.Add(new SclWorkspaceFinding
            {
                Severity = "High",
                Code = "SCL_MMS_ENDPOINT_CONFLICT",
                ObjectReference = group.Key.Replace('|', ':'),
                Message = $"MMS endpoint {group.First().IpAddress}:{group.First().Port} is assigned to multiple IED access points: {string.Join(", ", identities)}."
            });
        }

        return new EndpointResolution(endpoints, findings);
    }

    private static IReadOnlyDictionary<string, string> ReadAddressParameters(XElement? address)
    {
        if (address is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return address.Elements()
            .Where(x => Is(x, "P"))
            .Select(x => new KeyValuePair<string, string>(Attr(x, "type"), x.Value.Trim()))
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static int ResolvePort(
        IReadOnlyDictionary<string, string> parameters,
        string identity,
        ICollection<SclWorkspaceFinding> findings)
    {
        var portText = FindParameter(parameters, PortParameterNames);
        if (string.IsNullOrWhiteSpace(portText))
            return 102;

        if (int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) &&
            port is > 0 and <= 65535)
        {
            return port;
        }

        findings.Add(new SclWorkspaceFinding
        {
            Severity = "Warning",
            Code = "SCL_MMS_PORT_INVALID",
            ObjectReference = identity,
            Message = $"ConnectedAP '{identity}' has invalid MMS port '{portText}'. TCP port 102 was selected as the safe default."
        });
        return 102;
    }

    private static string FindParameter(
        IReadOnlyDictionary<string, string> parameters,
        IEnumerable<string> candidateNames)
    {
        foreach (var candidate in candidateNames)
        {
            if (parameters.TryGetValue(candidate, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return string.Empty;
    }

    private static bool AppliesToWorkspace(
        SclWorkspaceFinding finding,
        string iedName,
        string accessPointName)
    {
        if (string.IsNullOrWhiteSpace(finding.ObjectReference))
            return true;

        var workspaceKey = BuildWorkspaceKey(iedName, accessPointName);
        return finding.ObjectReference.StartsWith(workspaceKey, StringComparison.OrdinalIgnoreCase) ||
               finding.ObjectReference.StartsWith(iedName, StringComparison.OrdinalIgnoreCase);
    }

    private static SclWorkspaceFinding ToWorkspaceFinding(SclEngineeringFinding finding)
        => new()
        {
            Severity = finding.Severity,
            Code = finding.Code,
            Message = finding.Message,
            ObjectReference = finding.ObjectReference
        };

    private static string FindingKey(SclWorkspaceFinding finding)
        => $"{finding.Severity}|{finding.Code}|{finding.ObjectReference}|{finding.Message}";

    private static int SeverityRank(string severity)
        => severity.ToUpperInvariant() switch
        {
            "ERROR" or "HIGH" => 3,
            "WARNING" or "WARN" => 2,
            _ => 1
        };

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string BuildWorkspaceKey(string iedName, string accessPointName)
        => string.IsNullOrWhiteSpace(accessPointName) ? iedName : $"{iedName}/{accessPointName}";

    private static bool Same(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Is(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string Attr(XElement? element, string localName)
        => element?.Attributes().FirstOrDefault(x => string.Equals(x.Name.LocalName, localName, StringComparison.Ordinal))?.Value?.Trim() ?? string.Empty;

    private sealed record WorkspaceDescriptor(string IedName, string AccessPointName);

    private sealed record EndpointResolution(
        IReadOnlyList<SclMmsEndpoint> Endpoints,
        IReadOnlyList<SclWorkspaceFinding> Findings);
}
