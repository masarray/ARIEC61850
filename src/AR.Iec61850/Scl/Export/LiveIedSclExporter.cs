using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Scl.Export;

public static class LiveIedSclExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    public static XDocument BuildDocument(LiveIedModelDiscoveryDocument model, LiveIedSclExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new LiveIedSclExportOptions();

        var context = BuildTypeTemplates(model, options);
        var root = new XElement(
            Scl + "SCL",
            new XAttribute("version", "2007"),
            new XAttribute("revision", "B"));

        root.Add(new XElement(
            Scl + "Header",
            new XAttribute("id", SafeXmlName($"{EffectiveIedName(model)}_GENERATED")),
            new XAttribute("version", "1"),
            new XAttribute("revision", "0"),
            new XAttribute("toolID", "ARIEC61850"),
            new XAttribute("nameStructure", "IEDName")));

        root.Add(BuildCommunication(model, options));
        root.Add(BuildIed(model, context, options));
        root.Add(BuildDataTypeTemplates(context));
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    public static LiveIedSclExportResult WriteFiles(
        LiveIedModelDiscoveryDocument model,
        string sclPath,
        LiveIedSclExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(sclPath))
            throw new ArgumentException("SCL output path is empty.", nameof(sclPath));

        options ??= new LiveIedSclExportOptions();
        var directory = Path.GetDirectoryName(sclPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var context = BuildTypeTemplates(model, options);
        var document = BuildDocumentWithContext(model, options, context);
        document.Save(sclPath);

        var report = BuildReport(model, options, context, sclPath);
        var reportPath = Path.ChangeExtension(sclPath, ".scl-export-report.json");
        var summaryPath = Path.ChangeExtension(sclPath, ".scl-export-summary.md");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8);
        File.WriteAllText(summaryPath, BuildMarkdown(model, report), Encoding.UTF8);

        return new LiveIedSclExportResult
        {
            GeneratedAtUtc = report.GeneratedAtUtc,
            Profile = report.Profile,
            SclPath = sclPath,
            ReportPath = reportPath,
            SummaryPath = summaryPath,
            LogicalDeviceCount = report.LogicalDeviceCount,
            LogicalNodeCount = report.LogicalNodeCount,
            DataSetCount = report.DataSetCount,
            ReportControlCount = report.ReportControlCount,
            GooseControlBlockCount = report.GooseControlBlockCount,
            SampledValueControlBlockCount = report.SampledValueControlBlockCount,
            SettingGroupControlCount = report.SettingGroupControlCount,
            LogControlCount = report.LogControlCount,
            LNodeTypeCount = report.LNodeTypeCount,
            DoTypeCount = report.DoTypeCount,
            DaTypeCount = report.DaTypeCount,
            EnumTypeCount = report.EnumTypeCount,
            Warnings = report.Warnings,
            DataSetMappings = report.DataSetMappings,
            ReportMappings = report.ReportMappings,
            ControlBlockMappings = report.ControlBlockMappings
        };
    }

    private static XDocument BuildDocumentWithContext(
        LiveIedModelDiscoveryDocument model,
        LiveIedSclExportOptions options,
        LiveIedSclBuildContext context)
    {
        var root = new XElement(
            Scl + "SCL",
            new XAttribute("version", "2007"),
            new XAttribute("revision", "B"));

        root.Add(new XElement(
            Scl + "Header",
            new XAttribute("id", SafeXmlName($"{EffectiveIedName(model)}_GENERATED")),
            new XAttribute("version", "1"),
            new XAttribute("revision", "0"),
            new XAttribute("toolID", "ARIEC61850"),
            new XAttribute("nameStructure", "IEDName")));
        root.Add(BuildCommunication(model, options));
        root.Add(BuildIed(model, context, options));
        root.Add(BuildDataTypeTemplates(context));
        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private static LiveIedSclExportResult BuildReport(
        LiveIedModelDiscoveryDocument model,
        LiveIedSclExportOptions options,
        LiveIedSclBuildContext context,
        string sclPath)
        => new()
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Profile = options.Profile,
            SclPath = sclPath,
            LogicalDeviceCount = model.LogicalDevices.Count,
            LogicalNodeCount = model.LogicalDevices.SelectMany(x => x.LogicalNodes).Count(),
            DataSetCount = model.DataSets.Count,
            ReportControlCount = model.ReportControls.Count,
            GooseControlBlockCount = model.GooseControlBlocks.Count,
            SampledValueControlBlockCount = model.SampledValueControlBlocks.Count,
            SettingGroupControlCount = model.SettingGroupControls.Count,
            LogControlCount = model.LogControls.Count,
            LNodeTypeCount = context.LNodeTypes.Count,
            DoTypeCount = context.DoTypes.Count,
            DaTypeCount = context.DaTypes.Count,
            EnumTypeCount = context.EnumTypes.Count,
            Warnings = MergeWarnings(model, context),
            DataSetMappings = context.DataSetMappings.ToArray(),
            ReportMappings = context.ReportMappings.ToArray(),
            ControlBlockMappings = context.ControlBlockMappings.ToArray()
        };


    private static IReadOnlyList<LiveIedSclExportWarning> MergeWarnings(
        LiveIedModelDiscoveryDocument model,
        LiveIedSclBuildContext context)
    {
        var warnings = new List<LiveIedSclExportWarning>(context.Warnings);
        warnings.AddRange(model.Warnings.Select(warning => new LiveIedSclExportWarning
        {
            Code = $"Discovery.{warning.Code}",
            Reference = warning.Reference,
            Message = warning.Message
        }));

        return warnings
            .GroupBy(warning => $"{warning.Code}|{warning.Reference}|{warning.Message}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static XElement BuildCommunication(LiveIedModelDiscoveryDocument model, LiveIedSclExportOptions options)
    {
        var address = new XElement(Scl + "Address");
        AddAddressP(address, "IP", string.IsNullOrWhiteSpace(options.IpAddress) ? model.Host : options.IpAddress);
        AddAddressP(address, "IP-SUBNET", options.IpSubnet);
        AddAddressP(address, "IP-GATEWAY", options.IpGateway);

        if (options.IncludeDefaultOsiParameters)
        {
            AddAddressP(address, "OSI-AP-Title", options.OsiApTitle);
            AddAddressP(address, "OSI-AE-Qualifier", options.OsiAeQualifier);
            AddAddressP(address, "OSI-PSEL", options.OsiPsel);
            AddAddressP(address, "OSI-SSEL", options.OsiSsel);
            AddAddressP(address, "OSI-TSEL", options.OsiTsel);
        }

        return new XElement(
            Scl + "Communication",
            new XElement(
                Scl + "SubNetwork",
                new XAttribute("name", SafeXmlName(options.SubNetworkName)),
                new XAttribute("type", "8-MMS"),
                new XElement(
                    Scl + "ConnectedAP",
                    new XAttribute("iedName", EffectiveIedName(model)),
                    new XAttribute("apName", EffectiveAccessPointName(model)),
                    address)));
    }

    private static XElement BuildIed(
        LiveIedModelDiscoveryDocument model,
        LiveIedSclBuildContext context,
        LiveIedSclExportOptions options)
    {
        var server = new XElement(Scl + "Server");
        foreach (var ld in model.LogicalDevices.OrderBy(x => SclLogicalDeviceInst(context, x), StringComparer.OrdinalIgnoreCase))
            server.Add(BuildLDevice(model, ld, context, options));

        var ied = new XElement(
            Scl + "IED",
            new XAttribute("name", EffectiveIedName(model)),
            new XAttribute("manufacturer", "ARIEC61850"),
            new XAttribute("type", "GeneratedLiveDiscovery"),
            new XAttribute("configVersion", model.GeneratedAtUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)),
            new XElement(
                Scl + "AccessPoint",
                new XAttribute("name", EffectiveAccessPointName(model)),
                server));

        if (options.IncludeRuntimeStateComment)
        {
            ied.AddFirst(new XComment("Generated from live MMS discovery. Runtime states such as RptEna, ResvTms, Owner, SqNum, EntryID, and contention evidence are intentionally not encoded as static SCL configuration."));
        }

        return ied;
    }

    private static XElement BuildLDevice(
        LiveIedModelDiscoveryDocument model,
        LiveIedLogicalDeviceModel ld,
        LiveIedSclBuildContext context,
        LiveIedSclExportOptions options)
    {
        var ldDomain = LogicalDeviceDomain(ld);
        var sclLdInst = SclLogicalDeviceInst(context, ld);
        var lDevice = new XElement(Scl + "LDevice", new XAttribute("inst", sclLdInst));
        var lns = ld.LogicalNodes.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var ln0 = lns.FirstOrDefault(x => string.Equals(x.LnClass, "LLN0", StringComparison.OrdinalIgnoreCase));
        if (ln0 is null)
        {
            var syntheticTypeId = MakeUniqueId(context, $"LN_LLN0_{Iec61850ReferenceParts.SafeIdPart(sclLdInst)}");
            ln0 = new LiveIedLogicalNodeModel
            {
                Name = "LLN0",
                LnClass = "LLN0",
                ProposedLnTypeId = syntheticTypeId
            };
            context.LogicalNodeTypeIds[LogicalNodeKey(ldDomain, "LLN0")] = syntheticTypeId;
            context.LNodeTypes.Add(new XElement(Scl + "LNodeType", new XAttribute("id", syntheticTypeId), new XAttribute("lnClass", "LLN0")));
            context.Warnings.Add(new LiveIedSclExportWarning
            {
                Code = "SyntheticLLN0",
                Reference = ldDomain,
                Message = "Live discovery did not contain LLN0. A minimal LLN0 was synthesized so the SCL can host DataSet and control blocks."
            });
        }

        lDevice.Add(BuildLogicalNodeElement(model, ld, ln0, context, options, isLn0: true));
        foreach (var ln in lns.Where(x => !string.Equals(x.LnClass, "LLN0", StringComparison.OrdinalIgnoreCase)))
            lDevice.Add(BuildLogicalNodeElement(model, ld, ln, context, options, isLn0: false));

        return lDevice;
    }

    private static XElement BuildLogicalNodeElement(
        LiveIedModelDiscoveryDocument model,
        LiveIedLogicalDeviceModel ld,
        LiveIedLogicalNodeModel ln,
        LiveIedSclBuildContext context,
        LiveIedSclExportOptions options,
        bool isLn0)
    {
        var ldDomain = LogicalDeviceDomain(ld);
        var typeId = context.LogicalNodeTypeIds.TryGetValue(LogicalNodeKey(ldDomain, ln.Name), out var existingType)
            ? existingType
            : MakeUniqueId(context, string.IsNullOrWhiteSpace(ln.ProposedLnTypeId) ? $"LN_{Iec61850ReferenceParts.SafeIdPart(ln.LnClass)}_{Iec61850ReferenceParts.SafeIdPart(ln.Name)}" : ln.ProposedLnTypeId);

        var element = isLn0
            ? new XElement(Scl + "LN0", new XAttribute("lnClass", "LLN0"), new XAttribute("lnType", typeId))
            : new XElement(
                Scl + "LN",
                OptionalAttribute("prefix", ln.Prefix),
                new XAttribute("lnClass", string.IsNullOrWhiteSpace(ln.LnClass) ? ln.Name : ln.LnClass),
                new XAttribute("inst", ln.LnInst),
                new XAttribute("lnType", typeId));

        foreach (var dataSet in model.DataSets.Where(x => string.Equals(x.Domain, ldDomain, StringComparison.OrdinalIgnoreCase) && SameLogicalNode(x.LogicalNode, ln.Name)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            element.Add(BuildDataSet(model, dataSet, context));

        foreach (var rcb in model.ReportControls.Where(x => string.Equals(x.Domain, ldDomain, StringComparison.OrdinalIgnoreCase) && SameLogicalNode(x.LogicalNode, ln.Name)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            element.Add(BuildReportControl(model, rcb, context));

        foreach (var gcb in model.GooseControlBlocks.Where(x => string.Equals(x.Domain, ldDomain, StringComparison.OrdinalIgnoreCase) && SameLogicalNode(x.LogicalNode, ln.Name)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            element.Add(BuildGseControl(model, gcb, context));

        foreach (var svcb in model.SampledValueControlBlocks.Where(x => string.Equals(x.Domain, ldDomain, StringComparison.OrdinalIgnoreCase) && SameLogicalNode(x.LogicalNode, ln.Name)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            element.Add(BuildSampledValueControl(model, svcb, context));

        foreach (var lcb in model.LogControls.Where(x => string.Equals(x.Domain, ldDomain, StringComparison.OrdinalIgnoreCase) && SameLogicalNode(x.LogicalNode, ln.Name)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            element.Add(BuildLogControl(model, lcb, context));

        if (isLn0)
        {
            foreach (var sgcb in model.SettingGroupControls.Where(x => string.Equals(x.Domain, ldDomain, StringComparison.OrdinalIgnoreCase) && SameLogicalNode(x.LogicalNode, ln.Name)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                element.Add(BuildSettingControl(model, sgcb, context));
        }

        return element;
    }

    private static XElement BuildDataSet(
        LiveIedModelDiscoveryDocument model,
        LiveIedDataSetModel dataSet,
        LiveIedSclBuildContext context)
    {
        var element = new XElement(Scl + "DataSet", new XAttribute("name", SafeXmlName(dataSet.Name)));
        foreach (var member in dataSet.Members.OrderBy(x => x.Index))
        {
            var parts = ParseSignalReference(model, context, member.Reference, member.FunctionalConstraint);
            if (string.IsNullOrWhiteSpace(parts.LdInst) || string.IsNullOrWhiteSpace(parts.LnClass) || string.IsNullOrWhiteSpace(parts.DoName))
            {
                context.Warnings.Add(new LiveIedSclExportWarning
                {
                    Code = "UnresolvedFCDA",
                    Reference = member.Reference,
                    Message = "DataSet member could not be converted to a SCL FCDA reference."
                });
                continue;
            }

            var fcda = new XElement(
                Scl + "FCDA",
                OptionalAttribute("iedName", parts.IedNameOverride),
                new XAttribute("ldInst", parts.LdInst),
                OptionalAttribute("prefix", parts.Prefix),
                new XAttribute("lnClass", parts.LnClass),
                new XAttribute("lnInst", parts.LnInst),
                new XAttribute("doName", parts.DoName),
                OptionalAttribute("daName", parts.DaName),
                new XAttribute("fc", string.IsNullOrWhiteSpace(parts.Fc) ? member.FunctionalConstraint : parts.Fc));
            element.Add(fcda);
            context.DataSetMappings.Add(new LiveIedSclExportMapping
            {
                Kind = "FCDA",
                SourceReference = member.Reference,
                SclReference = FormatFcdaReference(parts),
                Message = "DataSet member converted to SCL FCDA."
            });
        }

        if (!element.Elements(Scl + "FCDA").Any())
            context.Warnings.Add(new LiveIedSclExportWarning
            {
                Code = "EmptyDataSet",
                Reference = dataSet.Reference,
                Message = "Generated DataSet has no valid FCDA entries."
            });

        return element;
    }

    private static XElement BuildReportControl(
        LiveIedModelDiscoveryDocument model,
        LiveIedReportControlModel rcb,
        LiveIedSclBuildContext context)
    {
        var name = SafeXmlName(rcb.Name);
        var dataSetName = LocalDataSetName(rcb.DataSetReference);
        var element = new XElement(
            Scl + "ReportControl",
            new XAttribute("name", name),
            OptionalAttribute("rptID", rcb.ReportId),
            OptionalAttribute("datSet", dataSetName),
            new XAttribute("confRev", ParseUIntText(rcb.ConfRev, 1U)),
            new XAttribute("buffered", rcb.Buffered ? "true" : "false"),
            new XAttribute("indexed", "false"),
            new XAttribute("bufTime", ParseUIntText(rcb.BufferTimeMs, 0U)),
            new XAttribute("intgPd", ParseUIntText(rcb.IntegrityPeriodMs, 0U)),
            BuildTriggerOptions(rcb.TriggerOptions),
            BuildOptionalFields(rcb.OptionalFields));

        context.ReportMappings.Add(new LiveIedSclExportMapping
        {
            Kind = rcb.Buffered ? "BRCB" : "URCB",
            SourceReference = rcb.Reference,
            SclReference = $"{ReconstructedLogicalDeviceReference(model, context, rcb.Domain)}/{rcb.LogicalNode}${(rcb.Buffered ? "BR" : "RP")}${name}",
            Message = "ReportControl converted from live RCB discovery. Runtime RptEna/Resv state excluded."
        });
        return element;
    }

    private static XElement BuildTriggerOptions(string source)
    {
        var normalized = source ?? string.Empty;
        return new XElement(
            Scl + "TrgOps",
            new XAttribute("dchg", ContainsAny(normalized, "data-change", "dchg") ? "true" : "true"),
            new XAttribute("qchg", ContainsAny(normalized, "quality-change", "qchg") ? "true" : "true"),
            new XAttribute("dupd", ContainsAny(normalized, "data-update", "dupd") ? "true" : "false"),
            new XAttribute("period", ContainsAny(normalized, "period", "integrity") ? "true" : "false"),
            new XAttribute("gi", ContainsAny(normalized, "gi", "general-interrogation") ? "true" : "true"));
    }

    private static XElement BuildOptionalFields(string source)
    {
        var normalized = source ?? string.Empty;
        var hasHints = !string.IsNullOrWhiteSpace(normalized);
        return new XElement(
            Scl + "OptFields",
            new XAttribute("seqNum", !hasHints || ContainsAny(normalized, "sequence-number", "seqnum") ? "true" : "false"),
            new XAttribute("timeStamp", !hasHints || ContainsAny(normalized, "report-time-stamp", "timestamp", "timeofentry") ? "true" : "false"),
            new XAttribute("reasonCode", !hasHints || ContainsAny(normalized, "reason", "reason-for-inclusion") ? "true" : "false"),
            new XAttribute("dataSet", !hasHints || ContainsAny(normalized, "data-set-name", "dataset") ? "true" : "false"),
            new XAttribute("dataRef", ContainsAny(normalized, "data-reference", "dataref") ? "true" : "false"),
            new XAttribute("entryID", !hasHints || ContainsAny(normalized, "entryid", "entryID") ? "true" : "false"),
            new XAttribute("configRef", !hasHints || ContainsAny(normalized, "conf-revision", "configref") ? "true" : "false"),
            new XAttribute("bufOvfl", !hasHints || ContainsAny(normalized, "buffer-overflow", "bufovfl") ? "true" : "false"));
    }

    private static XElement BuildGseControl(
        LiveIedModelDiscoveryDocument model,
        LiveIedControlBlockModel control,
        LiveIedSclBuildContext context)
    {
        var name = SafeXmlName(control.Name);
        var element = new XElement(
            Scl + "GSEControl",
            new XAttribute("name", name),
            new XAttribute("type", "GOOSE"),
            OptionalAttribute("appID", FirstNonEmpty(control.ControlId, control.AppId, control.Name)),
            OptionalAttribute("datSet", LocalDataSetName(control.DataSetReference)),
            new XAttribute("confRev", ParseUIntText(control.ConfRev, 1U)));

        AddControlBlockValueWarning(control, context, "GSEControl");
        context.ControlBlockMappings.Add(new LiveIedSclExportMapping
        {
            Kind = "GSEControl",
            SourceReference = control.Reference,
            SclReference = $"{ReconstructedLogicalDeviceReference(model, context, control.Domain)}/{control.LogicalNode}$GO${name}",
            Message = string.IsNullOrWhiteSpace(control.DataSetReference)
                ? "GSEControl shell exported from live GO attribute inventory; DatSet value is not known yet."
                : "GSEControl exported from live discovery."
        });
        return element;
    }

    private static XElement BuildSampledValueControl(
        LiveIedModelDiscoveryDocument model,
        LiveIedControlBlockModel control,
        LiveIedSclBuildContext context)
    {
        var name = SafeXmlName(control.Name);
        var element = new XElement(
            Scl + "SampledValueControl",
            new XAttribute("name", name),
            OptionalAttribute("smvID", FirstNonEmpty(control.SmvId, control.ControlId, control.Name)),
            OptionalAttribute("datSet", LocalDataSetName(control.DataSetReference)),
            new XAttribute("confRev", ParseUIntText(control.ConfRev, 1U)),
            new XAttribute("multicast", "true"),
            OptionalAttribute("smpRate", control.SampleRate),
            OptionalAttribute("smpMod", control.SampleMode),
            OptionalAttribute("nofASDU", control.NumberOfAsdu));

        AddControlBlockValueWarning(control, context, "SampledValueControl");
        context.ControlBlockMappings.Add(new LiveIedSclExportMapping
        {
            Kind = "SampledValueControl",
            SourceReference = control.Reference,
            SclReference = $"{ReconstructedLogicalDeviceReference(model, context, control.Domain)}/{control.LogicalNode}$SV${name}",
            Message = string.IsNullOrWhiteSpace(control.DataSetReference)
                ? "SampledValueControl shell exported from live MS/US attribute inventory; DatSet value is not known yet."
                : "SampledValueControl exported from live discovery."
        });
        return element;
    }

    private static XElement BuildSettingControl(
        LiveIedModelDiscoveryDocument model,
        LiveIedControlBlockModel control,
        LiveIedSclBuildContext context)
    {
        context.ControlBlockMappings.Add(new LiveIedSclExportMapping
        {
            Kind = "SettingControl",
            SourceReference = control.Reference,
            SclReference = $"{ReconstructedLogicalDeviceReference(model, context, control.Domain)}/{control.LogicalNode}$SG${SafeXmlName(control.Name)}",
            Message = "SettingControl shell exported from SG/SE attribute inventory. ActSG/EditSG values remain runtime evidence until SGCB value read is implemented."
        });
        context.Warnings.Add(new LiveIedSclExportWarning
        {
            Code = "SettingControlShell",
            Reference = control.Reference,
            Message = "Setting group control was detected, but NumOfSG/ActSG/EditSG values are not read yet. Exported SCL contains a conservative SettingControl shell."
        });
        return new XElement(Scl + "SettingControl", new XAttribute("numOfSGs", "1"));
    }

    private static XElement BuildLogControl(
        LiveIedModelDiscoveryDocument model,
        LiveIedControlBlockModel control,
        LiveIedSclBuildContext context)
    {
        var name = SafeXmlName(control.Name);
        var element = new XElement(
            Scl + "LogControl",
            new XAttribute("name", name),
            OptionalAttribute("datSet", LocalDataSetName(control.DataSetReference)));
        AddControlBlockValueWarning(control, context, "LogControl");
        context.ControlBlockMappings.Add(new LiveIedSclExportMapping
        {
            Kind = "LogControl",
            SourceReference = control.Reference,
            SclReference = $"{ReconstructedLogicalDeviceReference(model, context, control.Domain)}/{control.LogicalNode}$LG${name}",
            Message = "LogControl shell exported from live LG attribute inventory."
        });
        return element;
    }

    private static void AddControlBlockValueWarning(LiveIedControlBlockModel control, LiveIedSclBuildContext context, string sclElementName)
    {
        if (string.IsNullOrWhiteSpace(control.DataSetReference) && control.Attributes.Any(x => string.Equals(x, "DatSet", StringComparison.OrdinalIgnoreCase)))
        {
            context.Warnings.Add(new LiveIedSclExportWarning
            {
                Code = $"{sclElementName}DataSetValueNotRead",
                Reference = control.Reference,
                Message = "Control block DatSet attribute is present in live model, but its value has not been read yet. The SCL element is exported as a shell and companion JSON keeps the discovery evidence."
            });
        }

        if (string.Equals(control.AddressStatus, "NotDiscovered", StringComparison.OrdinalIgnoreCase) && (string.Equals(sclElementName, "GSEControl", StringComparison.OrdinalIgnoreCase) || string.Equals(sclElementName, "SampledValueControl", StringComparison.OrdinalIgnoreCase)))
        {
            context.Warnings.Add(new LiveIedSclExportWarning
            {
                Code = $"{sclElementName}CommunicationAddressMissing",
                Reference = control.Reference,
                Message = "Multicast address/APPID/VLAN was not discovered from MMS attribute inventory. Communication GSE/SMV address binding is intentionally not generated yet."
            });
        }
    }

    private static XElement BuildDataTypeTemplates(LiveIedSclBuildContext context)
        => new(
            Scl + "DataTypeTemplates",
            context.LNodeTypes.OrderBy(x => AttributeValue(x, "id"), StringComparer.OrdinalIgnoreCase),
            context.DoTypes.OrderBy(x => AttributeValue(x, "id"), StringComparer.OrdinalIgnoreCase),
            context.DaTypes.OrderBy(x => AttributeValue(x, "id"), StringComparer.OrdinalIgnoreCase),
            context.EnumTypes.OrderBy(x => AttributeValue(x, "id"), StringComparer.OrdinalIgnoreCase));

    private static LiveIedSclBuildContext BuildTypeTemplates(LiveIedModelDiscoveryDocument model, LiveIedSclExportOptions options)
    {
        var context = new LiveIedSclBuildContext();
        InitializeLogicalDeviceNameMap(model, options, context);
        foreach (var ld in model.LogicalDevices.OrderBy(x => SclLogicalDeviceInst(context, x), StringComparer.OrdinalIgnoreCase))
        {
            foreach (var ln in ld.LogicalNodes.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var lnKey = LogicalNodeKey(LogicalDeviceDomain(ld), ln.Name);
                var lnTypeId = MakeUniqueId(context, string.IsNullOrWhiteSpace(ln.ProposedLnTypeId) ? $"LN_{Iec61850ReferenceParts.SafeIdPart(ln.LnClass)}_{Iec61850ReferenceParts.SafeIdPart(ln.Name)}" : ln.ProposedLnTypeId);
                context.LogicalNodeTypeIds[lnKey] = lnTypeId;
                var lNodeType = new XElement(
                    Scl + "LNodeType",
                    new XAttribute("id", lnTypeId),
                    new XAttribute("lnClass", string.IsNullOrWhiteSpace(ln.LnClass) ? ln.Name : ln.LnClass));

                foreach (var dataObject in ln.DataObjects.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (IsControlBlockDataObject(dataObject))
                        continue;

                    if (!CdcInferenceEngine.IsKnownCdc(dataObject.InferredCdc))
                    {
                        context.Warnings.Add(new LiveIedSclExportWarning
                        {
                            Code = "UnknownCdcSkipped",
                            Reference = dataObject.Reference,
                            Message = "Data object was discovered online, but its CDC could not be inferred safely enough for SCL DataTypeTemplates. It is omitted instead of exporting an invalid CDC."
                        });
                        continue;
                    }

                    if (!options.IncludeLowConfidenceTypes && dataObject.ConfidenceLevel is LiveIedDiscoveryConfidenceLevel.Low or LiveIedDiscoveryConfidenceLevel.Unknown)
                        continue;

                    var doTypeId = MakeUniqueId(context, dataObject.ProposedDoTypeId);
                    context.DataObjectTypeIds[dataObject.Reference] = doTypeId;
                    lNodeType.Add(new XElement(Scl + "DO", new XAttribute("name", SafeXmlName(dataObject.Name)), new XAttribute("type", doTypeId)));
                    context.DoTypes.Add(BuildDoType(dataObject, doTypeId, context));
                }

                context.LNodeTypes.Add(lNodeType);
            }
        }

        EnsureReferencedMemberTypes(model, context);
        return context;
    }

    private static XElement BuildDoType(LiveIedDataObjectModel dataObject, string doTypeId, LiveIedSclBuildContext context)
    {
        var cdc = dataObject.InferredCdc.Trim();
        if (!CdcInferenceEngine.IsKnownCdc(cdc))
        {
            cdc = "SPS";
            context.Warnings.Add(new LiveIedSclExportWarning
            {
                Code = "UnknownCdcFallback",
                Reference = dataObject.Reference,
                Message = "Internal export fallback used SPS because the inferred CDC was empty or unknown. This should only be reached by direct BuildDoType callers."
            });
        }

        var doType = new XElement(Scl + "DOType", new XAttribute("id", doTypeId), new XAttribute("cdc", cdc));
        var tree = TypeTreeNode.Build(dataObject.Attributes);
        if (tree.Children.Count == 0)
        {
            context.Warnings.Add(new LiveIedSclExportWarning
            {
                Code = "EmptyDOType",
                Reference = dataObject.Reference,
                Message = "No DA path was available for this DO. The DOType is generated as a semantic shell only."
            });
            return doType;
        }

        foreach (var child in tree.Children.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            doType.Add(BuildDaElement(child, doTypeId, context, cdc, isRootDa: true));

        return doType;
    }

    private static XElement BuildDaElement(TypeTreeNode node, string ownerTypeId, LiveIedSclBuildContext context, string cdc, bool isRootDa)
    {
        if (node.Children.Count == 0)
        {
            var bType = NormalizeBType(node.BType, node.Name, node.Path, cdc);
            return new XElement(
                Scl + (isRootDa ? "DA" : "BDA"),
                new XAttribute("name", SafeXmlName(node.Name)),
                isRootDa ? new XAttribute("fc", string.IsNullOrWhiteSpace(node.Fc) ? "ST" : node.Fc) : null,
                new XAttribute("bType", bType));
        }

        var daTypeId = MakeUniqueId(context, $"DA_{Iec61850ReferenceParts.SafeIdPart(ownerTypeId)}_{Iec61850ReferenceParts.SafeIdPart(node.Path)}");
        context.DataAttributeTypeIds[$"{ownerTypeId}|{node.Path}"] = daTypeId;
        var daType = new XElement(Scl + "DAType", new XAttribute("id", daTypeId));
        foreach (var child in node.Children.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            daType.Add(BuildDaElement(child, daTypeId, context, cdc, isRootDa: false));
        context.DaTypes.Add(daType);

        return new XElement(
            Scl + (isRootDa ? "DA" : "BDA"),
            new XAttribute("name", SafeXmlName(node.Name)),
            isRootDa ? new XAttribute("fc", string.IsNullOrWhiteSpace(node.Fc) ? "ST" : node.Fc) : null,
            new XAttribute("bType", "Struct"),
            new XAttribute("type", daTypeId));
    }

    private static void EnsureReferencedMemberTypes(LiveIedModelDiscoveryDocument model, LiveIedSclBuildContext context)
    {
        foreach (var dataSet in model.DataSets)
        {
            foreach (var member in dataSet.Members)
            {
                var parts = ParseSignalReference(model, context, member.Reference, member.FunctionalConstraint);
                if (string.IsNullOrWhiteSpace(parts.LdInst) || string.IsNullOrWhiteSpace(parts.LnClass) || string.IsNullOrWhiteSpace(parts.DoName))
                    continue;

                var lnKey = LogicalNodeKey(parts.MmsDomain, parts.LogicalNodeName);
                if (!context.LogicalNodeTypeIds.ContainsKey(lnKey))
                {
                    var lnTypeId = MakeUniqueId(context, $"LN_{Iec61850ReferenceParts.SafeIdPart(parts.LnClass)}_{Iec61850ReferenceParts.SafeIdPart(parts.LogicalNodeName)}");
                    context.LogicalNodeTypeIds[lnKey] = lnTypeId;
                    var lNodeType = new XElement(Scl + "LNodeType", new XAttribute("id", lnTypeId), new XAttribute("lnClass", parts.LnClass));
                    var inferred = CdcInferenceEngine.Infer(parts.LnClass, parts.DoName, Array.Empty<string>(), [parts.Fc]);
                    var cdc = CdcInferenceEngine.IsKnownCdc(inferred.Cdc) ? inferred.Cdc : "SPS";
                    var doTypeId = MakeUniqueId(context, $"DO_{Iec61850ReferenceParts.SafeIdPart(cdc)}_{Iec61850ReferenceParts.SafeIdPart(parts.LnClass)}_{Iec61850ReferenceParts.SafeIdPart(parts.DoName)}");
                    lNodeType.Add(new XElement(Scl + "DO", new XAttribute("name", SafeXmlName(parts.DoName)), new XAttribute("type", doTypeId)));
                    context.LNodeTypes.Add(lNodeType);
                    context.DoTypes.Add(new XElement(Scl + "DOType", new XAttribute("id", doTypeId), new XAttribute("cdc", cdc)));
                    context.Warnings.Add(new LiveIedSclExportWarning
                    {
                        Code = "SyntheticMemberType",
                        Reference = member.Reference,
                        Message = "DataSet member was not present in the live FC point model. A conservative valid CDC shell was synthesized."
                    });
                }
            }
        }
    }

    private static string BuildMarkdown(LiveIedModelDiscoveryDocument model, LiveIedSclExportResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Live-to-SCL Export Summary");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {result.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        sb.AppendLine($"- Source IED: {Escape(model.IedName)}");
        sb.AppendLine($"- Target: {Escape(model.Host)}:{model.Port.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"- SCL: {Escape(result.SclPath)}");
        sb.AppendLine($"- Profile: {Escape(result.Profile)}");
        sb.AppendLine();
        sb.AppendLine("## Export Counts");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | ---: |");
        AppendMetric(sb, "Logical devices", result.LogicalDeviceCount);
        AppendMetric(sb, "Logical nodes", result.LogicalNodeCount);
        AppendMetric(sb, "DataSets", result.DataSetCount);
        AppendMetric(sb, "ReportControls", result.ReportControlCount);
        AppendMetric(sb, "LNodeType", result.LNodeTypeCount);
        AppendMetric(sb, "DOType", result.DoTypeCount);
        AppendMetric(sb, "DAType", result.DaTypeCount);
        AppendMetric(sb, "EnumType", result.EnumTypeCount);
        sb.AppendLine();
        sb.AppendLine("## DataSet FCDA Mappings");
        sb.AppendLine();
        sb.AppendLine("| Source | SCL FCDA | Message |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var mapping in result.DataSetMappings.Take(80))
            sb.AppendLine($"| {Escape(mapping.SourceReference)} | {Escape(mapping.SclReference)} | {Escape(mapping.Message)} |");
        if (result.DataSetMappings.Count > 80)
            sb.AppendLine($"| ... | ... | {result.DataSetMappings.Count - 80} more mapping(s) in export report | ");
        sb.AppendLine();
        sb.AppendLine("## ReportControl Mappings");
        sb.AppendLine();
        sb.AppendLine("| Source | SCL ReportControl | Message |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var mapping in result.ReportMappings.Take(80))
            sb.AppendLine($"| {Escape(mapping.SourceReference)} | {Escape(mapping.SclReference)} | {Escape(mapping.Message)} |");
        sb.AppendLine();
        sb.AppendLine("## Control Block Mappings");
        sb.AppendLine();
        sb.AppendLine("| Kind | Source | SCL Control | Message |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var mapping in result.ControlBlockMappings.Take(80))
            sb.AppendLine($"| {Escape(mapping.Kind)} | {Escape(mapping.SourceReference)} | {Escape(mapping.SclReference)} | {Escape(mapping.Message)} |");
        if (result.ControlBlockMappings.Count > 80)
            sb.AppendLine($"| ... | ... | ... | {result.ControlBlockMappings.Count - 80} more item(s) in JSON report | ");
        sb.AppendLine();

        sb.AppendLine("## Safety Notes");
        sb.AppendLine();
        sb.AppendLine("- This SCL is reconstructed from live IEC 61850/MMS discovery, not the original vendor ICD.");
        sb.AppendLine("- FCDA, DataSet, and ReportControl references are intended to be importable/reusable by ARIEC61850 for connection and reporting workflows.");
        sb.AppendLine("- DataTypeTemplates are generated generic templates using exact FC evidence plus MMS type/CDC inference where available.");
        sb.AppendLine("- Runtime RCB ownership states such as RptEna, ResvTms, EntryID, SqNum, and contention state are excluded from static SCL and remain in companion evidence JSON.");
        sb.AppendLine();
        if (result.Warnings.Count > 0)
        {
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            foreach (var warning in result.Warnings)
                sb.AppendLine($"- {Escape(warning.Code)} {Escape(warning.Reference)}: {Escape(warning.Message)}");
        }

        return sb.ToString();
    }

    private static void AppendMetric(StringBuilder sb, string name, int value)
        => sb.AppendLine($"| {Escape(name)} | {value.ToString(CultureInfo.InvariantCulture)} |");

    private static void AddAddressP(XElement address, string type, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        address.Add(new XElement(Scl + "P", new XAttribute("type", type), value.Trim()));
    }

    private static XAttribute? OptionalAttribute(string name, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value.Trim());

    private static bool SameLogicalNode(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void InitializeLogicalDeviceNameMap(
        LiveIedModelDiscoveryDocument model,
        LiveIedSclExportOptions options,
        LiveIedSclBuildContext context)
    {
        if (context.SclLogicalDeviceInstByMmsDomain.Count > 0)
            return;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ld in model.LogicalDevices.OrderBy(x => LogicalDeviceDomain(x), StringComparer.OrdinalIgnoreCase))
        {
            var domain = LogicalDeviceDomain(ld);
            var candidate = options.LogicalDeviceNameMode == LiveIedSclLogicalDeviceNameMode.Keep
                ? domain
                : StripIedNamePrefix(domain, model.IedName);

            if (string.IsNullOrWhiteSpace(candidate))
                candidate = domain;

            if (!used.Add(candidate))
            {
                context.Warnings.Add(new LiveIedSclExportWarning
                {
                    Code = "LogicalDeviceNameCollision",
                    Reference = domain,
                    Message = $"Auto LD name mapping produced duplicate LDevice.inst '{candidate}'. Keeping the full MMS domain for this logical device."
                });
                candidate = domain;
                _ = used.Add(candidate);
            }

            context.SclLogicalDeviceInstByMmsDomain[domain] = candidate;
        }
    }

    private static string LogicalDeviceDomain(LiveIedLogicalDeviceModel ld)
        => string.IsNullOrWhiteSpace(ld.MmsDomain) ? ld.Inst.Trim() : ld.MmsDomain.Trim();

    private static string SclLogicalDeviceInst(LiveIedSclBuildContext context, LiveIedLogicalDeviceModel ld)
        => SclLogicalDeviceInstByDomain(context, LogicalDeviceDomain(ld));

    private static string SclLogicalDeviceInstByDomain(LiveIedSclBuildContext context, string domain)
    {
        var normalized = domain.Trim();
        return context.SclLogicalDeviceInstByMmsDomain.TryGetValue(normalized, out var sclLdInst)
            ? sclLdInst
            : normalized;
    }

    private static string ReconstructedLogicalDeviceReference(
        LiveIedModelDiscoveryDocument model,
        LiveIedSclBuildContext context,
        string domain)
    {
        var sclLdInst = SclLogicalDeviceInstByDomain(context, domain);
        return $"{EffectiveIedName(model)}{sclLdInst}";
    }

    private static string StripIedNamePrefix(string domain, string iedName)
    {
        var trimmedDomain = domain.Trim();
        var trimmedIed = iedName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedDomain) || string.IsNullOrWhiteSpace(trimmedIed))
            return trimmedDomain;

        return trimmedDomain.StartsWith(trimmedIed, StringComparison.OrdinalIgnoreCase) && trimmedDomain.Length > trimmedIed.Length
            ? trimmedDomain[trimmedIed.Length..]
            : trimmedDomain;
    }

    private static string EffectiveIedName(LiveIedModelDiscoveryDocument model)
        => string.IsNullOrWhiteSpace(model.IedName) ? "LIVE_IED" : SafeXmlName(model.IedName);

    private static string EffectiveAccessPointName(LiveIedModelDiscoveryDocument model)
        => string.IsNullOrWhiteSpace(model.AccessPointName) ? "AP1" : SafeXmlName(model.AccessPointName);

    private static string LocalDataSetName(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var dot = reference.LastIndexOf(".", StringComparison.Ordinal);
        return dot >= 0 && dot < reference.Length - 1 ? SafeXmlName(reference[(dot + 1)..]) : SafeXmlName(reference);
    }

    private static uint ParseUIntText(string text, uint fallback)
        => uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static bool ContainsAny(string source, params string[] needles)
        => needles.Any(needle => source.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string LogicalNodeKey(string ldInst, string lnName)
        => $"{ldInst}/{lnName}";

    private static string MakeUniqueId(LiveIedSclBuildContext context, string candidate)
    {
        var safe = SafeXmlName(string.IsNullOrWhiteSpace(candidate) ? "T" : candidate);
        if (context.UsedIds.Add(safe))
            return safe;

        for (var index = 2; ; index++)
        {
            var next = $"{safe}_{index.ToString(CultureInfo.InvariantCulture)}";
            if (context.UsedIds.Add(next))
                return next;
        }
    }

    private static string SafeXmlName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "X";

        var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray();
        var result = new string(chars);
        return char.IsLetter(result[0]) || result[0] == '_' ? result : $"_{result}";
    }

    private static bool IsControlBlockDataObject(LiveIedDataObjectModel dataObject)
        => string.Equals(dataObject.Name, "SGCB", StringComparison.OrdinalIgnoreCase) ||
           dataObject.Attributes.Any(attribute =>
            string.Equals(attribute.FunctionalConstraint, "BR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attribute.FunctionalConstraint, "RP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attribute.FunctionalConstraint, "GO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attribute.FunctionalConstraint, "MS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attribute.FunctionalConstraint, "US", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attribute.FunctionalConstraint, "SG", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attribute.FunctionalConstraint, "SE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attribute.FunctionalConstraint, "LG", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeBType(string bType, string name, string path, string cdc)
    {
        if (string.Equals(name, "q", StringComparison.OrdinalIgnoreCase))
            return "Quality";
        if (string.Equals(name, "t", StringComparison.OrdinalIgnoreCase))
            return "Timestamp";
        if (string.Equals(name, "T", StringComparison.Ordinal) || name.EndsWith("Tm", StringComparison.OrdinalIgnoreCase))
            return "Timestamp";

        var normalizedCdc = cdc.Trim().ToUpperInvariant();
        var normalizedPath = path.Trim();
        var inferred = InferBTypeFromCdc(normalizedCdc, name, normalizedPath);
        if (!string.IsNullOrWhiteSpace(inferred))
            return inferred;

        if (string.IsNullOrWhiteSpace(bType) || string.Equals(bType, "Unknown", StringComparison.OrdinalIgnoreCase) || string.Equals(bType, "Struct", StringComparison.OrdinalIgnoreCase))
            return "VisString255";
        return bType.Trim();
    }

    private static string InferBTypeFromCdc(string cdc, string name, string path)
    {
        if (string.Equals(name, "ctlModel", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "sboClass", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "orCat", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("dir", StringComparison.OrdinalIgnoreCase))
            return "INT32";

        if (string.Equals(name, "ctlNum", StringComparison.OrdinalIgnoreCase))
            return "INT8U";

        if (string.Equals(name, "Test", StringComparison.Ordinal) ||
            string.Equals(name, "Check", StringComparison.Ordinal) ||
            string.Equals(name, "general", StringComparison.OrdinalIgnoreCase) ||
            IsPhaseBooleanName(name))
            return "BOOLEAN";

        if (string.Equals(name, "orIdent", StringComparison.OrdinalIgnoreCase))
            return "Octet64";

        if (string.Equals(name, "SIUnit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "multiplier", StringComparison.OrdinalIgnoreCase))
            return "INT32";

        if (string.Equals(name, "db", StringComparison.OrdinalIgnoreCase))
            return "INT32U";

        if (string.Equals(name, "pulsQty", StringComparison.OrdinalIgnoreCase))
            return "FLOAT32";

        if (string.Equals(name, "actVal", StringComparison.OrdinalIgnoreCase))
            return "INT32";

        if (string.Equals(name, "ctlVal", StringComparison.OrdinalIgnoreCase))
        {
            if (cdc is "INC" or "ISC")
                return "INT32";
            if (cdc is "DPC" or "BSC")
                return "INT32";
            return "BOOLEAN";
        }

        if (string.Equals(name, "stVal", StringComparison.OrdinalIgnoreCase))
        {
            return cdc switch
            {
                "SPS" or "SPC" or "ACT" => "BOOLEAN",
                "DPS" or "DPC" => "Dbpos",
                "INS" or "INC" or "ENS" or "ENC" or "BCR" => "INT32",
                _ => string.Empty
            };
        }

        if ((string.Equals(name, "f", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(name, "i", StringComparison.OrdinalIgnoreCase)) &&
            (path.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".mag.i", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".ang.f", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".ang.i", StringComparison.OrdinalIgnoreCase)))
        {
            return string.Equals(name, "f", StringComparison.OrdinalIgnoreCase) ? "FLOAT32" : "INT32";
        }

        return string.Empty;
    }

    private static bool IsPhaseBooleanName(string name)
        => string.Equals(name, "phsA", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, "phsB", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, "phsC", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, "neut", StringComparison.OrdinalIgnoreCase);

    private static string AttributeValue(XElement element, string name)
        => element.Attributes().FirstOrDefault(x => string.Equals(x.Name.LocalName, name, StringComparison.Ordinal))?.Value ?? string.Empty;

    private static string Escape(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static SclReferenceParts ParseSignalReference(
        LiveIedModelDiscoveryDocument model,
        LiveIedSclBuildContext context,
        string reference,
        string fallbackFc)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return new SclReferenceParts(Fc: fallbackFc);

        var normalized = reference.Trim();
        var bracket = normalized.LastIndexOf("[", StringComparison.Ordinal);
        var fc = fallbackFc;
        if (bracket >= 0 && normalized.EndsWith(']'))
        {
            fc = normalized[(bracket + 1)..^1].Trim();
            normalized = normalized[..bracket].Trim();
        }

        var slash = normalized.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
            return new SclReferenceParts(Fc: fc);

        var mmsDomain = normalized[..slash].Trim();
        var ldInst = SclLogicalDeviceInstByDomain(context, mmsDomain);
        var rest = normalized[(slash + 1)..].Trim();
        var dot = rest.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
            return new SclReferenceParts(MmsDomain: mmsDomain, LdInst: ldInst, Fc: fc);

        var lnName = rest[..dot].Trim();
        var dataPath = rest[(dot + 1)..].Trim();
        var ln = Iec61850ReferenceParts.ParseLogicalNodeName(lnName);
        var doName = dataPath;
        var daName = string.Empty;
        var doDot = dataPath.IndexOf('.', StringComparison.Ordinal);
        if (doDot >= 0)
        {
            doName = dataPath[..doDot];
            daName = dataPath[(doDot + 1)..];
        }

        return new SclReferenceParts(
            MmsDomain: mmsDomain,
            LdInst: ldInst,
            LogicalNodeName: lnName,
            Prefix: ln.Prefix,
            LnClass: ln.SclLnClass,
            LnInst: ln.LnInst,
            DoName: doName,
            DaName: daName,
            Fc: fc);
    }

    private static string FormatFcdaReference(SclReferenceParts parts)
    {
        var ln = $"{parts.Prefix}{parts.LnClass}{parts.LnInst}";
        var data = string.IsNullOrWhiteSpace(parts.DaName) ? parts.DoName : $"{parts.DoName}.{parts.DaName}";
        return $"{parts.LdInst}/{ln}.{data} [{parts.Fc}]";
    }

    private sealed record SclReferenceParts(
        string MmsDomain = "",
        string LdInst = "",
        string LogicalNodeName = "",
        string Prefix = "",
        string LnClass = "",
        string LnInst = "",
        string DoName = "",
        string DaName = "",
        string Fc = "",
        string IedNameOverride = "");

    private sealed class TypeTreeNode
    {
        private readonly Dictionary<string, TypeTreeNode> _children = new(StringComparer.OrdinalIgnoreCase);

        private TypeTreeNode(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public string Name { get; }
        public string Path { get; }
        public string Fc { get; private set; } = string.Empty;
        public string BType { get; private set; } = string.Empty;
        public IReadOnlyCollection<TypeTreeNode> Children => _children.Values;

        public static TypeTreeNode Build(IEnumerable<LiveIedDataAttributeModel> attributes)
        {
            var root = new TypeTreeNode(string.Empty, string.Empty);
            foreach (var attribute in attributes)
                root.Add(attribute);
            return root;
        }

        private void Add(LiveIedDataAttributeModel attribute)
        {
            if (string.IsNullOrWhiteSpace(attribute.AttributePath))
                return;

            var segments = attribute.AttributePath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
                return;

            var current = this;
            var path = string.Empty;
            foreach (var segment in segments)
            {
                path = string.IsNullOrWhiteSpace(path) ? segment : $"{path}_{segment}";
                if (!current._children.TryGetValue(segment, out var child))
                {
                    child = new TypeTreeNode(segment, path);
                    current._children[segment] = child;
                }

                current = child;
            }

            current.Fc = attribute.FunctionalConstraint;
            current.BType = attribute.SclBType;
        }
    }
}
