using System.Xml;
using System.Xml.Linq;
using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Scl.Engineering;

public enum SclCanonicalImportStatus
{
    Success,
    InvalidDocument,
    IedNotFound,
    AmbiguousIed,
    AccessPointNotFound,
    AmbiguousAccessPoint,
    MissingServer,
    ConflictingLogicalDeviceDomain,
    ProjectionFailed
}

public sealed class SclCanonicalImportOptions
{
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
}

public sealed class SclCanonicalImportResult
{
    public SclCanonicalImportStatus Status { get; init; }
    public string SelectedIedName { get; init; } = string.Empty;
    public string SelectedAccessPointName { get; init; } = string.Empty;
    public CanonicalIedModel? Model { get; init; }
    public string[] Errors { get; init; } = Array.Empty<string>();
    public string[] Warnings { get; init; } = Array.Empty<string>();
    public bool IsSuccess => Status == SclCanonicalImportStatus.Success && Model is not null;
}

/// <summary>
/// P0 ingress boundary for Open SCL. Selection and MMS-domain identity are resolved before
/// the document is projected into the shared canonical model. The XML tree is temporary
/// parse evidence and is never retained by CanonicalIedModel.
///
/// The existing SclLiveModelProjectionBuilder is deliberately used as a migration bridge
/// for typed DO/DA/template projection in this first P0 slice. Later P0 work can replace
/// that bridge without changing the canonical contract or callers.
/// </summary>
public static class SclCanonicalImporter
{
    public static SclCanonicalImportResult Load(
        string filePath,
        SclCanonicalImportOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Fail(SclCanonicalImportStatus.InvalidDocument, "SCL file path is empty.");

        try
        {
            using var stream = File.OpenRead(filePath);
            var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            options ??= new SclCanonicalImportOptions();
            var sourceName = string.IsNullOrWhiteSpace(options.SourceName)
                ? Path.GetFileName(filePath)
                : options.SourceName;
            return Import(document, new SclCanonicalImportOptions
            {
                IedName = options.IedName,
                AccessPointName = options.AccessPointName,
                SourceName = sourceName
            });
        }
        catch (XmlException ex)
        {
            return Fail(SclCanonicalImportStatus.InvalidDocument, $"SCL XML is malformed: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Fail(SclCanonicalImportStatus.InvalidDocument, $"SCL file could not be read: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Fail(SclCanonicalImportStatus.InvalidDocument, $"SCL file could not be read: {ex.Message}");
        }
    }

    public static SclCanonicalImportResult Import(
        XDocument document,
        SclCanonicalImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new SclCanonicalImportOptions();

        var root = document.Root;
        if (root is null || !Is(root, "SCL"))
            return Fail(SclCanonicalImportStatus.InvalidDocument, "The selected document is not an IEC 61850 SCL document.");

        var ieds = root.Elements().Where(element => Is(element, "IED")).ToArray();
        if (ieds.Length == 0)
            return Fail(SclCanonicalImportStatus.IedNotFound, "SCL does not contain an IED element.");

        var selectedIed = SelectByName(ieds, "name", options.IedName, out var iedSelectionError, out var iedAmbiguous);
        if (selectedIed is null)
        {
            return Fail(
                iedAmbiguous ? SclCanonicalImportStatus.AmbiguousIed : SclCanonicalImportStatus.IedNotFound,
                iedSelectionError);
        }

        var iedName = Attr(selectedIed, "name");
        var accessPoints = selectedIed.Elements().Where(element => Is(element, "AccessPoint")).ToArray();
        var selectedAccessPoint = SelectByName(accessPoints, "name", options.AccessPointName, out var apSelectionError, out var apAmbiguous);
        if (selectedAccessPoint is null)
        {
            return Fail(
                apAmbiguous ? SclCanonicalImportStatus.AmbiguousAccessPoint : SclCanonicalImportStatus.AccessPointNotFound,
                apSelectionError,
                iedName);
        }

        var accessPointName = Attr(selectedAccessPoint, "name");
        var server = selectedAccessPoint.Elements().FirstOrDefault(element => Is(element, "Server"));
        if (server is null)
        {
            return Fail(
                SclCanonicalImportStatus.MissingServer,
                $"Selected AccessPoint '{iedName}/{accessPointName}' does not contain a Server.",
                iedName,
                accessPointName);
        }

        var aliasResult = BuildDomainAliases(server, iedName);
        if (aliasResult.Error.Length > 0)
        {
            return Fail(
                SclCanonicalImportStatus.ConflictingLogicalDeviceDomain,
                aliasResult.Error,
                iedName,
                accessPointName);
        }

        try
        {
            var scoped = ScopeDocument(document, iedName, accessPointName);
            var projection = SclLiveModelProjectionBuilder.Build(scoped, options.SourceName);
            var originalTypeAliases = root.Descendants()
                .Where(element => Is(element, "LNodeType") || Is(element, "DOType") || Is(element, "DAType") || Is(element, "EnumType"))
                .Select(element => Attr(element, "id"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var model = CanonicalLiveModelAdapter.FromSclProjection(
                projection,
                options.SourceName,
                DetectSourceEdition(root),
                iedName,
                accessPointName,
                aliasResult.Aliases,
                originalTypeAliases);

            var communication = ResolveCommunication(root, iedName, accessPointName);
            model = WithSclCommunication(model, communication, accessPointName);

            return new SclCanonicalImportResult
            {
                Status = SclCanonicalImportStatus.Success,
                SelectedIedName = iedName,
                SelectedAccessPointName = accessPointName,
                Model = model,
                Warnings = communication.Warnings.ToArray()
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return Fail(
                SclCanonicalImportStatus.ProjectionFailed,
                $"SCL semantic projection failed: {ex.GetType().Name}: {ex.Message}",
                iedName,
                accessPointName);
        }
    }

    private static XElement? SelectByName(
        IReadOnlyList<XElement> candidates,
        string attributeName,
        string requested,
        out string error,
        out bool ambiguous)
    {
        error = string.Empty;
        ambiguous = false;

        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = candidates.FirstOrDefault(candidate =>
                string.Equals(Attr(candidate, attributeName), requested.Trim(), StringComparison.Ordinal));
            if (match is not null)
                return match;

            error = $"Requested '{requested.Trim()}' was not found. Available: {string.Join(", ", candidates.Select(candidate => Attr(candidate, attributeName)).Where(name => name.Length > 0))}.";
            return null;
        }

        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count == 0)
        {
            error = "No matching element is available.";
            return null;
        }

        ambiguous = true;
        error = $"Selection is ambiguous. Specify one of: {string.Join(", ", candidates.Select(candidate => Attr(candidate, attributeName)).Where(name => name.Length > 0))}.";
        return null;
    }

    private static (IReadOnlyDictionary<string, string> Aliases, string Error) BuildDomainAliases(
        XElement server,
        string iedName)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var exactDomains = new HashSet<string>(StringComparer.Ordinal);

        foreach (var lDevice in server.Elements().Where(element => Is(element, "LDevice")))
        {
            var inst = Attr(lDevice, "inst");
            if (inst.Length == 0)
                continue;

            var legacy = iedName + inst;
            var ldName = Attr(lDevice, "ldName");
            var exact = ldName.Length > 0 ? ldName : legacy;

            if (aliases.TryGetValue(legacy, out var previous) && !string.Equals(previous, exact, StringComparison.Ordinal))
                return (aliases, $"LDevice inst '{inst}' maps to conflicting MMS domains '{previous}' and '{exact}'.");

            if (!exactDomains.Add(exact) && !aliases.ContainsKey(legacy))
                return (aliases, $"More than one selected LDevice resolves to MMS domain '{exact}'.");

            aliases[legacy] = exact;
        }

        return (aliases, string.Empty);
    }

    private static XDocument ScopeDocument(XDocument source, string iedName, string accessPointName)
    {
        var scoped = new XDocument(source);
        var root = scoped.Root!;

        foreach (var ied in root.Elements().Where(element => Is(element, "IED")).ToArray())
        {
            if (!string.Equals(Attr(ied, "name"), iedName, StringComparison.Ordinal))
            {
                ied.Remove();
                continue;
            }

            foreach (var accessPoint in ied.Elements().Where(element => Is(element, "AccessPoint")).ToArray())
            {
                if (!string.Equals(Attr(accessPoint, "name"), accessPointName, StringComparison.Ordinal))
                    accessPoint.Remove();
            }
        }

        return scoped;
    }

    private static CommunicationResolution ResolveCommunication(
        XElement root,
        string iedName,
        string accessPointName)
    {
        var connected = root.Descendants()
            .Where(element => Is(element, "ConnectedAP") &&
                string.Equals(Attr(element, "iedName"), iedName, StringComparison.Ordinal) &&
                string.Equals(Attr(element, "apName"), accessPointName, StringComparison.Ordinal))
            .ToArray();

        var ipValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var portValues = new HashSet<int>();
        foreach (var connectedAp in connected)
        {
            foreach (var parameter in connectedAp.Descendants().Where(element => Is(element, "P")))
            {
                var type = Attr(parameter, "type");
                var value = parameter.Value.Trim();
                if (string.Equals(type, "IP", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    ipValues.Add(value);
                else if (string.Equals(type, "MMS-Port", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var port) &&
                         port is >= 1 and <= 65535)
                    portValues.Add(port);
            }
        }

        var warnings = new List<string>();
        string? host = null;
        int? portValue = null;
        if (ipValues.Count == 1)
            host = ipValues.Single();
        else if (ipValues.Count > 1)
            warnings.Add($"ConnectedAP '{iedName}/{accessPointName}' declares conflicting IP values; canonical host remains Unknown.");

        if (portValues.Count == 1)
            portValue = portValues.Single();
        else if (portValues.Count > 1)
            warnings.Add($"ConnectedAP '{iedName}/{accessPointName}' declares conflicting MMS-Port values; canonical port remains Unknown.");

        return new CommunicationResolution(host, portValue, warnings);
    }

    private static CanonicalIedModel WithSclCommunication(
        CanonicalIedModel model,
        CommunicationResolution communication,
        string accessPointName)
    {
        var extraDiagnostics = communication.Warnings.Select(message => new CanonicalDiagnostic
        {
            Code = "SCL.COMMUNICATION_CONFLICT",
            Reference = $"{model.Identity.Name}/{accessPointName}",
            Message = message
        });

        return new CanonicalIedModel
        {
            SchemaVersion = model.SchemaVersion,
            GeneratedAtUtc = model.GeneratedAtUtc,
            Identity = model.Identity,
            Communication = new CanonicalCommunicationContext
            {
                Host = communication.Host is null
                    ? CanonicalFact<string>.Unknown(CanonicalEvidenceSource.SclDeclared)
                    : CanonicalFact<string>.Known(communication.Host, CanonicalEvidenceSource.SclDeclared),
                Port = communication.Port.HasValue
                    ? CanonicalFact<int>.Known(communication.Port.Value, CanonicalEvidenceSource.SclDeclared)
                    : CanonicalFact<int>.Unknown(CanonicalEvidenceSource.SclDeclared),
                AccessPointName = CanonicalFact<string>.Known(accessPointName, CanonicalEvidenceSource.SclDeclared)
            },
            Source = model.Source,
            Strings = model.Strings,
            AccessPoints = model.AccessPoints,
            LogicalDevices = model.LogicalDevices,
            LogicalNodes = model.LogicalNodes,
            DataObjects = model.DataObjects,
            Signals = model.Signals,
            DataSets = model.DataSets,
            ReportControls = model.ReportControls,
            Diagnostics = model.Diagnostics.Concat(extraDiagnostics).ToArray()
        };
    }

    private static string DetectSourceEdition(XElement root)
    {
        var version = Attr(root, "version");
        var revision = Attr(root, "revision");
        if (version.Length > 0)
            return revision.Length > 0 ? $"SCL {version}/{revision}" : $"SCL {version}";

        return "SCL legacy/edition-1-profile";
    }

    private static SclCanonicalImportResult Fail(
        SclCanonicalImportStatus status,
        string error,
        string iedName = "",
        string accessPointName = "")
        => new()
        {
            Status = status,
            SelectedIedName = iedName,
            SelectedAccessPointName = accessPointName,
            Errors = [error]
        };

    private static bool Is(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string Attr(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))?
            .Value?.Trim() ?? string.Empty;

    private sealed record CommunicationResolution(string? Host, int? Port, IReadOnlyList<string> Warnings);
}
