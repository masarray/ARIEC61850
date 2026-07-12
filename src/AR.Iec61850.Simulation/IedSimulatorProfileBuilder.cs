using System.Globalization;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Simulation;

/// <summary>
/// Options that steer how an <see cref="IedSimulatorProfile"/> is derived from an SCL document.
/// </summary>
public sealed class IedSimulatorProfileFromSclOptions
{
    /// <summary>Restrict the build to a single IED by name. Empty means the first IED in the document.</summary>
    public string IedName { get; init; } = string.Empty;

    /// <summary>
    /// Runtime IED identity exposed by the simulator. When omitted for an ICD
    /// whose IED name is TEMPLATE, the simulator instantiates the template with
    /// the SCL filename stem.
    /// </summary>
    public string RuntimeIedName { get; init; } = string.Empty;

    /// <summary>Include quality (<c>q</c>) and timestamp (<c>t</c>) DataSet members as readable points.</summary>
    public bool IncludeQualityAndTimestampPoints { get; init; } = true;

    /// <summary>Nominal system frequency used to seed measurement runtime behavior.</summary>
    public double NominalFrequencyHz { get; init; } = 50.0;
}

/// <summary>
/// Result of an SCL-to-simulator build, including the derived profile and any clean-room findings
/// raised while interpreting the engineering model.
/// </summary>
public sealed class IedSimulatorProfileFromSclResult
{
    public IedSimulatorProfile Profile { get; init; } = new();
    public string SelectedIedName { get; init; } = string.Empty;
    public string SourceIedName { get; init; } = string.Empty;
    public int DataSetMemberCount { get; init; }
    public int StructuralDataAttributeCount { get; init; }
    public int SkippedMemberCount { get; init; }
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Builds an <see cref="IedSimulatorProfile"/> from a parsed SCL document so the simulator runtime,
/// the read-only MMS server model, and the live listener can mirror a real station instead of a
/// fixed demo feeder. The bridge is deterministic and clean-room: it interprets DataSet FCDA
/// membership, ReportControl declarations, and CDC/FC semantics only from public IEC 61850 structure.
///
/// The SCL DataTypeTemplates projection is the primary source for the complete LD/LN/DO/DA model.
/// DataSet membership enriches that model with ordered service bindings; it is not allowed to shrink
/// the model to only the signals referenced by DataSets.
/// </summary>
public sealed class IedSimulatorProfileBuilder
{
    private static readonly HashSet<string> MeasurementCdcs = new(StringComparer.OrdinalIgnoreCase)
    {
        "MV", "CMV", "WYE", "DEL", "SEQ", "SAV", "ASG", "BCR", "HMV", "HWYE", "HDEL"
    };

    public IedSimulatorProfileFromSclResult FromScl(string sclPath, IedSimulatorProfileFromSclOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sclPath);
        var document = new SclParser().Load(sclPath);
        var structuralModel = SclLiveModelProjectionBuilder.Load(sclPath);
        return FromScl(document, structuralModel, options);
    }

    public IedSimulatorProfileFromSclResult FromScl(SclDocument document, IedSimulatorProfileFromSclOptions? options = null)
        => FromSclCore(document, structuralModel: null, options);

    public IedSimulatorProfileFromSclResult FromScl(
        SclDocument document,
        LiveIedModelDiscoveryDocument structuralModel,
        IedSimulatorProfileFromSclOptions? options = null)
        => FromSclCore(document, structuralModel, options);

    private IedSimulatorProfileFromSclResult FromSclCore(
        SclDocument document,
        LiveIedModelDiscoveryDocument? structuralModel,
        IedSimulatorProfileFromSclOptions? options)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new IedSimulatorProfileFromSclOptions();

        var findings = new List<string>();

        var sourceIedName = ResolveIedName(document, options.IedName, findings);
        var iedName = ResolveRuntimeIedName(document, sourceIedName, options.RuntimeIedName, findings);
        var dataSets = document.DataSets
            .Where(ds => MatchesIed(ds.IedName, sourceIedName))
            .ToList();

