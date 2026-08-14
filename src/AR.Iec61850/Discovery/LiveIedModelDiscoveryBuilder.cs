using AR.Iec61850.Mms;

namespace AR.Iec61850.Discovery;

public sealed class LiveIedModelDiscoveryBuildOptions
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = "AP1";
    public bool IncludeLowConfidenceTemplates { get; init; } = true;
}

public static class LiveIedModelDiscoveryBuilder
{
    public static LiveIedModelDiscoveryDocument Build(
        MmsDiscoveryResult discovery,
        LiveIedModelDiscoveryBuildOptions options,
        IReadOnlyList<MmsDataSetDirectoryResult>? dataSetDirectories = null,
        IReadOnlyList<MmsVariableAccessAttributesResult>? variableTypeAttributes = null,
        IReadOnlyList<MmsFileDirectoryResult>? fileDirectoryPages = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(options);

        var primaryDirectory = discovery.IedDirectory;
        var dataSetDirectoryMap = (dataSetDirectories ?? discovery.DataSetDirectories)
            .Where(x => !string.IsNullOrWhiteSpace(x.DataSetReference))
            .GroupBy(x => x.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var variableTypeList = variableTypeAttributes ?? Array.Empty<MmsVariableAccessAttributesResult>();
        var fileDirectory = BuildFileDirectory(fileDirectoryPages ?? Array.Empty<MmsFileDirectoryResult>());
        var (directory, supplementalPointCount) = BuildEffectiveDirectory(
            primaryDirectory,
            dataSetDirectoryMap.Values,
            discovery.ReportInventory);
        var variableTypeIndex = LiveIedVariableTypeHierarchyIndex.Build(directory, variableTypeList);

        var logicalDevices = BuildLogicalDevices(directory, variableTypeIndex).ToArray();
        var reportControls = BuildReportControls(discovery.ReportInventory).ToArray();
        var controlBlocks = BuildControlBlockInventory(directory).ToArray();
        var dataSets = BuildDataSets(discovery.ReportInventory, dataSetDirectoryMap, reportControls, controlBlocks).ToArray();
        var typeTemplates = BuildTypeTemplates(logicalDevices, options.IncludeLowConfidenceTemplates).ToArray();
        var variableTypes = BuildVariableTypeDiscoveries(variableTypeList).ToArray();
        var warnings = BuildWarnings(
            directory,
            primaryDirectory.PointCount,
            supplementalPointCount,
            dataSets,
            controlBlocks,
            variableTypeList,
            variableTypeIndex,
            fileDirectory).ToArray();
        var coverage = BuildCoverage(logicalDevices, dataSets, reportControls, controlBlocks, variableTypes, fileDirectory);
        var identity = LiveIedIdentityResolver.Resolve(
            directory.LogicalDevices.Keys,
            options.Host,
            options.IedName,
            BuildFallbackIedName(options.Host));

        return new LiveIedModelDiscoveryDocument
        {
            Host = options.Host.Trim(),
            Port = options.Port <= 0 ? 102 : options.Port,
            IedName = identity.IedName,
            IedIdentity = identity,
            AccessPointName = string.IsNullOrWhiteSpace(options.AccessPointName) ? "AP1" : options.AccessPointName.Trim(),
            LogicalDevices = logicalDevices,
            FileDirectory = fileDirectory,
            DataSets = dataSets,
            ReportControls = reportControls,
            GooseControlBlocks = controlBlocks.Where(x => string.Equals(x.Kind, "GSEControl", StringComparison.OrdinalIgnoreCase)).ToArray(),
            SampledValueControlBlocks = controlBlocks.Where(x => string.Equals(x.Kind, "SampledValueControl", StringComparison.OrdinalIgnoreCase)).ToArray(),
            SettingGroupControls = controlBlocks.Where(x => string.Equals(x.Kind, "SettingGroupControl", StringComparison.OrdinalIgnoreCase)).ToArray(),
            LogControls = controlBlocks.Where(x => string.Equals(x.Kind, "LogControl", StringComparison.OrdinalIgnoreCase)).ToArray(),
            TypeTemplates = typeTemplates,
            VariableTypeDiscoveries = variableTypes,
            Coverage = coverage,
            Warnings = warnings,
            Summary = FormatSummary(coverage)
        };
    }

    private static (MmsIedModelDirectory Directory, int SupplementalPointCount) BuildEffectiveDirectory(
        MmsIedModelDirectory primaryDirectory,
        IEnumerable<MmsDataSetDirectoryResult> dataSetDirectories,
        MmsReportInventory reportInventory)
    {
        var points = new List<MmsFcResolvedPoint>(primaryDirectory.Points);
        var knownMmsReferences = new HashSet<string>(
            primaryDirectory.Points.Select(point => point.MmsReference),
            StringComparer.OrdinalIgnoreCase);
        var supplementalPointCount = 0;

        foreach (var member in dataSetDirectories
                     .Where(directory => directory.IsSuccess)
                     .SelectMany(directory => directory.Members))
        {
            var domain = member.Domain?.Trim() ?? string.Empty;
            var itemName = member.MmsItemName?.Trim() ?? string.Empty;
            var logicalNode = member.LogicalNode?.Trim() ?? string.Empty;
            var functionalConstraint = MmsFunctionalConstraint.Normalize(member.FunctionalConstraint);
            var dataObjectPath = member.DataObjectPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain) ||
                string.IsNullOrWhiteSpace(itemName) ||
                string.IsNullOrWhiteSpace(logicalNode) ||
                string.IsNullOrWhiteSpace(functionalConstraint) ||
                string.IsNullOrWhiteSpace(dataObjectPath))
                continue;

            var point = new MmsFcResolvedPoint
            {
                Domain = domain,
                LogicalNode = logicalNode,
                FunctionalConstraint = functionalConstraint,
                DataObjectPath = dataObjectPath,
                MmsItemName = itemName,
                Source = "GetNamedVariableListAttributes",
                Confidence = member.Confidence
            };
            if (!knownMmsReferences.Add(point.MmsReference))
                continue;

            points.Add(point);
            supplementalPointCount++;
        }

