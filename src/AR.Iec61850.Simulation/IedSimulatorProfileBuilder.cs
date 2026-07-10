using System.Globalization;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Simulation;

/// <summary>
/// Options that steer how an <see cref="IedSimulatorProfile"/> is derived from an SCL document.
/// </summary>
public sealed class IedSimulatorProfileFromSclOptions
{
    /// <summary>Restrict the build to a single IED by name. Empty means the first IED in the document.</summary>
    public string IedName { get; init; } = string.Empty;

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
    public int DataSetMemberCount { get; init; }
    public int SkippedMemberCount { get; init; }
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Builds an <see cref="IedSimulatorProfile"/> from a parsed SCL document so the simulator runtime,
/// the read-only MMS server model, and the live listener can mirror a real station instead of a
/// fixed demo feeder. The bridge is deterministic and clean-room: it interprets DataSet FCDA
/// membership, ReportControl declarations, and CDC/FC semantics only from public IEC 61850 structure.
///
/// Point inventory is taken from the union of DataSet members, which are exactly the signals the
/// station exposes to GOOSE, Sampled Values, and reports. Each non-quality/non-timestamp member
/// becomes a readable point; quality/timestamp members become companion points so DataSet membership
/// resolves one-to-one with no missing-member gaps.
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
        return FromScl(new SclParser().Load(sclPath), options);
    }

    public IedSimulatorProfileFromSclResult FromScl(SclDocument document, IedSimulatorProfileFromSclOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new IedSimulatorProfileFromSclOptions();

        var findings = new List<string>();

        var iedName = ResolveIedName(document, options.IedName, findings);
        var dataSets = document.DataSets
            .Where(ds => MatchesIed(ds.IedName, iedName))
            .ToList();

        if (dataSets.Count == 0)
            findings.Add($"No DataSet definitions were found for IED '{iedName}'. The simulator profile will be structural only.");

        // Collect points from DataSet members, grouped by logical device (iedName+ldInst).
        var deviceBuilders = new Dictionary<string, DeviceBuilder>(StringComparer.OrdinalIgnoreCase);
        var pointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                var deviceName = LogicalDeviceName(entry.IedName.Length > 0 ? entry.IedName : iedName, entry.LdInst);
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
                Reference = ds.Reference,
                Members = ds.Entries
                    .Where(e => options.IncludeQualityAndTimestampPoints || (!e.IsQuality && !e.IsTimestamp))
                    .Select(e => $"{LogicalDeviceName(e.IedName.Length > 0 ? e.IedName : iedName, e.LdInst)}/{RelativeReference(LogicalNodeName(e.Prefix, e.LnClass, e.LnInst), e.DoName, e.DaName)}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .Where(ds => ds.Members.Count > 0)
            .ToList();

        var reportControlBlocks = document.ReportControls
            .Where(rc => MatchesIed(rc.IedName, iedName))
            .Select(rc => new IedSimulatorReportControlBlock
            {
                Reference = rc.ControlBlockReference,
                Buffered = rc.Buffered,
                DataSetReference = rc.DataSetReference,
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

        var ied = document.Ieds.FirstOrDefault(i => MatchesIed(i.Name, iedName));

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
            DataSetMemberCount = memberCount,
            SkippedMemberCount = skipped,
            Findings = findings
        };
    }

    private static IedSimulatorPoint CreatePoint(string relativeReference, SclDataSetEntry entry, IedSimulatorProfileFromSclOptions options)
    {
        if (entry.IsQuality)
            return new IedSimulatorPoint
            {
                Reference = relativeReference,
                FunctionalConstraint = string.IsNullOrWhiteSpace(entry.Fc) ? "MX" : entry.Fc,
                Kind = "quality",
                InitialValue = "valid"
            };

        if (entry.IsTimestamp)
            return new IedSimulatorPoint
            {
                Reference = relativeReference,
                FunctionalConstraint = string.IsNullOrWhiteSpace(entry.Fc) ? "MX" : entry.Fc,
                Kind = "timestamp",
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
                ResolvePhaseDegrees(entry));
        }

        return new IedSimulatorPoint
        {
            Reference = relativeReference,
            FunctionalConstraint = string.IsNullOrWhiteSpace(entry.Fc) ? "ST" : entry.Fc,
            Kind = "status",
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
        var cdc = entry.Cdc;
        if (cdc.Equals("DPS", StringComparison.OrdinalIgnoreCase) || cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) ||
            entry.DoName.Equals("Pos", StringComparison.OrdinalIgnoreCase))
            return "closed";

        if (cdc.Equals("SPS", StringComparison.OrdinalIgnoreCase) || cdc.Equals("SPC", StringComparison.OrdinalIgnoreCase) ||
            cdc.Equals("ACT", StringComparison.OrdinalIgnoreCase) || cdc.Equals("ACD", StringComparison.OrdinalIgnoreCase))
            return "false";

        var bType = NormalizeBType(entry.BType);
        if (bType is "BOOLEAN" or "BOOL")
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