        if (dataSets.Count == 0)
            findings.Add($"No DataSet definitions were found for IED '{sourceIedName}'. The simulator profile will expose its structural SCL model only.");

        // The full SCL type projection seeds every LD/LN/DO/DA. DataSet FCDA members are
        // then added only when they expose a leaf absent from the type projection.
        var deviceBuilders = new Dictionary<string, DeviceBuilder>(StringComparer.OrdinalIgnoreCase);
        var pointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var structuralDataAttributeCount = structuralModel is null
            ? 0
            : AddStructuralModelPoints(structuralModel, sourceIedName, iedName, deviceBuilders, pointKeys, options, findings);
        var memberCount = 0;
        var skipped = 0;

        foreach (var dataSet in dataSets)
        {
            foreach (var entry in dataSet.Entries)
            {
                memberCount++;

                if (!options.IncludeQualityAndTimestampPoints && (entry.IsQuality || entry.IsTimestamp))
                {
                    skipped++;
                    continue;
                }

                var entryIedName = RuntimeIedName(entry.IedName.Length > 0 ? entry.IedName : sourceIedName, sourceIedName, iedName);
                var deviceName = LogicalDeviceName(entryIedName, entry.LdInst);
                var lnName = LogicalNodeName(entry.Prefix, entry.LnClass, entry.LnInst);
                var relativeReference = RelativeReference(lnName, entry.DoName, entry.DaName);
                var fullReference = $"{deviceName}/{relativeReference}";

                if (!pointKeys.Add(fullReference))
                    continue; // members repeat across DataSets; keep one point per unique reference.

                if (!deviceBuilders.TryGetValue(deviceName, out var device))
                {
                    device = new DeviceBuilder(deviceName);
                    deviceBuilders[deviceName] = device;
                }

                var node = device.GetOrAddNode(lnName, entry.LnClass);
                node.Points.Add(CreatePoint(relativeReference, entry, options));
            }
        }

