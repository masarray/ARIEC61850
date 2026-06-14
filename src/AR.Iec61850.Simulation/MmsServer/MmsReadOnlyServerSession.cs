using System.Globalization;

namespace AR.Iec61850.Simulation;

public enum MmsReadOnlyOperation
{
    GetLogicalDeviceDirectory,
    GetLogicalNodeDirectory,
    GetDataSetDirectory,
    GetReportControlBlockDirectory,
    GetVariableAccessAttributes,
    Read,
    ReadDataSet,
    Write
}

public sealed record MmsReadOnlyServerRequest
{
    public MmsReadOnlyOperation Operation { get; init; }
    public string Target { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed record MmsReadOnlyServerResponse
{
    public bool IsSuccess { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MmsReadOnlyPoint> Values { get; init; } = Array.Empty<MmsReadOnlyPoint>();

    public string Summary => $"{(IsSuccess ? "OK" : "FAIL")} {Operation} {Target}: {Message}";
}

public sealed class MmsReadOnlyServerSession
{
    private readonly Dictionary<string, MmsReadOnlyPoint> _points;
    private readonly Dictionary<string, MmsReadOnlyDataSet> _dataSets;

    public MmsReadOnlyServerSession(MmsReadOnlyServerProfile profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _points = Profile.Points.ToDictionary(x => x.Reference, StringComparer.OrdinalIgnoreCase);
        _dataSets = Profile.DataSets.ToDictionary(x => x.Reference, StringComparer.OrdinalIgnoreCase);
    }

    public MmsReadOnlyServerProfile Profile { get; }

    public MmsReadOnlyServerResponse Handle(MmsReadOnlyServerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Operation switch
        {
            MmsReadOnlyOperation.GetLogicalDeviceDirectory => GetLogicalDeviceDirectory(),
            MmsReadOnlyOperation.GetLogicalNodeDirectory => GetLogicalNodeDirectory(request.Target),
            MmsReadOnlyOperation.GetDataSetDirectory => GetDataSetDirectory(),
            MmsReadOnlyOperation.GetReportControlBlockDirectory => GetReportControlBlockDirectory(),
            MmsReadOnlyOperation.GetVariableAccessAttributes => GetVariableAccessAttributes(request.Target),
            MmsReadOnlyOperation.Read => Read(request.Target),
            MmsReadOnlyOperation.ReadDataSet => ReadDataSet(request.Target),
            MmsReadOnlyOperation.Write => RejectWrite(request.Target),
            _ => Fail(request.Operation.ToString(), request.Target, "Unsupported read-only server operation.")
        };
    }

    public IReadOnlyList<MmsReadOnlySelfTestStep> RunSelfTest()
    {
        var steps = new List<MmsReadOnlySelfTestStep>();
        var firstDevice = Profile.LogicalDevices.FirstOrDefault()?.Name ?? string.Empty;
        var firstPoint = Profile.Points.FirstOrDefault()?.Reference ?? string.Empty;
        var firstDataSet = Profile.DataSets.FirstOrDefault()?.Reference ?? string.Empty;

        AddStep(steps, Handle(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetLogicalDeviceDirectory }));
        if (!string.IsNullOrWhiteSpace(firstDevice))
            AddStep(steps, Handle(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.GetLogicalNodeDirectory, Target = firstDevice }));
        if (!string.IsNullOrWhiteSpace(firstPoint))
            AddStep(steps, Handle(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Read, Target = firstPoint }));
        if (!string.IsNullOrWhiteSpace(firstDataSet))
            AddStep(steps, Handle(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.ReadDataSet, Target = firstDataSet }));

