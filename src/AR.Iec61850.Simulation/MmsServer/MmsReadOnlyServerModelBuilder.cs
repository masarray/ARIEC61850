using System.Globalization;

namespace AR.Iec61850.Simulation;

public sealed class MmsReadOnlyServerModelBuilder
{
    public MmsReadOnlyServerProfile Build(IedSimulatorProfile simulatorProfile, IedSimulatorSnapshot? snapshot = null, MmsReadOnlyServerProfileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(simulatorProfile);
        options ??= new MmsReadOnlyServerProfileOptions();
        snapshot ??= new IedSimulatorEngine(simulatorProfile).CreateSnapshot(DateTimeOffset.UtcNow);

        var pointStates = snapshot.Points.ToDictionary(x => x.Reference, StringComparer.OrdinalIgnoreCase);
        var points = new List<MmsReadOnlyPoint>();
        var logicalDevices = new List<MmsReadOnlyLogicalDevice>();
        var logicalNodes = new List<MmsReadOnlyLogicalNode>();
        var diagnostics = new List<MmsReadOnlyDiagnostic>();

        foreach (var device in simulatorProfile.LogicalDevices)
        {
            var devicePointCount = 0;

            foreach (var node in device.LogicalNodes)
            {
                logicalNodes.Add(new MmsReadOnlyLogicalNode
                {
                    LogicalDevice = device.Name,
                    Name = node.Name,
                    LnClass = node.LnClass,
                    PointCount = node.Points.Count
                });

                devicePointCount += node.Points.Count;

                foreach (var point in node.Points)
                {
                    pointStates.TryGetValue(point.Reference, out var state);
                    points.Add(new MmsReadOnlyPoint
                    {
                        Reference = ToFullReference(device.Name, point.Reference),
                        LogicalDevice = device.Name,
                        LogicalNode = node.Name,
                        FunctionalConstraint = point.FunctionalConstraint,
                        Kind = point.Kind,
                        Unit = point.Unit,
                        Value = state?.Value ?? point.InitialValue,
                        Quality = state?.Quality ?? "valid",
                        TimestampUtc = state?.TimestampUtc ?? DateTimeOffset.UtcNow
                    });
                }
            }

            logicalDevices.Add(new MmsReadOnlyLogicalDevice
            {
                Name = device.Name,
                LogicalNodeCount = device.LogicalNodes.Count,
                PointCount = devicePointCount
            });
        }

        if (logicalDevices.Count == 0)
            diagnostics.Add(High("NO_LOGICAL_DEVICE", "The simulator profile does not expose any logical device."));

        if (points.Count == 0)
            diagnostics.Add(High("NO_POINTS", "The simulator profile does not expose any readable data point."));

        var pointIndex = points.ToDictionary(x => x.Reference, StringComparer.OrdinalIgnoreCase);
        var dataSets = simulatorProfile.DataSets.Select(dataSet =>
        {
            var missing = dataSet.Members.Where(member => !pointIndex.ContainsKey(member)).ToArray();
            var status = missing.Length == 0 ? "OK" : $"Missing {missing.Length.ToString(CultureInfo.InvariantCulture)} member(s)";
            if (missing.Length > 0)
            {
                diagnostics.Add(new MmsReadOnlyDiagnostic
                {
                    Severity = "Warning",
                    Code = "DATASET_MEMBER_MISSING",
                    Message = $"DataSet {dataSet.Reference} references missing member(s): {string.Join(", ", missing.Take(5))}."
                });
            }

            return new MmsReadOnlyDataSet
            {
                Reference = dataSet.Reference,
                Members = dataSet.Members.ToArray(),
                Status = status
            };
        }).ToArray();

        var dataSetIndex = dataSets.ToDictionary(x => x.Reference, StringComparer.OrdinalIgnoreCase);
        var rcbs = simulatorProfile.ReportControlBlocks.Select(rcb =>
        {
            var status = dataSetIndex.ContainsKey(rcb.DataSetReference) ? "OK" : "DataSet missing";
            if (!dataSetIndex.ContainsKey(rcb.DataSetReference))
            {
                diagnostics.Add(new MmsReadOnlyDiagnostic
                {
                    Severity = "Warning",
                    Code = "RCB_DATASET_MISSING",
                    Message = $"RCB {rcb.Reference} points to missing DataSet {rcb.DataSetReference}."
                });
            }

            return new MmsReadOnlyReportControlBlock
            {
                Reference = rcb.Reference,
                Mode = rcb.Mode,
                Buffered = rcb.Buffered,
                DataSetReference = rcb.DataSetReference,
                ReportId = rcb.ReportId,
                ConfRev = rcb.ConfRev,
                BufferTimeMs = rcb.BufferTimeMs,
                IntegrityPeriodMs = rcb.IntegrityPeriodMs,
                TriggerOptions = rcb.TriggerOptions,
                OptionalFields = rcb.OptionalFields,
                Status = status
            };
        }).ToArray();

        var profile = new MmsReadOnlyServerProfile
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            ServerName = options.ServerName,
            Port = options.Port,
            LogicalDevices = logicalDevices.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            LogicalNodes = logicalNodes.OrderBy(x => x.LogicalDevice, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            Points = points.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase).ToArray(),
            DataSets = dataSets.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase).ToArray(),
            ReportControlBlocks = rcbs.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase).ToArray(),
            Diagnostics = diagnostics.ToArray()
        };

        return options.IncludeSelfTest ? profile with { SelfTestSteps = new MmsReadOnlyServerSession(profile).RunSelfTest() } : profile;
    }

    private static string ToFullReference(string logicalDevice, string pointReference)
    {
        if (pointReference.Contains('/', StringComparison.Ordinal))
            return pointReference;

        return $"{logicalDevice}/{pointReference}";
    }

    private static MmsReadOnlyDiagnostic High(string code, string message)
        => new() { Severity = "High", Code = code, Message = message };
}