        var logicalDevices = deviceBuilders.Values
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => d.Build())
            .ToList();

        var simulatorDataSets = dataSets
            .Select(ds => new IedSimulatorDataSet
            {
                Reference = RemapIedReference(ds.Reference, sourceIedName, iedName),
                Members = ds.Entries
                    .Where(e => options.IncludeQualityAndTimestampPoints || (!e.IsQuality && !e.IsTimestamp))
                    .Select(e => $"{LogicalDeviceName(RuntimeIedName(e.IedName.Length > 0 ? e.IedName : sourceIedName, sourceIedName, iedName), e.LdInst)}/{RelativeReference(LogicalNodeName(e.Prefix, e.LnClass, e.LnInst), e.DoName, e.DaName)}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .Where(ds => ds.Members.Count > 0)
            .ToList();

        var reportControlBlocks = document.ReportControls
            .Where(rc => MatchesIed(rc.IedName, sourceIedName))
            .Select(rc => new IedSimulatorReportControlBlock
            {
                Reference = RemapIedReference(rc.ControlBlockReference, sourceIedName, iedName),
                Buffered = rc.Buffered,
                DataSetReference = RemapIedReference(rc.DataSetReference, sourceIedName, iedName),
                ReportId = string.IsNullOrWhiteSpace(rc.ReportId) ? rc.Name : rc.ReportId,
                ConfRev = (int)rc.ConfigurationRevision,
                BufferTimeMs = (int)rc.BufferTimeMilliseconds,
                IntegrityPeriodMs = (int)rc.IntegrityPeriodMilliseconds,
                TriggerOptions = rc.Buffered ? "data-change, quality-change, integrity, GI" : "data-change, quality-change, GI",
                OptionalFields = rc.Buffered
                    ? "seqNum, entryId, timeStamp, reasonCode, dataSet, confRev"
                    : "seqNum, timeStamp, reasonCode, dataSet, confRev"
            })
            .OrderBy(rc => rc.Reference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ied = document.Ieds.FirstOrDefault(i => MatchesIed(i.Name, sourceIedName));

        var profile = new IedSimulatorProfile
        {
            Name = string.IsNullOrWhiteSpace(iedName) ? "SCL IED" : iedName,
            Vendor = string.IsNullOrWhiteSpace(ied?.Manufacturer) ? "ARIEC61850" : ied!.Manufacturer,
            Edition = $"Imported from SCL ({DescribeEdition(document.Edition)})",
            LogicalDevices = logicalDevices,
            DataSets = simulatorDataSets,
            ReportControlBlocks = reportControlBlocks
        };

        if (logicalDevices.Count == 0)
            findings.Add("The SCL document produced no logical devices with readable points.");

        // Surface report DataSets that do not resolve to a derived DataSet reference.
        var dataSetReferences = new HashSet<string>(simulatorDataSets.Select(d => d.Reference), StringComparer.OrdinalIgnoreCase);
        foreach (var rcb in reportControlBlocks.Where(r => !string.IsNullOrWhiteSpace(r.DataSetReference) && !dataSetReferences.Contains(r.DataSetReference)))
            findings.Add($"ReportControl '{rcb.Reference}' references DataSet '{rcb.DataSetReference}' that was not found among IED DataSets.");

        return new IedSimulatorProfileFromSclResult
        {
            Profile = profile,
            SelectedIedName = iedName,
            SourceIedName = sourceIedName,
            DataSetMemberCount = memberCount,
            StructuralDataAttributeCount = structuralDataAttributeCount,
            SkippedMemberCount = skipped,
            Findings = findings
        };
    }

    private static int AddStructuralModelPoints(
        LiveIedModelDiscoveryDocument structuralModel,
        string sourceIedName,
        string runtimeIedName,
        IDictionary<string, DeviceBuilder> deviceBuilders,
        ISet<string> pointKeys,
        IedSimulatorProfileFromSclOptions options,
        ICollection<string> findings)
    {
        var added = 0;
        var missingFunctionalConstraint = 0;

        foreach (var sourceDevice in structuralModel.LogicalDevices
                     .Where(device => MatchesIedDomain(device.MmsDomain, sourceIedName))
                     .OrderBy(device => device.MmsDomain, StringComparer.OrdinalIgnoreCase))
        {
            var deviceName = RemapMmsDomain(sourceDevice.MmsDomain, sourceIedName, runtimeIedName);
            if (!deviceBuilders.TryGetValue(deviceName, out var device))
            {
                device = new DeviceBuilder(deviceName);
                deviceBuilders[deviceName] = device;
            }

            foreach (var sourceNode in sourceDevice.LogicalNodes.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase))
            {
                var node = device.GetOrAddNode(sourceNode.Name, sourceNode.LnClass);
                foreach (var dataObject in sourceNode.DataObjects)
                {
                    foreach (var attribute in dataObject.Attributes)
                    {
                        var functionalConstraint = attribute.FunctionalConstraint.Trim();
                        if (string.IsNullOrWhiteSpace(functionalConstraint))
                        {
                            missingFunctionalConstraint++;
                            continue;
                        }

                        var relativeReference = RelativeReference(sourceNode.Name, dataObject.Name, attribute.AttributePath);
                        var fullReference = $"{deviceName}/{relativeReference}";
                        if (!pointKeys.Add(fullReference))
                            continue;

                        var attributePath = attribute.AttributePath;
                        node.Points.Add(CreatePoint(relativeReference, new SclDataSetEntry
                        {
                            IedName = runtimeIedName,
                            LdInst = sourceDevice.Inst,
                            LnClass = sourceNode.LnClass,
                            LnInst = sourceNode.LnInst,
                            DoName = dataObject.Name,
                            DaName = attributePath,
                            Fc = functionalConstraint,
                            Cdc = dataObject.InferredCdc,
                            BType = attribute.SclBType,
                            IsQuality = attributePath.EndsWith("q", StringComparison.OrdinalIgnoreCase),
                            IsTimestamp = attributePath.EndsWith("t", StringComparison.OrdinalIgnoreCase)
                        }, options));
                        added++;
                    }
                }
            }
        }

        if (missingFunctionalConstraint > 0)
            findings.Add($"Skipped {missingFunctionalConstraint.ToString(CultureInfo.InvariantCulture)} SCL data attribute(s) without a functional constraint; their logical nodes remain present in the server model.");

        return added;
    }

    private static IedSimulatorPoint CreatePoint(string relativeReference, SclDataSetEntry entry, IedSimulatorProfileFromSclOptions options)
    {
        if (entry.IsQuality)
            return new IedSimulatorPoint
            {
                Reference = relativeReference,
                FunctionalConstraint = string.IsNullOrWhiteSpace(entry.Fc) ? "MX" : entry.Fc,
                Kind = "quality",
                SclBType = entry.BType,
                InitialValue = "valid"
            };

        if (entry.IsTimestamp)
            return new IedSimulatorPoint
            {
                Reference = relativeReference,
                FunctionalConstraint = string.IsNullOrWhiteSpace(entry.Fc) ? "MX" : entry.Fc,
                Kind = "timestamp",
                SclBType = entry.BType,
                InitialValue = "0"
            };

        if (IsMeasurement(entry))
        {
            var (baseValue, amplitude, unit) = ResolveMeasurementShape(entry);
            return IedSimulatorPoint.Measurement(
                relativeReference,
                string.IsNullOrWhiteSpace(entry.Fc) ? "MX" : entry.Fc,
                unit,
                baseValue,
                amplitude,
                ResolvePhaseDegrees(entry),
                IsDynamicMeasurement(entry),
                entry.BType);
        }

        return new IedSimulatorPoint
        {
            Reference = relativeReference,
            FunctionalConstraint = string.IsNullOrWhiteSpace(entry.Fc) ? "ST" : entry.Fc,
            Kind = "status",
            SclBType = entry.BType,
            InitialValue = ResolveStatusInitialValue(entry)
        };
    }

    private static bool IsMeasurement(SclDataSetEntry entry)
    {
        if (string.Equals(entry.Fc, "MX", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(entry.Cdc) && MeasurementCdcs.Contains(entry.Cdc))
            return true;

        var bType = NormalizeBType(entry.BType);
        return string.Equals(entry.Fc, "MX", StringComparison.OrdinalIgnoreCase) &&
               bType is "FLOAT32" or "FLOAT64" or "INT32" or "INT16";
    }

    private static bool IsDynamicMeasurement(SclDataSetEntry entry)
        => entry.DaName.EndsWith("cVal.mag.f", StringComparison.OrdinalIgnoreCase) ||
           entry.DaName.EndsWith("instMag.f", StringComparison.OrdinalIgnoreCase);

    private static (double BaseValue, double Amplitude, string Unit) ResolveMeasurementShape(SclDataSetEntry entry)
    {
        var isVoltage = IsVoltage(entry);
        var isCurrent = IsCurrent(entry);
        var isInteger = NormalizeBType(entry.BType) is "INT8" or "INT16" or "INT32" or "INT64" or "INT8U" or "INT16U" or "INT24U" or "INT32U";

        if (isVoltage)
            return isInteger ? (0, 100_000, "V") : (230_000, 1_500, "V");

        if (isCurrent)
            return isInteger ? (0, 10_000, "A") : (240, 18, "A");

        return isInteger ? (0, 1_000, string.Empty) : (0, 1, string.Empty);
    }

    private static bool IsVoltage(SclDataSetEntry entry)
        => entry.LnClass.Equals("TVTR", StringComparison.OrdinalIgnoreCase) ||
           entry.DoName.Contains("Vol", StringComparison.OrdinalIgnoreCase) ||
           entry.DoName.Equals("PhV", StringComparison.OrdinalIgnoreCase) ||
           entry.DoName.Equals("PPV", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrent(SclDataSetEntry entry)
        => entry.LnClass.Equals("TCTR", StringComparison.OrdinalIgnoreCase) ||
           entry.DoName.Contains("Amp", StringComparison.OrdinalIgnoreCase) ||
           entry.DoName.Equals("A", StringComparison.OrdinalIgnoreCase);

    private static double ResolvePhaseDegrees(SclDataSetEntry entry)
    {
        var doName = entry.DoName;
        if (doName.EndsWith("phsA", StringComparison.OrdinalIgnoreCase) || doName.Contains(".phsA", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (doName.EndsWith("phsB", StringComparison.OrdinalIgnoreCase) || doName.Contains(".phsB", StringComparison.OrdinalIgnoreCase))
            return -120;
        if (doName.EndsWith("phsC", StringComparison.OrdinalIgnoreCase) || doName.Contains(".phsC", StringComparison.OrdinalIgnoreCase))
            return 120;

        return entry.LnInst switch
        {
            "1" => 0,
            "2" => -120,
            "3" => 120,
            _ => 0
        };
    }

    private static string ResolveStatusInitialValue(SclDataSetEntry entry)
    {
        var bType = NormalizeBType(entry.BType);
        if (bType is "DBPOS" or "TCMD")
            return "closed";

        if (bType == "ENUM" || bType.StartsWith("INT", StringComparison.Ordinal) || bType.StartsWith("FLOAT", StringComparison.Ordinal))
            return "0";

        if (bType is "BOOLEAN" or "BOOL")
            return entry.DoName.Equals("Pos", StringComparison.OrdinalIgnoreCase) &&
                   entry.DaName.EndsWith("stVal", StringComparison.OrdinalIgnoreCase)
                ? "true"
                : "false";

        if (bType.StartsWith("VISSTRING", StringComparison.Ordinal) || bType.StartsWith("UNICODE", StringComparison.Ordinal) ||
            bType.StartsWith("MMSSTRING", StringComparison.Ordinal) || bType.StartsWith("OCTET", StringComparison.Ordinal) || bType == "OBJREF")
            return string.Empty;

        var cdc = entry.Cdc;
        if (cdc.Equals("DPS", StringComparison.OrdinalIgnoreCase) || cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) ||
            entry.DoName.Equals("Pos", StringComparison.OrdinalIgnoreCase))
            return "closed";

        if (cdc.Equals("SPS", StringComparison.OrdinalIgnoreCase) || cdc.Equals("SPC", StringComparison.OrdinalIgnoreCase) ||
            cdc.Equals("ACT", StringComparison.OrdinalIgnoreCase) || cdc.Equals("ACD", StringComparison.OrdinalIgnoreCase))
            return "false";

        return "0";
    }

    private static string ResolveIedName(SclDocument document, string requested, List<string> findings)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (document.Ieds.Any(i => MatchesIed(i.Name, requested)) ||
                document.DataSets.Any(ds => MatchesIed(ds.IedName, requested)))
                return requested.Trim();

            findings.Add($"Requested IED '{requested}' was not found in the SCL document; falling back to the first available IED.");
        }

        var firstIed = document.Ieds.FirstOrDefault()?.Name;
        if (!string.IsNullOrWhiteSpace(firstIed))
            return firstIed!;

        var firstDataSetIed = document.DataSets.Select(ds => ds.IedName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        return firstDataSetIed ?? string.Empty;
    }

    private static string ResolveRuntimeIedName(SclDocument document, string sourceIedName, string requestedRuntimeIedName, ICollection<string> findings)
    {
        if (!string.IsNullOrWhiteSpace(requestedRuntimeIedName))
            return requestedRuntimeIedName.Trim();

        if (!sourceIedName.Equals("TEMPLATE", StringComparison.OrdinalIgnoreCase))
            return sourceIedName;

        var sourceFileName = Path.GetFileNameWithoutExtension(document.SourceName);
        if (!string.IsNullOrWhiteSpace(sourceFileName) && !sourceFileName.Equals("TEMPLATE", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add($"SCL IED '{sourceIedName}' is a generic ICD template; the simulator instantiated it as runtime IED '{sourceFileName}'.");
            return sourceFileName;
        }

        return sourceIedName;
    }

    private static bool MatchesIedDomain(string domain, string sourceIedName)
        => !string.IsNullOrWhiteSpace(domain) &&
           (domain.Equals(sourceIedName, StringComparison.OrdinalIgnoreCase) ||
            domain.StartsWith(sourceIedName, StringComparison.OrdinalIgnoreCase));

    private static string RuntimeIedName(string candidateIedName, string sourceIedName, string runtimeIedName)
        => MatchesIed(candidateIedName, sourceIedName) ? runtimeIedName : candidateIedName;

    private static string RemapIedReference(string reference, string sourceIedName, string runtimeIedName)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var slash = reference.IndexOf('/');
        if (slash <= 0)
            return reference;

        var domain = reference[..slash];
        return $"{RemapMmsDomain(domain, sourceIedName, runtimeIedName)}{reference[slash..]}";
    }

    private static string RemapMmsDomain(string domain, string sourceIedName, string runtimeIedName)
    {
        if (sourceIedName.Equals(runtimeIedName, StringComparison.OrdinalIgnoreCase) ||
            !MatchesIedDomain(domain, sourceIedName))
            return domain;

        return runtimeIedName + domain[sourceIedName.Length..];
    }

    private static bool MatchesIed(string candidate, string iedName)
        => string.IsNullOrWhiteSpace(iedName) ||
           string.IsNullOrWhiteSpace(candidate) ||
           string.Equals(candidate, iedName, StringComparison.OrdinalIgnoreCase);

    private static string LogicalDeviceName(string iedName, string ldInst) => $"{iedName}{ldInst}";

    private static string LogicalNodeName(string prefix, string lnClass, string lnInst) => $"{prefix}{lnClass}{lnInst}";

    private static string RelativeReference(string lnName, string doName, string daName)
    {
        var data = string.IsNullOrWhiteSpace(daName) ? doName : $"{doName}.{daName}";
        return string.IsNullOrWhiteSpace(data) ? lnName : $"{lnName}.{data}";
    }

    private static string NormalizeBType(string bType)
        => new((bType ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string DescribeEdition(SclEdition edition) => edition.ToString();

    private sealed class DeviceBuilder
    {
        private readonly Dictionary<string, NodeBuilder> _nodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<NodeBuilder> _order = new();

        public DeviceBuilder(string name) => Name = name;

        public string Name { get; }

        public NodeBuilder GetOrAddNode(string name, string lnClass)
        {
            if (_nodes.TryGetValue(name, out var node))
                return node;

            node = new NodeBuilder(name, lnClass);
            _nodes[name] = node;
            _order.Add(node);
            return node;
        }

        public IedSimulatorLogicalDevice Build()
            => new()
            {
                Name = Name,
                LogicalNodes = _order.Select(n => n.Build()).ToArray()
            };
    }

    private sealed class NodeBuilder
    {
        public NodeBuilder(string name, string lnClass)
        {
            Name = name;
            LnClass = string.IsNullOrWhiteSpace(lnClass) ? InferLnClass(name) : lnClass;
        }

        public string Name { get; }
        public string LnClass { get; }
        public List<IedSimulatorPoint> Points { get; } = new();

        public IedSimulatorLogicalNode Build()
            => new()
            {
                Name = Name,
                LnClass = LnClass,
                Points = Points.ToArray()
            };

        private static string InferLnClass(string name)
            => new(name.TakeWhile(char.IsLetter).ToArray());
    }
}
