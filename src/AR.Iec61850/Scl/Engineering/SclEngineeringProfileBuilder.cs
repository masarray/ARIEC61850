using System.Xml.Linq;

namespace AR.Iec61850.Scl.Engineering;

public sealed class SclEngineeringProfileBuilder
{
    public SclEngineeringProfile Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("SCL file path is empty.", nameof(filePath));

        using var stream = File.OpenRead(filePath);
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        return Build(document, Path.GetFileName(filePath));
    }

    public SclEngineeringProfile Parse(string xml, string sourceName = "")
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("SCL XML is empty.", nameof(xml));

        return Build(XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo), sourceName);
    }

    public SclEngineeringProfile Build(XDocument xdoc, string sourceName = "")
    {
        var scl = new SclParser().Parse(xdoc, sourceName);
        var root = xdoc.Root ?? throw new InvalidDataException("SCL document has no root element.");

        var findings = new List<SclEngineeringFinding>();
        findings.AddRange(scl.Warnings.Select(w => Finding("Warning", "SCL_STATIC_WARNING", w)));
        findings.AddRange(scl.Conflicts.Select(c => Finding("High", $"SCL_CONFLICT_{NormalizeCode(c.Kind)}", c.Description, c.Key)));

        var accessPoints = ParseAccessPoints(root).ToList();
        var logicalDevices = ParseLogicalDevices(root).ToList();
        var logicalNodes = ParseLogicalNodes(root).ToList();
        var externalReferences = ParseExternalReferences(root).ToList();
        var services = ParseServiceDeclarations(root);

        var ieds = scl.Ieds.Select(i => new SclEngineeringIed
        {
            Name = i.Name,
            Manufacturer = i.Manufacturer,
            Type = i.Type,
            ConfigVersion = i.ConfigVersion,
            AccessPointCount = accessPoints.Count(ap => Same(ap.IedName, i.Name)),
            LogicalDeviceCount = logicalDevices.Count(ld => Same(ld.IedName, i.Name))
        }).ToList();

        AddStaticFindings(scl, accessPoints, logicalDevices, logicalNodes, externalReferences, findings);

        var capabilities = new SclEngineeringCapabilityMatrix
        {
            HasServerModel = accessPoints.Any(ap => ap.HasServer) || logicalDevices.Count > 0,
            HasDataSets = scl.DataSets.Count > 0,
            HasReports = scl.ReportControls.Count > 0,
            HasBufferedReports = scl.ReportControls.Any(r => r.Buffered),
            HasUnbufferedReports = scl.ReportControls.Any(r => !r.Buffered),
            HasGoose = scl.GooseStreams.Count > 0,
            HasSampledValues = scl.SampledValuesStreams.Count > 0,
            HasExternalReferences = externalReferences.Count > 0,
            HasControlObjects = logicalNodes.Any(ln => Same(ln.LnClass, "XCBR") || Same(ln.LnClass, "CSWI") || Same(ln.LnClass, "CILO") || Same(ln.LnClass, "ATCC")),
            HasSettingGroups = root.Descendants().Any(e => Is(e, "SettingControl")),
            FileServiceDeclared = services.Contains("FileHandling"),
            LogServiceDeclared = services.Contains("Log"),
            GooseServiceDeclared = services.Contains("GOOSE") || services.Contains("GSE") || scl.GooseStreams.Count > 0,
            SampledValuesServiceDeclared = services.Contains("SMVsc") || services.Contains("SMV") || scl.SampledValuesStreams.Count > 0,
            ReportServiceDeclared = services.Contains("ReportSettings") || services.Contains("Report") || scl.ReportControls.Count > 0
        };

        return new SclEngineeringProfile
        {
            SourceName = sourceName,
            Edition = scl.Edition,
            HeaderId = scl.HeaderId,
            HeaderVersion = scl.HeaderVersion,
            HeaderRevision = scl.HeaderRevision,
            Ieds = ieds,
            AccessPoints = accessPoints,
            LogicalDevices = logicalDevices,
            LogicalNodes = logicalNodes,
            ExternalReferences = externalReferences,
            Capabilities = capabilities,
            ProcessBus = new SclEngineeringStreamSummary
            {
                DataSetCount = scl.DataSets.Count,
                GooseStreams = scl.GooseStreams,
                SampledValuesStreams = scl.SampledValuesStreams,
                ReportControls = scl.ReportControls
            },
            Findings = findings
                .OrderByDescending(f => SeverityRank(f.Severity))
                .ThenBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.ObjectReference, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static IEnumerable<SclEngineeringAccessPoint> ParseAccessPoints(XElement root)
    {
        foreach (var ied in root.Elements().Where(e => Is(e, "IED")))
        {
            var iedName = Attr(ied, "name");
            foreach (var ap in ied.Elements().Where(e => Is(e, "AccessPoint")))
            {
                var server = ap.Elements().FirstOrDefault(e => Is(e, "Server"));
                yield return new SclEngineeringAccessPoint
                {
                    IedName = iedName,
                    Name = Attr(ap, "name"),
                    HasServer = server is not null,
                    Router = Attr(ap, "router"),
                    LogicalDeviceCount = server?.Elements().Count(e => Is(e, "LDevice")) ?? 0
                };
            }
        }
    }

    private static IEnumerable<SclEngineeringLogicalDevice> ParseLogicalDevices(XElement root)
    {
        foreach (var ied in root.Elements().Where(e => Is(e, "IED")))
        {
            var iedName = Attr(ied, "name");
            foreach (var ap in ied.Elements().Where(e => Is(e, "AccessPoint")))
            {
                var accessPointName = Attr(ap, "name");
                foreach (var lDevice in ap.Descendants().Where(e => Is(e, "LDevice")))
                {
                    var lnElements = lDevice.Elements().Where(e => Is(e, "LN0") || Is(e, "LN")).ToList();
                    yield return new SclEngineeringLogicalDevice
                    {
                        IedName = iedName,
                        AccessPointName = accessPointName,
                        Inst = Attr(lDevice, "inst"),
                        Description = Attr(lDevice, "desc"),
                        LogicalNodeCount = lnElements.Count,
                        DataSetCount = lnElements.Sum(ln => ln.Elements().Count(e => Is(e, "DataSet"))),
                        ReportControlCount = lnElements.Sum(ln => ln.Elements().Count(e => Is(e, "ReportControl"))),
                        GooseControlCount = lnElements.Sum(ln => ln.Elements().Count(e => Is(e, "GSEControl"))),
                        SampledValueControlCount = lnElements.Sum(ln => ln.Elements().Count(e => Is(e, "SampledValueControl")))
                    };
                }
            }
        }
    }

    private static IEnumerable<SclEngineeringLogicalNode> ParseLogicalNodes(XElement root)
    {
        foreach (var ied in root.Elements().Where(e => Is(e, "IED")))
        {
            var iedName = Attr(ied, "name");
            foreach (var lDevice in ied.Descendants().Where(e => Is(e, "LDevice")))
            {
                var ldInst = Attr(lDevice, "inst");
                foreach (var ln in lDevice.Elements().Where(e => Is(e, "LN0") || Is(e, "LN")))
                {
                    var lnClass = Is(ln, "LN0") ? "LLN0" : Attr(ln, "lnClass");
                    var prefix = Is(ln, "LN0") ? string.Empty : Attr(ln, "prefix");
                    var lnInst = Is(ln, "LN0") ? string.Empty : Attr(ln, "inst");
                    var logicalNode = Is(ln, "LN0") ? "LLN0" : $"{prefix}{lnClass}{lnInst}";
                    yield return new SclEngineeringLogicalNode
                    {
                        IedName = iedName,
                        LogicalDeviceInst = ldInst,
                        Reference = $"{iedName}{ldInst}/{logicalNode}",
                        Prefix = prefix,
                        LnClass = lnClass,
                        LnInst = lnInst,
                        LnType = Attr(ln, "lnType"),
                        DataObjectCount = ln.Elements().Count(e => Is(e, "DOI")),
                        DataSetCount = ln.Elements().Count(e => Is(e, "DataSet")),
                        ReportControlCount = ln.Elements().Count(e => Is(e, "ReportControl")),
                        GooseControlCount = ln.Elements().Count(e => Is(e, "GSEControl")),
                        SampledValueControlCount = ln.Elements().Count(e => Is(e, "SampledValueControl")),
                        InputReferenceCount = ln.Descendants().Count(e => Is(e, "ExtRef"))
                    };
                }
            }
        }
    }

    private static IEnumerable<SclEngineeringExternalReference> ParseExternalReferences(XElement root)
    {
        foreach (var ied in root.Elements().Where(e => Is(e, "IED")))
        {
            var subscriberIed = Attr(ied, "name");
            foreach (var lDevice in ied.Descendants().Where(e => Is(e, "LDevice")))
            {
                var subscriberLd = Attr(lDevice, "inst");
                foreach (var ln in lDevice.Elements().Where(e => Is(e, "LN0") || Is(e, "LN")))
                {
                    var subscriberLn = Is(ln, "LN0") ? "LLN0" : $"{Attr(ln, "prefix")}{Attr(ln, "lnClass")}{Attr(ln, "inst")}";
                    foreach (var extRef in ln.Descendants().Where(e => Is(e, "ExtRef")))
                    {
                        var sourceIed = Attr(extRef, "iedName");
                        var sourceLd = Attr(extRef, "ldInst");
                        var prefix = Attr(extRef, "prefix");
                        var lnClass = Attr(extRef, "lnClass");
                        var lnInst = Attr(extRef, "lnInst");
                        var doName = Attr(extRef, "doName");
                        var daName = Attr(extRef, "daName");
                        yield return new SclEngineeringExternalReference
                        {
                            SubscriberIedName = subscriberIed,
                            SubscriberLdInst = subscriberLd,
                            SubscriberLogicalNode = subscriberLn,
                            SourceIedName = sourceIed,
                            SourceLdInst = sourceLd,
                            SourcePrefix = prefix,
                            SourceLnClass = lnClass,
                            SourceLnInst = lnInst,
                            DoName = doName,
                            DaName = daName,
                            ServiceType = Attr(extRef, "serviceType"),
                            SourceControlBlockName = Attr(extRef, "srcCBName"),
                            SourceSignalReference = BuildSourceSignalReference(sourceIed, sourceLd, prefix, lnClass, lnInst, doName, daName)
                        };
                    }
                }
            }
        }
    }

    private static HashSet<string> ParseServiceDeclarations(XElement root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in root.Descendants().Where(e => Is(e, "Services")).Descendants())
            result.Add(service.Name.LocalName);
        return result;
    }

    private static void AddStaticFindings(
        SclDocument document,
        IReadOnlyList<SclEngineeringAccessPoint> accessPoints,
        IReadOnlyList<SclEngineeringLogicalDevice> logicalDevices,
        IReadOnlyList<SclEngineeringLogicalNode> logicalNodes,
        IReadOnlyList<SclEngineeringExternalReference> externalReferences,
        ICollection<SclEngineeringFinding> findings)
    {
        if (accessPoints.Count == 0)
            findings.Add(Finding("Warning", "SCL_NO_ACCESS_POINT", "No AccessPoint element was found. Online engineering binding will be limited."));

        if (logicalDevices.Count == 0)
            findings.Add(Finding("High", "SCL_NO_SERVER_MODEL", "No Server/LDevice model was found. The file cannot seed MMS model discovery or simulator model generation."));

        foreach (var ap in accessPoints.Where(a => !a.HasServer))
            findings.Add(Finding("Warning", "SCL_ACCESS_POINT_WITHOUT_SERVER", $"AccessPoint {ap.IedName}/{ap.Name} has no Server element.", $"{ap.IedName}/{ap.Name}"));

        foreach (var ln in logicalNodes.Where(l => string.IsNullOrWhiteSpace(l.LnType)))
            findings.Add(Finding("Warning", "SCL_LN_TYPE_MISSING", $"Logical node {ln.Reference} has no lnType binding.", ln.Reference));

        foreach (var goose in document.GooseStreams)
        {
            if (!goose.Address.AppId.HasValue || string.IsNullOrWhiteSpace(goose.Address.DestinationMacText))
                findings.Add(Finding("High", "SCL_GOOSE_ADDRESS_INCOMPLETE", $"GOOSE {goose.ControlBlockReference} is missing APPID or multicast MAC address.", goose.ControlBlockReference));
            if (goose.Entries.Count == 0)
                findings.Add(Finding("High", "SCL_GOOSE_DATASET_EMPTY", $"GOOSE {goose.ControlBlockReference} has no resolved DataSet members.", goose.ControlBlockReference));
            if (goose.ConfigurationRevision == 0)
                findings.Add(Finding("Warning", "SCL_GOOSE_CONFREV_ZERO", $"GOOSE {goose.ControlBlockReference} has confRev=0.", goose.ControlBlockReference));
        }

        foreach (var sv in document.SampledValuesStreams)
        {
            if (!sv.Address.AppId.HasValue || string.IsNullOrWhiteSpace(sv.Address.DestinationMacText))
                findings.Add(Finding("High", "SCL_SV_ADDRESS_INCOMPLETE", $"SV {sv.ControlBlockReference} is missing APPID or multicast MAC address.", sv.ControlBlockReference));
            if (sv.Entries.Count == 0)
                findings.Add(Finding("High", "SCL_SV_DATASET_EMPTY", $"SV {sv.ControlBlockReference} has no resolved DataSet members.", sv.ControlBlockReference));
            if (sv.SampleRate == 0)
                findings.Add(Finding("Warning", "SCL_SV_SAMPLE_RATE_MISSING", $"SV {sv.ControlBlockReference} has no smpRate value.", sv.ControlBlockReference));
            if (sv.NoAsdu == 0)
                findings.Add(Finding("Warning", "SCL_SV_ASDU_COUNT_MISSING", $"SV {sv.ControlBlockReference} has no ASDU count.", sv.ControlBlockReference));
        }

        foreach (var report in document.ReportControls)
{
    switch (report.DataSetBindingStatus)
    {
        case SclDataSetBindingStatus.NotSpecified when report.Indexed:
            findings.Add(Finding(
                "Warning",
                "SCL_REPORT_DATASET_UNASSIGNED",
                $"Indexed report {report.ControlBlockReference} has no datSet assignment. The RCB may be preallocated and must be bound before use.",
                report.ControlBlockReference));
            break;
        case SclDataSetBindingStatus.NotSpecified:
            findings.Add(Finding(
                "High",
                "SCL_REPORT_DATASET_MISSING",
                $"Report {report.ControlBlockReference} has no datSet attribute.",
                report.ControlBlockReference));
            break;
        case SclDataSetBindingStatus.Unresolved:
            findings.Add(Finding(
                "Warning",
                "SCL_REPORT_DATASET_UNRESOLVED",
                $"Report {report.ControlBlockReference} references DataSet '{report.DataSetName}', but no unique matching DataSet was resolved in the same IED, logical device, and logical node scope.",
                report.ControlBlockReference));
            break;
        case SclDataSetBindingStatus.ResolvedEmpty:
            findings.Add(Finding(
                "High",
                "SCL_REPORT_DATASET_EMPTY",
                $"Report {report.ControlBlockReference} resolves to DataSet {report.DataSetReference}, but that DataSet contains no FCDA members.",
                report.ControlBlockReference));
            break;
    }

    if (report.ConfigurationRevision == 0)
        findings.Add(Finding("Warning", "SCL_REPORT_CONFREV_ZERO", $"Report {report.ControlBlockReference} has confRev=0.", report.ControlBlockReference));
}

        foreach (var extRef in externalReferences)
        {
            if (string.IsNullOrWhiteSpace(extRef.SourceIedName) || string.IsNullOrWhiteSpace(extRef.SourceLdInst) || string.IsNullOrWhiteSpace(extRef.SourceLnClass) || string.IsNullOrWhiteSpace(extRef.DoName))
                findings.Add(Finding("Warning", "SCL_EXTREF_INCOMPLETE", $"ExtRef under {extRef.SubscriberReference} is missing source binding fields.", extRef.SubscriberReference));
        }
    }

    private static SclEngineeringFinding Finding(string severity, string code, string message, string objectReference = "")
        => new() { Severity = severity, Code = code, Message = message, ObjectReference = objectReference };

    private static string BuildSourceSignalReference(string iedName, string ldInst, string prefix, string lnClass, string lnInst, string doName, string daName)
    {
        if (string.IsNullOrWhiteSpace(iedName) && string.IsNullOrWhiteSpace(ldInst) && string.IsNullOrWhiteSpace(lnClass) && string.IsNullOrWhiteSpace(doName))
            return string.Empty;

        var ln = $"{prefix}{lnClass}{lnInst}";
        var data = string.IsNullOrWhiteSpace(daName) ? doName : $"{doName}.{daName}";
        return $"{iedName}/{ldInst}/{ln}.{data}";
    }

    private static int SeverityRank(string severity)
        => severity.Equals("High", StringComparison.OrdinalIgnoreCase) ? 3
            : severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? 2
            : severity.Equals("Info", StringComparison.OrdinalIgnoreCase) ? 1
            : 0;

    private static string NormalizeCode(string text)
        => string.IsNullOrWhiteSpace(text)
            ? "UNKNOWN"
            : new string(text.Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_').ToArray());

    private static bool Same(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    private static bool Is(XElement element, string localName) => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string Attr(XElement? element, string localName)
    {
        if (element is null)
            return string.Empty;

        var attr = element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, localName, StringComparison.Ordinal));
        return attr?.Value?.Trim() ?? string.Empty;
    }
}