        var writeReject = Handle(new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Write, Target = firstPoint, Value = "test" });
        steps.Add(new MmsReadOnlySelfTestStep
        {
            Operation = writeReject.Operation,
            Target = writeReject.Target,
            IsSuccess = !writeReject.IsSuccess && writeReject.Message.Contains("read-only", StringComparison.OrdinalIgnoreCase),
            Message = writeReject.Message
        });

        return steps.ToArray();
    }

    private static void AddStep(ICollection<MmsReadOnlySelfTestStep> steps, MmsReadOnlyServerResponse response)
        => steps.Add(new MmsReadOnlySelfTestStep
        {
            Operation = response.Operation,
            Target = response.Target,
            IsSuccess = response.IsSuccess,
            Message = response.Message
        });

    private MmsReadOnlyServerResponse GetLogicalDeviceDirectory()
        => Ok(nameof(MmsReadOnlyOperation.GetLogicalDeviceDirectory), string.Empty, $"Returned {Profile.LogicalDevices.Count.ToString(CultureInfo.InvariantCulture)} logical device(s).", Profile.LogicalDevices.Select(x => x.Name).ToArray());

    private MmsReadOnlyServerResponse GetLogicalNodeDirectory(string logicalDevice)
    {
        if (string.IsNullOrWhiteSpace(logicalDevice))
            return Fail(nameof(MmsReadOnlyOperation.GetLogicalNodeDirectory), logicalDevice, "Logical device reference is required.");

        var nodes = Profile.LogicalNodes.Where(x => string.Equals(x.LogicalDevice, logicalDevice, StringComparison.OrdinalIgnoreCase)).Select(x => x.Name).ToArray();
        return nodes.Length == 0
            ? Fail(nameof(MmsReadOnlyOperation.GetLogicalNodeDirectory), logicalDevice, "Logical device not found or has no logical nodes.")
            : Ok(nameof(MmsReadOnlyOperation.GetLogicalNodeDirectory), logicalDevice, $"Returned {nodes.Length.ToString(CultureInfo.InvariantCulture)} logical node(s).", nodes);
    }

    private MmsReadOnlyServerResponse GetDataSetDirectory()
        => Ok(nameof(MmsReadOnlyOperation.GetDataSetDirectory), string.Empty, $"Returned {Profile.DataSets.Count.ToString(CultureInfo.InvariantCulture)} DataSet(s).", Profile.DataSets.Select(x => x.Reference).ToArray());

    private MmsReadOnlyServerResponse GetReportControlBlockDirectory()
        => Ok(nameof(MmsReadOnlyOperation.GetReportControlBlockDirectory), string.Empty, $"Returned {Profile.ReportControlBlocks.Count.ToString(CultureInfo.InvariantCulture)} RCB(s).", Profile.ReportControlBlocks.Select(x => x.Reference).ToArray());

    private MmsReadOnlyServerResponse GetVariableAccessAttributes(string target)
    {
        if (!_points.TryGetValue(target, out var point))
            return Fail(nameof(MmsReadOnlyOperation.GetVariableAccessAttributes), target, "Readable point not found.");

        var items = new[]
        {
            $"fc={point.FunctionalConstraint}",
            $"kind={point.Kind}",
            $"unit={point.Unit}",
            $"quality={point.Quality}"
        };
        return Ok(nameof(MmsReadOnlyOperation.GetVariableAccessAttributes), target, "Returned synthetic variable access attributes.", items);
    }

    private MmsReadOnlyServerResponse Read(string target)
    {
        if (!_points.TryGetValue(target, out var point))
            return Fail(nameof(MmsReadOnlyOperation.Read), target, "Readable point not found.");

        return new MmsReadOnlyServerResponse
        {
            IsSuccess = true,
            Operation = nameof(MmsReadOnlyOperation.Read),
            Target = target,
            Message = $"Returned value {point.Value} quality={point.Quality}.",
            Values = new[] { point }
        };
    }

    private MmsReadOnlyServerResponse ReadDataSet(string target)
    {
        if (!_dataSets.TryGetValue(target, out var dataSet))
            return Fail(nameof(MmsReadOnlyOperation.ReadDataSet), target, "DataSet not found.");

        var values = new List<MmsReadOnlyPoint>();
        var missing = new List<string>();
        foreach (var member in dataSet.Members)
        {
            if (_points.TryGetValue(member, out var point))
                values.Add(point);
            else
                missing.Add(member);
        }

        if (missing.Count > 0)
            return Fail(nameof(MmsReadOnlyOperation.ReadDataSet), target, $"DataSet contains {missing.Count.ToString(CultureInfo.InvariantCulture)} missing member(s): {string.Join(", ", missing.Take(5))}.");

        return new MmsReadOnlyServerResponse
        {
            IsSuccess = true,
            Operation = nameof(MmsReadOnlyOperation.ReadDataSet),
            Target = target,
            Message = $"Returned {values.Count.ToString(CultureInfo.InvariantCulture)} DataSet member value(s).",
            Items = dataSet.Members.ToArray(),
            Values = values.ToArray()
        };
    }

    private static MmsReadOnlyServerResponse RejectWrite(string target)
        => Fail(nameof(MmsReadOnlyOperation.Write), target, "Write operation rejected because this alpha server profile is read-only.");

    private static MmsReadOnlyServerResponse Ok(string operation, string target, string message, IReadOnlyList<string> items)
        => new() { IsSuccess = true, Operation = operation, Target = target, Message = message, Items = items };

    private static MmsReadOnlyServerResponse Fail(string operation, string target, string message)
        => new() { IsSuccess = false, Operation = operation, Target = target, Message = message };
}
