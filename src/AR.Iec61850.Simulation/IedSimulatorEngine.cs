namespace AR.Iec61850.Simulation;

public sealed class IedSimulatorEngine
{
    private readonly Dictionary<string, IedSimulatorPointState> _states = new(StringComparer.OrdinalIgnoreCase);
    private int _stepIndex;

    public IedSimulatorEngine(IedSimulatorProfile profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Reset();
    }

    public IedSimulatorProfile Profile { get; }
    public bool IsRunning { get; private set; }
    public IReadOnlyCollection<IedSimulatorPointState> PointStates => _states.Values.OrderBy(x => x.Reference, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;

    public void Reset()
    {
        _states.Clear();
        _stepIndex = 0;

        foreach (var point in Profile.LogicalDevices.SelectMany(ld => ld.LogicalNodes).SelectMany(ln => ln.Points))
        {
            _states[point.Reference] = new IedSimulatorPointState
            {
                Reference = point.Reference,
                FunctionalConstraint = point.FunctionalConstraint,
                Kind = point.Kind,
                Unit = point.Unit,
                Value = point.InitialValue,
                Quality = "valid",
                TimestampUtc = DateTimeOffset.UtcNow,
                Reason = "init"
            };
        }
    }

    public IReadOnlyList<IedSimulatorEvent> Step(DateTimeOffset nowUtc)
    {
        _stepIndex++;
        var events = new List<IedSimulatorEvent>();
        var angle = _stepIndex * 0.12;

        foreach (var point in Profile.LogicalDevices.SelectMany(ld => ld.LogicalNodes).SelectMany(ln => ln.Points))
        {
            if (!_states.TryGetValue(point.Reference, out var state))
                continue;

            var previous = state.Value;
            var next = ComputeValue(point, angle);
            var reason = string.Equals(previous, next, StringComparison.Ordinal) ? "sample" : "data-change";

            state.Value = next;
            state.TimestampUtc = nowUtc;
            state.Reason = reason;

            if (!string.Equals(previous, next, StringComparison.Ordinal))
            {
                events.Add(new IedSimulatorEvent
                {
                    TimestampUtc = nowUtc,
                    Reference = point.Reference,
                    FunctionalConstraint = point.FunctionalConstraint,
                    PreviousValue = previous,
                    NewValue = next,
                    Reason = reason
                });
            }
        }

        return events;
    }

    public IedSimulatorSnapshot CreateSnapshot(DateTimeOffset nowUtc)
        => new()
        {
            GeneratedAtUtc = nowUtc,
            ProfileName = Profile.Name,
            LogicalDeviceCount = Profile.LogicalDevices.Count,
            LogicalNodeCount = Profile.LogicalNodeCount,
            PointCount = Profile.PointCount,
            DataSetCount = Profile.DataSets.Count,
            ReportControlBlockCount = Profile.ReportControlBlocks.Count,
            Points = PointStates.ToArray()
        };

    private string ComputeValue(IedSimulatorPoint point, double angle)
    {
        if (string.Equals(point.Kind, "measurement", StringComparison.OrdinalIgnoreCase))
        {
            var radians = angle + point.PhaseDeg * Math.PI / 180.0;
            var value = point.BaseValue + Math.Sin(radians) * point.Amplitude;
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (point.Reference.EndsWith("PTOC1.Str.general", StringComparison.OrdinalIgnoreCase))
            return (_stepIndex % 40) is >= 25 and <= 31 ? "true" : "false";

        if (point.Reference.EndsWith("PTOC1.Op.general", StringComparison.OrdinalIgnoreCase))
            return (_stepIndex % 40) is >= 30 and <= 31 ? "true" : "false";

        if (point.Reference.EndsWith("XCBR1.Pos.stVal", StringComparison.OrdinalIgnoreCase) ||
            point.Reference.EndsWith("CSWI1.Pos.stVal", StringComparison.OrdinalIgnoreCase))
            return (_stepIndex % 80) >= 60 ? "open" : "closed";

        return point.InitialValue;
    }
}

public sealed class IedSimulatorPointState
{
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Quality { get; set; } = "valid";
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = string.Empty;
}

public sealed record IedSimulatorEvent
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string PreviousValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;

    public string Summary => $"{TimestampUtc:HH:mm:ss.fff} {Reference} {PreviousValue} -> {NewValue} ({Reason})";
}

public sealed record IedSimulatorSnapshot
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public int LogicalDeviceCount { get; init; }
    public int LogicalNodeCount { get; init; }
    public int PointCount { get; init; }
    public int DataSetCount { get; init; }
    public int ReportControlBlockCount { get; init; }
    public IReadOnlyList<IedSimulatorPointState> Points { get; init; } = Array.Empty<IedSimulatorPointState>();
}