        foreach (var reportControl in reportInventory.ReportControls)
        {
            var domain = reportControl.Domain?.Trim() ?? string.Empty;
            var logicalNode = reportControl.LogicalNode?.Trim() ?? string.Empty;
            var name = reportControl.Name?.Trim() ?? string.Empty;
            var functionalConstraint = reportControl.Buffered ? "BR" : "RP";
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(logicalNode) || string.IsNullOrWhiteSpace(name))
                continue;

            var point = new MmsFcResolvedPoint
            {
                Domain = domain,
                LogicalNode = logicalNode,
                FunctionalConstraint = functionalConstraint,
                DataObjectPath = $"{name}.RptEna",
                MmsItemName = $"{logicalNode}${functionalConstraint}${name}$RptEna",
                Source = "ReportControlInventory",
                Confidence = 100
            };
            if (!knownMmsReferences.Add(point.MmsReference))
                continue;

            points.Add(point);
            supplementalPointCount++;
        }

        return (new MmsIedModelDirectory(points), supplementalPointCount);
    }

    private static IEnumerable<LiveIedLogicalDeviceModel> BuildLogicalDevices(
        MmsIedModelDirectory directory,
        LiveIedVariableTypeHierarchyIndex variableTypeIndex)
    {
        foreach (var ld in directory.LogicalDevices.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield return new LiveIedLogicalDeviceModel
            {
                MmsDomain = ld.Name,
                Inst = ld.Name,
                LogicalNodes = BuildLogicalNodes(ld, variableTypeIndex).ToArray()
            };
        }
    }

    private static IEnumerable<LiveIedLogicalNodeModel> BuildLogicalNodes(
        MmsLogicalDeviceDirectory ld,
        LiveIedVariableTypeHierarchyIndex variableTypeIndex)
    {
        foreach (var ln in ld.LogicalNodes.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var parsed = Iec61850ReferenceParts.ParseLogicalNodeName(ln.Name);
            var dataObjects = BuildDataObjects(ln, parsed, variableTypeIndex).ToArray();
            yield return new LiveIedLogicalNodeModel
            {
                Name = ln.Name,
                Prefix = parsed.Prefix,
                LnClass = parsed.SclLnClass,
                LnInst = parsed.LnInst,
                ProposedLnTypeId = $"LN_{Iec61850ReferenceParts.SafeIdPart(parsed.SclLnClass)}_{Iec61850ReferenceParts.SafeIdPart(ln.Name)}",
                FunctionalConstraintCounts = ln.CountByFunctionalConstraint(),
                DataObjects = dataObjects
            };
        }
    }

    private static IEnumerable<LiveIedDataObjectModel> BuildDataObjects(
        MmsLogicalNodeDirectory ln,
        Iec61850LogicalNodeName parsedLn,
        LiveIedVariableTypeHierarchyIndex variableTypeIndex)
    {
        var groups = ln.Points
            .GroupBy(x => Iec61850ReferenceParts.TopDataObjectName(x.DataObjectPath), StringComparer.OrdinalIgnoreCase)
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var attributes = group
                .Where(point => !string.IsNullOrWhiteSpace(Iec61850ReferenceParts.DataAttributePath(point.DataObjectPath)))
                .Select(point => BuildAttribute(point, group.Key, variableTypeIndex))
                .OrderBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.AttributePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var attrPaths = attributes.Select(x => x.AttributePath).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            var fcs = group.Select(x => x.FunctionalConstraint).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            var cdc = CdcInferenceEngine.Infer(parsedLn.SclLnClass, group.Key, attrPaths, fcs);
            var reference = $"{ln.Domain}/{ln.Name}.{group.Key}";
            yield return new LiveIedDataObjectModel
            {
                Reference = reference,
                Name = group.Key,
                ProposedDoTypeId = $"DO_{Iec61850ReferenceParts.SafeIdPart(cdc.Cdc)}_{Iec61850ReferenceParts.SafeIdPart(parsedLn.SclLnClass)}_{Iec61850ReferenceParts.SafeIdPart(group.Key)}",
                InferredCdc = cdc.Cdc,
                CdcConfidence = cdc.Confidence,
                ConfidenceLevel = cdc.Level,
                Evidence = cdc.Evidence,
                Attributes = attributes
            };
        }
    }

    private static LiveIedDataAttributeModel BuildAttribute(
        MmsFcResolvedPoint point,
        string dataObjectName,
        LiveIedVariableTypeHierarchyIndex variableTypeIndex)
    {
        var attrPath = Iec61850ReferenceParts.DataAttributePath(point.DataObjectPath);
        if (string.IsNullOrWhiteSpace(attrPath) && !string.Equals(point.DataObjectPath, dataObjectName, StringComparison.OrdinalIgnoreCase))
            attrPath = point.DataObjectPath;

        var hasExactType = variableTypeIndex.TryResolve(point, out var typeResult);
        var sclBType = hasExactType ? typeResult.TypeSpecification.SclBType : GuessSclBType(attrPath, point.FunctionalConstraint);
        var mmsType = hasExactType ? typeResult.TypeSpecification.MmsType : string.Empty;
        var signature = hasExactType ? typeResult.TypeSpecification.Signature : string.Empty;

        return new LiveIedDataAttributeModel
        {
            ObjectReference = point.UserReference,
            AttributePath = attrPath,
            FunctionalConstraint = point.FunctionalConstraint,
            MmsReference = point.MmsReference,
            MmsItemName = point.MmsItemName,
            Source = point.Source,
            SclBType = sclBType,
            MmsType = mmsType,
            MmsTypeSignature = signature,
            TypeDiscoveryStatus = hasExactType ? "Exact" : "NotRead",
            TypeDiscoveryMessage = hasExactType ? typeResult.Message : string.Empty,
            TypeSource = hasExactType ? typeResult.Source : "NameListHeuristic",
            TypeConfidence = hasExactType ? LiveIedDiscoveryConfidenceLevel.Exact : LiveIedDiscoveryConfidenceLevel.Low,
            FunctionalConstraintConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
    }

    private static LiveIedFileDirectoryModel BuildFileDirectory(IReadOnlyList<MmsFileDirectoryResult> pages)
    {
        if (pages.Count == 0)
            return new LiveIedFileDirectoryModel();

        var successfulPages = pages.Where(page => page.IsSuccess).ToArray();
        var last = pages.Last();
        return new LiveIedFileDirectoryModel
        {
            Attempted = true,
            IsSuccess = successfulPages.Length > 0,
            DirectoryName = pages.FirstOrDefault(page => !string.IsNullOrWhiteSpace(page.DirectoryName))?.DirectoryName ?? string.Empty,
            PageCount = pages.Count,
            Message = last.Message,
            Entries = successfulPages
                .SelectMany(page => page.Entries)
                .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new LiveIedFileModel
                {
                    Name = entry.Name,
                    Path = entry.Path,
                    SizeBytes = entry.SizeBytes,
                    LastModified = entry.LastModifiedDisplay,
                    IsLikelyDirectory = entry.IsLikelyDirectory
                })
                .ToArray()
        };
    }

    private static IEnumerable<LiveIedReportControlModel> BuildReportControls(MmsReportInventory inventory)
    {
        foreach (var rcb in inventory.ReportControls.OrderBy(x => x.Domain, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Reference, StringComparer.OrdinalIgnoreCase))
        {
            yield return new LiveIedReportControlModel
            {
                Reference = rcb.Reference,
                Domain = rcb.Domain,
                LogicalNode = rcb.LogicalNode,
                Name = rcb.Name,
                Buffered = rcb.Buffered,
                DataSetReference = NormalizeDataSetReference(rcb.DataSetReference),
                ReportId = rcb.ReportId,
                ConfRev = rcb.ConfRev,
                TriggerOptions = rcb.TriggerOptions,
                OptionalFields = rcb.OptionalFields,
                BufferTimeMs = rcb.BufferTimeMs,
                IntegrityPeriodMs = rcb.IntegrityPeriodMs,
                EnabledState = rcb.EnabledState,
                ReservationState = rcb.ReservationState,
                ReservationTimeSeconds = rcb.ReservationTimeSeconds,
                Status = rcb.Status
            };
        }
    }

    private static IEnumerable<LiveIedDataSetModel> BuildDataSets(
        MmsReportInventory inventory,
        IReadOnlyDictionary<string, MmsDataSetDirectoryResult> directoryMap,
        IReadOnlyList<LiveIedReportControlModel> reportControls,
        IReadOnlyList<LiveIedControlBlockModel> controlBlocks)
    {
        var candidates = inventory.DataSets
            .GroupBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            directoryMap.TryGetValue(candidate.Reference, out var directory);
            var usedByReports = reportControls
                .Where(x => string.Equals(x.DataSetReference, candidate.Reference, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Reference)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var usedByGoose = controlBlocks
                .Where(x => string.Equals(x.Kind, "GSEControl", StringComparison.OrdinalIgnoreCase) && string.Equals(x.DataSetReference, candidate.Reference, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Reference)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var usedBySv = controlBlocks
                .Where(x => string.Equals(x.Kind, "SampledValueControl", StringComparison.OrdinalIgnoreCase) && string.Equals(x.DataSetReference, candidate.Reference, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Reference)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var members = directory?.Members
                .Select((member, index) => new LiveIedDataSetMemberModel
                {
                    Index = index,
                    Reference = member.UserReference,
                    FunctionalConstraint = member.FunctionalConstraint,
                    MmsReference = member.MmsReference,
                    Confidence = member.Confidence >= 100 ? LiveIedDiscoveryConfidenceLevel.Exact : LiveIedDiscoveryConfidenceLevel.Medium
                })
                .ToArray() ?? Array.Empty<LiveIedDataSetMemberModel>();

            yield return new LiveIedDataSetModel
            {
                Reference = candidate.Reference,
                Domain = candidate.Domain,
                LogicalNode = candidate.LogicalNode,
                Name = candidate.Name,
                IsDeletable = directory?.IsDeletable,
                MemberCount = directory?.Members.Count ?? 0,
                Members = members,
                UsedByReportControls = usedByReports,
                UsedByGooseControls = usedByGoose,
                UsedBySampledValueControls = usedBySv
            };
        }
    }

    private static IEnumerable<LiveIedControlBlockModel> BuildControlBlockInventory(MmsIedModelDirectory directory)
    {
        foreach (var fcGroup in directory.Points.GroupBy(x => x.FunctionalConstraint, StringComparer.OrdinalIgnoreCase))
        {
            if (!CanContainControlBlock(fcGroup.Key))
                continue;

            var blockGroups = fcGroup
                .GroupBy(x => $"{x.Domain}/{x.LogicalNode}.{Iec61850ReferenceParts.TopDataObjectName(x.DataObjectPath)}", StringComparer.OrdinalIgnoreCase)
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !x.Key.EndsWith(".", StringComparison.Ordinal))
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var blockGroup in blockGroups)
            {
                var first = blockGroup.First();
                var name = Iec61850ReferenceParts.TopDataObjectName(first.DataObjectPath);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var kind = ClassifyControlBlock(first.FunctionalConstraint, name);
                if (string.IsNullOrWhiteSpace(kind))
                    continue;

                var attributes = blockGroup
                    .Select(x => Iec61850ReferenceParts.DataAttributePath(x.DataObjectPath))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                yield return new LiveIedControlBlockModel
                {
                    Kind = kind,
                    Reference = $"{first.Domain}/{first.LogicalNode}.{first.FunctionalConstraint}.{name}",
                    Domain = first.Domain,
                    LogicalNode = first.LogicalNode,
                    Name = name,
                    FunctionalConstraint = first.FunctionalConstraint,
                    AttributeCount = attributes.Length,
                    Attributes = attributes,
                    DataSetReferenceStatus = attributes.Any(x => string.Equals(x, "DatSet", StringComparison.OrdinalIgnoreCase)) ? "AttributePresentValueNotRead" : "AttributeNotPresentInNameList",
                    AddressStatus = HasAnyAttribute(attributes, "DstAddress", "Addr", "APPID", "MAC-Address") ? "AddressAttributesPresentValueNotRead" : "NotDiscovered",
                    DiscoveryStatus = "AttributeInventoryOnly",
                    Message = BuildControlBlockInventoryMessage(kind, attributes)
                };
            }
        }
    }

    private static bool CanContainControlBlock(string functionalConstraint)
        => functionalConstraint.ToUpperInvariant() is "GO" or "MS" or "US" or "SG" or "SE" or "SP" or "LG";

    private static string ClassifyControlBlock(string functionalConstraint, string dataObjectName)
        => functionalConstraint.ToUpperInvariant() switch
        {
            "GO" => "GSEControl",
            "MS" or "US" => "SampledValueControl",
            "SG" or "SE" => "SettingGroupControl",
            "SP" when string.Equals(dataObjectName, "SGCB", StringComparison.OrdinalIgnoreCase) => "SettingGroupControl",
            "LG" => "LogControl",
            _ => string.Empty
        };

    private static bool HasAnyAttribute(IEnumerable<string> attributes, params string[] names)
        => attributes.Any(attribute => names.Any(name => string.Equals(attribute, name, StringComparison.OrdinalIgnoreCase)));

    private static string BuildControlBlockInventoryMessage(string kind, IReadOnlyList<string> attributes)
    {
        var hasDatSet = attributes.Any(x => string.Equals(x, "DatSet", StringComparison.OrdinalIgnoreCase));
        var hasConfRev = attributes.Any(x => string.Equals(x, "ConfRev", StringComparison.OrdinalIgnoreCase));
        var hasEnable = attributes.Any(x => x.EndsWith("Ena", StringComparison.OrdinalIgnoreCase));
        return $"{kind} discovered from live FC attribute names. Attribute values are not read in this phase. DatSetAttr={(hasDatSet ? "yes" : "no")}, ConfRevAttr={(hasConfRev ? "yes" : "no")}, enableAttr={(hasEnable ? "yes" : "no")}.";
    }

    private static IEnumerable<LiveIedTypeTemplateCandidate> BuildTypeTemplates(
        IReadOnlyList<LiveIedLogicalDeviceModel> logicalDevices,
        bool includeLowConfidenceTemplates)
    {
        var logicalNodes = logicalDevices.SelectMany(x => x.LogicalNodes).ToArray();
        foreach (var ln in logicalNodes)
        {
            yield return new LiveIedTypeTemplateCandidate
            {
                TemplateKind = "LNodeType",
                Id = ln.ProposedLnTypeId,
                SourceReference = ln.Name,
                InferredType = ln.LnClass,
                Confidence = 1.0,
                Members = ln.DataObjects.Select(x => x.Name).ToArray()
            };
        }

        foreach (var dataObject in logicalNodes.SelectMany(x => x.DataObjects))
        {
            if (!includeLowConfidenceTemplates && dataObject.ConfidenceLevel is LiveIedDiscoveryConfidenceLevel.Low or LiveIedDiscoveryConfidenceLevel.Unknown)
                continue;

            yield return new LiveIedTypeTemplateCandidate
            {
                TemplateKind = "DOType",
                Id = dataObject.ProposedDoTypeId,
                SourceReference = dataObject.Reference,
                InferredType = dataObject.InferredCdc,
                Confidence = dataObject.CdcConfidence,
                Members = dataObject.Attributes.Select(x => FormatTemplateMember(x)).ToArray()
            };
        }
    }


    private static IEnumerable<LiveIedVariableTypeDiscoveryModel> BuildVariableTypeDiscoveries(
        IReadOnlyList<MmsVariableAccessAttributesResult> variableTypeAttributes)
    {
        foreach (var result in variableTypeAttributes.OrderBy(x => x.ReferenceKey, StringComparer.OrdinalIgnoreCase))
        {
            yield return new LiveIedVariableTypeDiscoveryModel
            {
                Reference = result.Reference.ToString(),
                Domain = result.Reference.Domain,
                MmsItemName = result.Reference.Item,
                FunctionalConstraint = result.Reference.FunctionalConstraint,
                IsSuccess = result.IsSuccess,
                MmsType = result.MmsType,
                SclBType = result.SclBType,
                TypeSignature = result.TypeSignature,
                IsMmsDeletable = result.IsMmsDeletable,
                Message = result.Message,
                Source = result.Source
            };
        }
    }

    private static string FormatTemplateMember(LiveIedDataAttributeModel attribute)
    {
        var type = string.IsNullOrWhiteSpace(attribute.MmsType) ? attribute.SclBType : $"{attribute.SclBType}/{attribute.MmsType}";
        var source = attribute.TypeConfidence == LiveIedDiscoveryConfidenceLevel.Exact ? "exact" : "heuristic";
        return $"{attribute.AttributePath} [{attribute.FunctionalConstraint}] {type} ({source})";
    }

    private static IEnumerable<LiveIedDiscoveryWarning> BuildWarnings(
        MmsIedModelDirectory directory,
        int primaryPointCount,
        int supplementalPointCount,
        IReadOnlyList<LiveIedDataSetModel> dataSets,
        IReadOnlyList<LiveIedControlBlockModel> controlBlocks,
        IReadOnlyList<MmsVariableAccessAttributesResult> variableTypeAttributes,
        LiveIedVariableTypeHierarchyIndex variableTypeIndex,
        LiveIedFileDirectoryModel fileDirectory)
    {
        if (directory.PointCount == 0)
        {
            yield return new LiveIedDiscoveryWarning
            {
                Code = "NO_FC_POINTS",
                Message = "No FC points were parsed from MMS GetNameList. Deep discovery cannot build a useful SCL model."
            };
        }

        if (supplementalPointCount > 0)
        {
            yield return new LiveIedDiscoveryWarning
            {
                Code = "MODEL_AUGMENTED_FROM_SECONDARY_MMS_EVIDENCE",
                Message = primaryPointCount == 0
                    ? $"GetNameList produced no FC points. The model was recovered with {supplementalPointCount} exact point(s) from DataSet member and ReportControl discovery. Objects outside that evidence remain unavailable."
                    : $"The model was augmented with {supplementalPointCount} exact point(s) from DataSet member and ReportControl discovery that were absent from the primary GetNameList directory."
            };
        }

        foreach (var dataSet in dataSets.Where(x => x.MemberCount == 0))
        {
            yield return new LiveIedDiscoveryWarning
            {
                Code = "DATASET_MEMBERS_NOT_READ",
                Reference = dataSet.Reference,
                Message = "DataSet exists but member directory was not read in this run. Use --read-datasets true for better SCL FCDA export."
            };
        }

        if (controlBlocks.Count > 0)
        {
            yield return new LiveIedDiscoveryWarning
            {
                Code = "CONTROL_BLOCK_VALUE_READ_PENDING",
                Message = "GO/SV/SG/LG control blocks were inventoried from live FC attribute names. Attribute values such as DatSet, GoID, svID, APPID, and multicast address are not read yet and remain companion evidence until the deep value reader is implemented."
            };
        }

        if (variableTypeAttributes.Count > 0 && variableTypeAttributes.All(x => !x.IsSuccess))
        {
            yield return new LiveIedDiscoveryWarning
            {
                Code = "VARIABLE_ACCESS_ATTRIBUTES_UNSUPPORTED_OR_FAILED",
                Message = "GetVariableAccessAttributes was attempted but no variable type specification was decoded. The model remains usable with FC/name-based type inference, but SCL DataTypeTemplates will be less accurate."
            };
        }

        if (variableTypeAttributes.Any(x => x.IsSuccess && x.TypeSpecification is not null) &&
            variableTypeIndex.ResolvedAttributeCount == 0)
        {
            yield return new LiveIedDiscoveryWarning
            {
                Code = "VARIABLE_TYPE_HIERARCHY_UNMAPPED",
                Message = "GetVariableAccessAttributes returned type specifications, but none could be mapped to live FC/DA paths. The export preserves the directory model and marks unresolved types as heuristic."
            };
        }

        if (fileDirectory.Attempted && !fileDirectory.IsSuccess)
        {
            yield return new LiveIedDiscoveryWarning
            {
                Code = "FILE_DIRECTORY_UNAVAILABLE",
                Message = string.IsNullOrWhiteSpace(fileDirectory.Message)
                    ? "MMS FileDirectory was attempted but the IED did not return a usable directory listing."
                    : $"MMS FileDirectory was attempted but did not complete: {fileDirectory.Message}"
            };
        }
    }

    private static LiveIedModelDiscoveryCoverage BuildCoverage(
        IReadOnlyList<LiveIedLogicalDeviceModel> logicalDevices,
        IReadOnlyList<LiveIedDataSetModel> dataSets,
        IReadOnlyList<LiveIedReportControlModel> reportControls,
        IReadOnlyList<LiveIedControlBlockModel> controlBlocks,
        IReadOnlyList<LiveIedVariableTypeDiscoveryModel> variableTypes,
        LiveIedFileDirectoryModel fileDirectory)
    {
        var logicalNodes = logicalDevices.SelectMany(x => x.LogicalNodes).ToArray();
        var dataObjects = logicalNodes.SelectMany(x => x.DataObjects).ToArray();
        var dataAttributes = dataObjects.SelectMany(x => x.Attributes).ToArray();
        return new LiveIedModelDiscoveryCoverage
        {
            LogicalDeviceCount = logicalDevices.Count,
            LogicalNodeCount = logicalNodes.Length,
            DataObjectCount = dataObjects.Length,
            DataAttributeCount = dataAttributes.Length,
            ExactFunctionalConstraintCount = dataAttributes.Count(x => x.FunctionalConstraintConfidence == LiveIedDiscoveryConfidenceLevel.Exact),
            HighConfidenceCdcCount = dataObjects.Count(x => x.ConfidenceLevel == LiveIedDiscoveryConfidenceLevel.High),
            MediumConfidenceCdcCount = dataObjects.Count(x => x.ConfidenceLevel == LiveIedDiscoveryConfidenceLevel.Medium),
            LowConfidenceCdcCount = dataObjects.Count(x => x.ConfidenceLevel == LiveIedDiscoveryConfidenceLevel.Low),
            UnknownCdcCount = dataObjects.Count(x => x.ConfidenceLevel == LiveIedDiscoveryConfidenceLevel.Unknown),
            DataSetCount = dataSets.Count,
            FileCount = fileDirectory.Entries.Count,
            VariableTypeReadAttemptCount = variableTypes.Count,
            VariableTypeReadSuccessCount = variableTypes.Count(x => x.IsSuccess),
            VariableTypeReadFailureCount = variableTypes.Count(x => !x.IsSuccess),
            ExactMmsTypeCount = dataAttributes.Count(x => x.TypeConfidence == LiveIedDiscoveryConfidenceLevel.Exact),
            ReportControlCount = reportControls.Count,
            BufferedReportControlCount = reportControls.Count(x => x.Buffered),
            UnbufferedReportControlCount = reportControls.Count(x => !x.Buffered),
            GooseControlBlockCount = controlBlocks.Count(x => string.Equals(x.Kind, "GSEControl", StringComparison.OrdinalIgnoreCase)),
            SampledValueControlBlockCount = controlBlocks.Count(x => string.Equals(x.Kind, "SampledValueControl", StringComparison.OrdinalIgnoreCase)),
            SettingGroupControlCount = controlBlocks.Count(x => string.Equals(x.Kind, "SettingGroupControl", StringComparison.OrdinalIgnoreCase)),
            LogControlCount = controlBlocks.Count(x => string.Equals(x.Kind, "LogControl", StringComparison.OrdinalIgnoreCase))
        };
    }

    private static string FormatSummary(LiveIedModelDiscoveryCoverage coverage)
        => $"Live IED model: LD={coverage.LogicalDeviceCount}, LN={coverage.LogicalNodeCount}, DO={coverage.DataObjectCount}, DA={coverage.DataAttributeCount}, DataSets={coverage.DataSetCount}, RCB={coverage.ReportControlCount}, GoCB={coverage.GooseControlBlockCount}, SVCB={coverage.SampledValueControlBlockCount}, SGCB={coverage.SettingGroupControlCount}, LCB={coverage.LogControlCount}.";

    private static string BuildFallbackIedName(string host)
        => string.IsNullOrWhiteSpace(host) ? "DISCOVERED_IED" : "IED_" + Iec61850ReferenceParts.SafeIdPart(host);

    private static string NormalizeDataSetReference(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('$', '.');

    private static string GuessSclBType(string attributePath, string functionalConstraint)
    {
        var name = string.IsNullOrWhiteSpace(attributePath) ? string.Empty : attributePath.Split('.').Last();
        if (string.Equals(name, "q", StringComparison.OrdinalIgnoreCase))
            return "Quality";
        if (string.Equals(name, "t", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Tm", StringComparison.OrdinalIgnoreCase))
            return "Timestamp";
        if (string.Equals(name, "stVal", StringComparison.OrdinalIgnoreCase))
            return "BOOLEAN";
        if (string.Equals(name, "f", StringComparison.OrdinalIgnoreCase))
            return "FLOAT32";
        if (string.Equals(name, "i", StringComparison.OrdinalIgnoreCase))
            return "INT32";
        if (string.Equals(functionalConstraint, "CO", StringComparison.OrdinalIgnoreCase))
            return "Struct";
        return "Unknown";
    }
}
