using System.Xml;
using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Scl;

public sealed class SclInitialFcReadDesign
{
    public SclMmsDomainInventory DomainInventory { get; init; } = new();
    public LiveIedModelDiscoveryDocument Model { get; init; } = new();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool IsSuccess => Errors.Count == 0 && DomainInventory.IsSuccess && Model.LogicalDevices.Count > 0;
}

/// <summary>
/// Builds the trusted-SCL shape used by bounded FC-root projection for exactly one
/// IED/AccessPoint/Server. The existing offline SCL projector supplies ordered
/// LN/DO/DA, DataSet and ReportControl engineering shape; this adapter scopes it and
/// replaces legacy IED+inst domains with the exact SCL MMS domain rule
/// (LDevice@ldName when present, otherwise IED@name+inst).
/// </summary>
public static class SclInitialFcReadDesignBuilder
{
    public static SclInitialFcReadDesign Read(string xml, string iedName, string accessPointName)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return Fail(iedName, accessPointName, "SCL XML is empty.");

        try
        {
            return Read(XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo), iedName, accessPointName);
        }
        catch (XmlException ex)
        {
            return Fail(iedName, accessPointName, $"SCL XML is malformed: {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return Fail(iedName, accessPointName, $"SCL initial FC-read design could not be projected: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static SclInitialFcReadDesign Read(XDocument document, string iedName, string accessPointName)
    {
        ArgumentNullException.ThrowIfNull(document);
        var inventory = SclMmsDomainInventoryReader.Read(document, iedName, accessPointName);
        if (!inventory.IsSuccess)
        {
            return new SclInitialFcReadDesign
            {
                DomainInventory = inventory,
                Errors = inventory.Errors.ToArray(),
                Warnings = inventory.Warnings.ToArray()
            };
        }

        var root = document.Root!;
        var selectedIed = root.Elements()
            .Where(element => Is(element, "IED"))
            .First(element => string.Equals(Attr(element, "name"), iedName, StringComparison.Ordinal));
        var selectedAp = selectedIed.Elements()
            .Where(element => Is(element, "AccessPoint"))
            .First(element => string.Equals(Attr(element, "name"), accessPointName, StringComparison.Ordinal));
        var server = selectedAp.Elements().First(element => Is(element, "Server"));

        var domainByInst = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var lDevice in server.Elements().Where(element => Is(element, "LDevice")))
        {
            var inst = Attr(lDevice, "inst");
            var explicitLdName = Attr(lDevice, "ldName");
            if (string.IsNullOrWhiteSpace(inst))
                continue;

            var exactDomain = string.IsNullOrWhiteSpace(explicitLdName)
                ? iedName + inst
                : explicitLdName;
            if (domainByInst.TryGetValue(inst, out var existingDomain))
            {
                if (!string.Equals(existingDomain, exactDomain, StringComparison.Ordinal))
                {
                    return new SclInitialFcReadDesign
                    {
                        DomainInventory = inventory,
                        Errors = new[]
                        {
                            $"Selected SCL Server declares LDevice inst '{inst}' more than once with conflicting MMS domains '{existingDomain}' and '{exactDomain}'."
                        },
                        Warnings = inventory.Warnings.ToArray()
                    };
                }

                continue;
            }

            domainByInst.Add(inst, exactDomain);
        }

        var scopedDocument = new XDocument(document);
        var scopedRoot = scopedDocument.Root!;
        foreach (var otherIed in scopedRoot.Elements().Where(element => Is(element, "IED")).ToArray())
        {
            if (!string.Equals(Attr(otherIed, "name"), iedName, StringComparison.Ordinal))
            {
                otherIed.Remove();
                continue;
            }

            foreach (var otherAp in otherIed.Elements().Where(element => Is(element, "AccessPoint")).ToArray())
            {
                if (!string.Equals(Attr(otherAp, "name"), accessPointName, StringComparison.Ordinal))
                    otherAp.Remove();
            }
        }

        var projected = SclLiveModelProjectionBuilder.Build(scopedDocument, "SCL");
        var logicalDevices = new List<LiveIedLogicalDeviceModel>();
        var warnings = new List<string>(inventory.Warnings);
        var domainMap = domainByInst.ToDictionary(
            pair => iedName + pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);

        foreach (var mapping in domainByInst)
        {
            var legacyDomain = iedName + mapping.Key;
            var candidate = projected.LogicalDevices.FirstOrDefault(device =>
                string.Equals(device.Inst, mapping.Key, StringComparison.Ordinal) &&
                string.Equals(device.MmsDomain, legacyDomain, StringComparison.Ordinal));
            if (candidate is null)
            {
                warnings.Add($"SCL type projection did not produce logical device '{legacyDomain}' for selected AccessPoint '{accessPointName}'.");
                continue;
            }

            logicalDevices.Add(RemapLogicalDevice(candidate, mapping.Value));
        }

        if (logicalDevices.Count == 0)
        {
            return new SclInitialFcReadDesign
            {
                DomainInventory = inventory,
                Errors = new[] { "Selected SCL Server has no logical-device type shape usable for FC-root projection." },
                Warnings = warnings
            };
        }

        var dataSets = projected.DataSets
            .Select(dataSet => RemapDataSet(dataSet, domainMap))
            .ToArray();
        var reportControls = projected.ReportControls
            .Select(report => RemapReportControl(report, domainMap))
            .ToArray();

        var logicalNodes = logicalDevices.SelectMany(device => device.LogicalNodes).ToArray();
        var dataObjects = logicalNodes.SelectMany(node => node.DataObjects).ToArray();
        var attributes = dataObjects.SelectMany(dataObject => dataObject.Attributes).ToArray();
        var model = new LiveIedModelDiscoveryDocument
        {
            Source = "SclInitialFcReadProjection",
            Host = "SCL",
            Port = 102,
            IedName = iedName,
            AccessPointName = accessPointName,
            LogicalDevices = logicalDevices,
            DataSets = dataSets,
            ReportControls = reportControls,
            Coverage = new LiveIedModelDiscoveryCoverage
            {
                LogicalDeviceCount = logicalDevices.Count,
                LogicalNodeCount = logicalNodes.Length,
                DataObjectCount = dataObjects.Length,
                DataAttributeCount = attributes.Length,
                ExactFunctionalConstraintCount = attributes.Count(attribute => !string.IsNullOrWhiteSpace(attribute.FunctionalConstraint)),
                DataSetCount = dataSets.Length,
                ReportControlCount = reportControls.Length,
                BufferedReportControlCount = reportControls.Count(report => report.Buffered),
                UnbufferedReportControlCount = reportControls.Count(report => !report.Buffered)
            },
            Warnings = warnings.Select(message => new LiveIedDiscoveryWarning
            {
                Code = "SCL.INITIAL_FC_READ",
                Message = message
            }).ToArray(),
            Summary = $"Scoped SCL initial FC-read design: IED={iedName}, AP={accessPointName}, LD={logicalDevices.Count}, LN={logicalNodes.Length}, DO={dataObjects.Length}, DA={attributes.Length}, DataSet={dataSets.Length}, RCB={reportControls.Length}."
        };

        return new SclInitialFcReadDesign
        {
            DomainInventory = inventory,
            Model = model,
            Warnings = warnings
        };
    }

    private static LiveIedLogicalDeviceModel RemapLogicalDevice(
        LiveIedLogicalDeviceModel source,
        string exactDomain)
        => new()
        {
            MmsDomain = exactDomain,
            Inst = source.Inst,
            LogicalNodes = source.LogicalNodes.Select(node => new LiveIedLogicalNodeModel
            {
                Name = node.Name,
                Prefix = node.Prefix,
                LnClass = node.LnClass,
                LnInst = node.LnInst,
                ProposedLnTypeId = node.ProposedLnTypeId,
                FunctionalConstraintCounts = node.FunctionalConstraintCounts,
                DataObjects = node.DataObjects.Select(dataObject => new LiveIedDataObjectModel
                {
                    Reference = RemapReference(dataObject.Reference, exactDomain),
                    Name = dataObject.Name,
                    ProposedDoTypeId = dataObject.ProposedDoTypeId,
                    InferredCdc = dataObject.InferredCdc,
                    CdcConfidence = dataObject.CdcConfidence,
                    ConfidenceLevel = dataObject.ConfidenceLevel,
                    Evidence = dataObject.Evidence,
                    Attributes = dataObject.Attributes.Select(attribute => new LiveIedDataAttributeModel
                    {
                        ObjectReference = RemapReference(attribute.ObjectReference, exactDomain),
                        AttributePath = attribute.AttributePath,
                        FunctionalConstraint = attribute.FunctionalConstraint,
                        MmsReference = RemapReference(attribute.MmsReference, exactDomain),
                        MmsItemName = attribute.MmsItemName,
                        Source = attribute.Source,
                        SclBType = attribute.SclBType,
                        MmsType = attribute.MmsType,
                        MmsTypeSignature = attribute.MmsTypeSignature,
                        TypeDiscoveryStatus = attribute.TypeDiscoveryStatus,
                        TypeDiscoveryMessage = attribute.TypeDiscoveryMessage,
                        TypeSource = attribute.TypeSource,
                        TypeConfidence = attribute.TypeConfidence,
                        FunctionalConstraintConfidence = attribute.FunctionalConstraintConfidence
                    }).ToArray()
                }).ToArray()
            }).ToArray()
        };

    private static LiveIedDataSetModel RemapDataSet(
        LiveIedDataSetModel source,
        IReadOnlyDictionary<string, string> domainMap)
    {
        var exactDomain = RemapDomain(source.Domain, domainMap);
        return new LiveIedDataSetModel
        {
            Reference = RemapDomainReference(source.Reference, domainMap),
            Domain = exactDomain,
            LogicalNode = source.LogicalNode,
            Name = source.Name,
            IsDeletable = source.IsDeletable,
            MemberCount = source.MemberCount,
            Members = source.Members.Select(member => new LiveIedDataSetMemberModel
            {
                Index = member.Index,
                Reference = RemapDomainReference(member.Reference, domainMap),
                FunctionalConstraint = member.FunctionalConstraint,
                MmsReference = RemapDomainReference(member.MmsReference, domainMap),
                Confidence = member.Confidence
            }).ToArray(),
            UsedByReportControls = source.UsedByReportControls
                .Select(reference => RemapDomainReference(reference, domainMap))
                .ToArray(),
            UsedByGooseControls = source.UsedByGooseControls
                .Select(reference => RemapDomainReference(reference, domainMap))
                .ToArray(),
            UsedBySampledValueControls = source.UsedBySampledValueControls
                .Select(reference => RemapDomainReference(reference, domainMap))
                .ToArray()
        };
    }

    private static LiveIedReportControlModel RemapReportControl(
        LiveIedReportControlModel source,
        IReadOnlyDictionary<string, string> domainMap)
        => new()
        {
            Reference = RemapDomainReference(source.Reference, domainMap),
            Domain = RemapDomain(source.Domain, domainMap),
            LogicalNode = source.LogicalNode,
            Name = source.Name,
            Buffered = source.Buffered,
            Indexed = source.Indexed,
            DataSetReference = RemapDomainReference(source.DataSetReference, domainMap),
            ReportId = source.ReportId,
            ConfRev = source.ConfRev,
            TriggerOptions = source.TriggerOptions,
            OptionalFields = source.OptionalFields,
            BufferTimeMs = source.BufferTimeMs,
            IntegrityPeriodMs = source.IntegrityPeriodMs,
            EnabledState = source.EnabledState,
            ReservationState = source.ReservationState,
            ReservationTimeSeconds = source.ReservationTimeSeconds,
            Status = source.Status
        };

    private static string RemapDomain(string domain, IReadOnlyDictionary<string, string> domainMap)
    {
        var normalized = (domain ?? string.Empty).Trim();
        return domainMap.TryGetValue(normalized, out var exact) ? exact : normalized;
    }

    private static string RemapDomainReference(
        string reference,
        IReadOnlyDictionary<string, string> domainMap)
    {
        var normalized = (reference ?? string.Empty).Trim();
        var slash = normalized.IndexOf('/');
        if (slash <= 0)
            return normalized;

        var domain = normalized[..slash];
        return domainMap.TryGetValue(domain, out var exact)
            ? exact + normalized[slash..]
            : normalized;
    }

    private static string RemapReference(string reference, string exactDomain)
    {
        var normalized = (reference ?? string.Empty).Trim();
        var slash = normalized.IndexOf('/');
        if (slash < 0)
            return normalized;

        return exactDomain + normalized[slash..];
    }

    private static SclInitialFcReadDesign Fail(string iedName, string accessPointName, string error)
        => new()
        {
            DomainInventory = new SclMmsDomainInventory
            {
                IedName = iedName,
                AccessPointName = accessPointName,
                Errors = new[] { error }
            },
            Errors = new[] { error }
        };

    private static bool Is(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string Attr(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))?
            .Value?.Trim() ?? string.Empty;
}
