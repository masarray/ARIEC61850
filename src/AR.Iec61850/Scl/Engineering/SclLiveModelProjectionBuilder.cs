using System.Globalization;
using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Scl.Engineering;

public static class SclLiveModelProjectionBuilder
{
    public static LiveIedModelDiscoveryDocument Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("SCL file path is empty.", nameof(filePath));

        using var stream = File.OpenRead(filePath);
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        return Build(document, Path.GetFileName(filePath));
    }

    public static LiveIedModelDiscoveryDocument Build(XDocument document, string sourceName = "")
    {
        var root = document.Root ?? throw new InvalidDataException("SCL document has no root element.");
        if (!Is(root, "SCL"))
            throw new InvalidDataException("The selected file is not an IEC 61850 SCL document.");

        var parserDocument = new SclParser().Parse(document, sourceName);
        var typeIndex = TypeIndex.Build(root);
        var warnings = new List<LiveIedDiscoveryWarning>();
        warnings.AddRange(parserDocument.Warnings.Select(x => new LiveIedDiscoveryWarning
        {
            Code = "SCL.WARNING",
            Message = x
        }));

        var logicalDevices = BuildLogicalDevices(root, typeIndex, warnings).ToArray();
        var dataSets = parserDocument.DataSets.Select(ToLiveDataSet).ToArray();
        var reportControls = parserDocument.ReportControls.Select(ToLiveReportControl).ToArray();
        var goose = parserDocument.GooseStreams.Select(x => ToLiveControlBlock("GSEControl", x.ControlBlockReference, x.LdInst, "LLN0", x.ControlName, x.DataSetReference, x.GoId, x.Address.AppIdText, x.ConfigurationRevision.ToString(CultureInfo.InvariantCulture), $"SCL GOOSE confRev={x.ConfigurationRevision}")).ToArray();
        var sv = parserDocument.SampledValuesStreams.Select(x => ToLiveControlBlock("SampledValueControl", x.ControlBlockReference, x.LdInst, "LLN0", x.ControlName, x.DataSetReference, x.SmvId, x.Address.AppIdText, x.ConfigurationRevision.ToString(CultureInfo.InvariantCulture), $"SCL SV smpRate={x.SampleRate}, nofASDU={x.NoAsdu}")).ToArray();
        var coverage = BuildCoverage(logicalDevices, dataSets, reportControls, goose, sv);
        var iedName = parserDocument.Ieds.FirstOrDefault()?.Name ?? Path.GetFileNameWithoutExtension(sourceName);

        return new LiveIedModelDiscoveryDocument
        {
            Source = "SclOfflineProjection",
            Host = sourceName,
            Port = 102,
            IedName = string.IsNullOrWhiteSpace(iedName) ? "SCL" : iedName,
            AccessPointName = "AP1",
            LogicalDevices = logicalDevices,
            DataSets = dataSets,
            ReportControls = reportControls,
            GooseControlBlocks = goose,
            SampledValueControlBlocks = sv,
            Coverage = coverage,
            Warnings = warnings,
            Summary = $"Offline SCL model: LD={coverage.LogicalDeviceCount}, LN={coverage.LogicalNodeCount}, DO={coverage.DataObjectCount}, DA={coverage.DataAttributeCount}, RCB={coverage.ReportControlCount}, DataSet={coverage.DataSetCount}."
        };
    }

    private static IEnumerable<LiveIedLogicalDeviceModel> BuildLogicalDevices(XElement root, TypeIndex typeIndex, List<LiveIedDiscoveryWarning> warnings)
    {
        foreach (var ied in root.Elements().Where(e => Is(e, "IED")))
        {
            var iedName = Attr(ied, "name");
            foreach (var lDevice in ied.Descendants().Where(e => Is(e, "LDevice")))
            {
                var ldInst = Attr(lDevice, "inst");
                var domain = iedName + ldInst;
                var logicalNodes = new List<LiveIedLogicalNodeModel>();

                foreach (var ln in lDevice.Elements().Where(e => Is(e, "LN0") || Is(e, "LN")))
                {
                    var lnClass = Is(ln, "LN0") ? "LLN0" : Attr(ln, "lnClass");
                    var prefix = Is(ln, "LN0") ? string.Empty : Attr(ln, "prefix");
                    var lnInst = Is(ln, "LN0") ? string.Empty : Attr(ln, "inst");
                    var name = BuildLnName(prefix, lnClass, lnInst);
                    var lnTypeId = Attr(ln, "lnType");
                    var dataObjects = typeIndex.TryGetLNodeType(lnTypeId, out var lNodeType)
                        ? BuildDataObjects(domain, name, lnClass, lNodeType, typeIndex, warnings).ToArray()
                        : Array.Empty<LiveIedDataObjectModel>();

                    logicalNodes.Add(new LiveIedLogicalNodeModel
                    {
                        Name = name,
                        Prefix = prefix,
                        LnClass = lnClass,
                        LnInst = lnInst,
                        ProposedLnTypeId = string.IsNullOrWhiteSpace(lnTypeId) ? $"LN_{lnClass}_{name}" : lnTypeId,
                        FunctionalConstraintCounts = dataObjects
                            .SelectMany(x => x.Attributes)
                            .Where(x => !string.IsNullOrWhiteSpace(x.FunctionalConstraint))
                            .GroupBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase),
                        DataObjects = dataObjects
                    });
                }

                yield return new LiveIedLogicalDeviceModel
                {
                    MmsDomain = domain,
                    Inst = ldInst,
                    LogicalNodes = logicalNodes
                };
            }
        }
    }

    private static IEnumerable<LiveIedDataObjectModel> BuildDataObjects(string domain, string logicalNode, string lnClass, XElement lNodeType, TypeIndex typeIndex, List<LiveIedDiscoveryWarning> warnings)
    {
        foreach (var dataObject in lNodeType.Elements().Where(e => Is(e, "DO")))
        {
            var name = Attr(dataObject, "name");
            var doTypeId = Attr(dataObject, "type");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!typeIndex.TryGetDoType(doTypeId, out var doType))
            {
                warnings.Add(new LiveIedDiscoveryWarning
                {
                    Code = "SCL.DO_TYPE_MISSING",
                    Reference = $"{domain}/{logicalNode}.{name}",
                    Message = $"DOType '{doTypeId}' was not found."
                });
                continue;
            }

            var cdc = Attr(doType, "cdc");
            var reference = $"{domain}/{logicalNode}.{name}";
            var attributes = BuildDataAttributes(reference, name, doType, typeIndex, string.Empty, string.Empty).ToArray();
            var attributePaths = attributes
                .Select(x => x.AttributePath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            var functionalConstraints = attributes
                .Select(x => x.FunctionalConstraint)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            var inferred = string.IsNullOrWhiteSpace(cdc)
                ? CdcInferenceEngine.Infer(lnClass, name, attributePaths, functionalConstraints).Cdc
                : cdc;

            yield return new LiveIedDataObjectModel
            {
                Reference = reference,
                Name = name,
                ProposedDoTypeId = string.IsNullOrWhiteSpace(doTypeId) ? $"DO_{lnClass}_{name}" : doTypeId,
                InferredCdc = inferred,
                CdcConfidence = string.IsNullOrWhiteSpace(cdc) ? 0.60 : 0.95,
                ConfidenceLevel = string.IsNullOrWhiteSpace(cdc) ? LiveIedDiscoveryConfidenceLevel.Medium : LiveIedDiscoveryConfidenceLevel.Exact,
                Evidence = string.IsNullOrWhiteSpace(cdc) ? new[] { "CDC inferred from SCL attributes." } : new[] { $"CDC from SCL DOType={doTypeId}." },
                Attributes = attributes
            };
        }
    }

    private static IEnumerable<LiveIedDataAttributeModel> BuildDataAttributes(string objectReference, string dataObjectName, XElement container, TypeIndex typeIndex, string inheritedFc, string prefix)
    {
        foreach (var child in container.Elements().Where(e => Is(e, "DA") || Is(e, "BDA")))
        {
            var name = Attr(child, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var path = string.IsNullOrWhiteSpace(prefix) ? name : $"{prefix}.{name}";
            var fc = FirstNonEmpty(Attr(child, "fc"), inheritedFc);
            var bType = Attr(child, "bType");
            var typeId = Attr(child, "type");
            var reference = $"{objectReference}.{path}";

            yield return new LiveIedDataAttributeModel
            {
                ObjectReference = reference,
                AttributePath = path,
                FunctionalConstraint = fc,
                MmsReference = reference,
                MmsItemName = reference[(reference.IndexOf('/') + 1)..],
                Source = "SCL.DataTypeTemplates",
                SclBType = bType,
                MmsType = bType,
                MmsTypeSignature = string.IsNullOrWhiteSpace(typeId) ? bType : $"{bType}:{typeId}",
                TypeDiscoveryStatus = "SCL",
                TypeDiscoveryMessage = "Projected from SCL DataTypeTemplates.",
                TypeSource = "SCL",
                TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact,
                FunctionalConstraintConfidence = string.IsNullOrWhiteSpace(fc) ? LiveIedDiscoveryConfidenceLevel.Unknown : LiveIedDiscoveryConfidenceLevel.Exact
            };

            if (string.Equals(bType, "Struct", StringComparison.OrdinalIgnoreCase) && typeIndex.TryGetDaType(typeId, out var daType))
            {
                foreach (var nested in BuildDataAttributes(objectReference, dataObjectName, daType, typeIndex, fc, path))
                    yield return nested;
            }
        }

        foreach (var sdo in container.Elements().Where(e => Is(e, "SDO")))
        {
            var name = Attr(sdo, "name");
            var typeId = Attr(sdo, "type");
            if (string.IsNullOrWhiteSpace(name) || !typeIndex.TryGetDoType(typeId, out var sdoType))
                continue;

            var sdoPrefix = string.IsNullOrWhiteSpace(prefix) ? name : $"{prefix}.{name}";
            foreach (var nested in BuildDataAttributes(objectReference, dataObjectName, sdoType, typeIndex, inheritedFc, sdoPrefix))
                yield return nested;
        }
    }

    private static LiveIedDataSetModel ToLiveDataSet(SclDataSet dataSet)
        => new()
        {
            Reference = dataSet.Reference,
            Domain = dataSet.IedName + dataSet.LdInst,
            LogicalNode = dataSet.LogicalNodePath,
            Name = dataSet.Name,
            MemberCount = dataSet.Entries.Count,
            Members = dataSet.Entries.Select(x =>
            {
                var canonicalReference = BuildCanonicalDataSetMemberReference(dataSet, x);
                return new LiveIedDataSetMemberModel
                {
                    Index = x.Index,
                    Reference = canonicalReference,
                    FunctionalConstraint = x.Fc,
                    MmsReference = canonicalReference,
                    Confidence = x.BType.Length > 0 || x.Cdc.Length > 0 ? LiveIedDiscoveryConfidenceLevel.Exact : LiveIedDiscoveryConfidenceLevel.Medium
                };
            }).ToArray()
        };

    private static string BuildCanonicalDataSetMemberReference(SclDataSet dataSet, SclDataSetEntry entry)
    {
        var iedName = FirstNonEmpty(entry.IedName, dataSet.IedName).Trim();
        var ldInst = FirstNonEmpty(entry.LdInst, dataSet.LdInst).Trim();
        var logicalNode = BuildLnName(entry.Prefix, entry.LnClass, entry.LnInst);
        var doName = (entry.DoName ?? string.Empty).Trim();
        var daName = (entry.DaName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(iedName) ||
            string.IsNullOrWhiteSpace(ldInst) ||
            string.IsNullOrWhiteSpace(logicalNode) ||
            string.IsNullOrWhiteSpace(doName))
        {
            return entry.SignalReference;
        }

        var objectReference = string.Concat(iedName, ldInst, "/", logicalNode, ".", doName);
        return string.IsNullOrWhiteSpace(daName)
            ? objectReference
            : string.Concat(objectReference, ".", daName);
    }

    private static LiveIedReportControlModel ToLiveReportControl(SclReportControl report)
        => new()
        {
            Reference = report.ControlBlockReference,
            Domain = report.IedName + report.LdInst,
            LogicalNode = string.IsNullOrWhiteSpace(report.LogicalNodePath) ? "LLN0" : report.LogicalNodePath,
            Name = report.Name,
            Buffered = report.Buffered,
            DataSetReference = report.DataSetReference,
            ReportId = report.ReportId,
            ConfRev = report.ConfigurationRevision.ToString(CultureInfo.InvariantCulture),
            BufferTimeMs = report.BufferTimeMilliseconds.ToString(CultureInfo.InvariantCulture),
            IntegrityPeriodMs = report.IntegrityPeriodMilliseconds.ToString(CultureInfo.InvariantCulture),
            EnabledState = "offline",
            ReservationState = "offline",
            Status = "SCL offline"
        };

    private static LiveIedControlBlockModel ToLiveControlBlock(string kind, string reference, string ldInst, string logicalNode, string name, string dataSetReference, string controlId, string appId, string confRev, string message)
        => new()
        {
            Kind = kind,
            Reference = reference,
            Domain = reference.Contains('/') ? reference[..reference.IndexOf('/')] : ldInst,
            LogicalNode = logicalNode,
            Name = name,
            DataSetReference = dataSetReference,
            ControlId = controlId,
            AppId = appId,
            ConfRev = confRev,
            Message = message,
            DiscoveryStatus = "SCL offline"
        };

    private static LiveIedModelDiscoveryCoverage BuildCoverage(
        IReadOnlyList<LiveIedLogicalDeviceModel> logicalDevices,
        IReadOnlyList<LiveIedDataSetModel> dataSets,
        IReadOnlyList<LiveIedReportControlModel> reports,
        IReadOnlyList<LiveIedControlBlockModel> goose,
        IReadOnlyList<LiveIedControlBlockModel> sv)
    {
        var logicalNodes = logicalDevices.SelectMany(x => x.LogicalNodes).ToArray();
        var dataObjects = logicalNodes.SelectMany(x => x.DataObjects).ToArray();
        var attributes = dataObjects.SelectMany(x => x.Attributes).ToArray();
        return new LiveIedModelDiscoveryCoverage
        {
            LogicalDeviceCount = logicalDevices.Count,
            LogicalNodeCount = logicalNodes.Length,
            DataObjectCount = dataObjects.Length,
            DataAttributeCount = attributes.Length,
            ExactFunctionalConstraintCount = attributes.Count(x => x.FunctionalConstraintConfidence == LiveIedDiscoveryConfidenceLevel.Exact),
            HighConfidenceCdcCount = dataObjects.Count(x => x.ConfidenceLevel is LiveIedDiscoveryConfidenceLevel.Exact or LiveIedDiscoveryConfidenceLevel.High),
            MediumConfidenceCdcCount = dataObjects.Count(x => x.ConfidenceLevel == LiveIedDiscoveryConfidenceLevel.Medium),
            UnknownCdcCount = dataObjects.Count(x => x.ConfidenceLevel == LiveIedDiscoveryConfidenceLevel.Unknown),
            DataSetCount = dataSets.Count,
            ReportControlCount = reports.Count,
            BufferedReportControlCount = reports.Count(x => x.Buffered),
            UnbufferedReportControlCount = reports.Count(x => !x.Buffered),
            GooseControlBlockCount = goose.Count,
            SampledValueControlBlockCount = sv.Count
        };
    }

    private static string BuildLnName(string prefix, string lnClass, string lnInst)
        => string.Equals(lnClass, "LLN0", StringComparison.OrdinalIgnoreCase)
            ? "LLN0"
            : string.Concat(prefix ?? string.Empty, lnClass ?? string.Empty, lnInst ?? string.Empty);

    private static string FirstNonEmpty(string first, string second)
        => string.IsNullOrWhiteSpace(first) ? second : first;

    private static bool Is(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string Attr(XElement? element, string localName)
        => element?.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, localName, StringComparison.Ordinal))?.Value?.Trim() ?? string.Empty;

    private sealed class TypeIndex
    {
        private readonly Dictionary<string, XElement> _lNodeTypes;
        private readonly Dictionary<string, XElement> _doTypes;
        private readonly Dictionary<string, XElement> _daTypes;

        private TypeIndex(Dictionary<string, XElement> lNodeTypes, Dictionary<string, XElement> doTypes, Dictionary<string, XElement> daTypes)
        {
            _lNodeTypes = lNodeTypes;
            _doTypes = doTypes;
            _daTypes = daTypes;
        }

        public static TypeIndex Build(XElement root)
            => new(
                Index(root.Descendants().Where(e => Is(e, "LNodeType")), "id"),
                Index(root.Descendants().Where(e => Is(e, "DOType")), "id"),
                Index(root.Descendants().Where(e => Is(e, "DAType")), "id"));

        public bool TryGetLNodeType(string id, out XElement element) => _lNodeTypes.TryGetValue(id ?? string.Empty, out element!);
        public bool TryGetDoType(string id, out XElement element) => _doTypes.TryGetValue(id ?? string.Empty, out element!);
        public bool TryGetDaType(string id, out XElement element) => _daTypes.TryGetValue(id ?? string.Empty, out element!);

        private static Dictionary<string, XElement> Index(IEnumerable<XElement> elements, string key)
            => elements
                .Where(e => !string.IsNullOrWhiteSpace(Attr(e, key)))
                .GroupBy(e => Attr(e, key), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }
}
