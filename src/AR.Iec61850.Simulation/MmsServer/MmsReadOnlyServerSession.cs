using System.Globalization;

namespace AR.Iec61850.Simulation;

public enum MmsReadOnlyOperation
{
    GetLogicalDeviceDirectory,
    GetLogicalNodeDirectory,
    GetNamedVariableDirectory,
    GetDataSetDirectory,
    GetReportControlBlockDirectory,
    GetFileDirectory,
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
    public string ContinueAfter { get; init; } = string.Empty;
}

public sealed record MmsReadOnlyServerResponse
{
    public bool IsSuccess { get; init; }
    public string Operation { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MmsReadOnlyPoint> Values { get; init; } = Array.Empty<MmsReadOnlyPoint>();
    public bool MoreFollows { get; init; }

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
            MmsReadOnlyOperation.GetNamedVariableDirectory => GetNamedVariableDirectory(request.Target, request.ContinueAfter),
            MmsReadOnlyOperation.GetDataSetDirectory => GetDataSetDirectory(request.Target, request.ContinueAfter),
            MmsReadOnlyOperation.GetReportControlBlockDirectory => GetReportControlBlockDirectory(),
            MmsReadOnlyOperation.GetFileDirectory => GetFileDirectory(request.Target),
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

    private MmsReadOnlyServerResponse GetNamedVariableDirectory(string logicalDevice, string continueAfter)
    {
        if (string.IsNullOrWhiteSpace(logicalDevice))
            return Fail(nameof(MmsReadOnlyOperation.GetNamedVariableDirectory), logicalDevice, "Logical device reference is required.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in Profile.Points.Where(x => string.Equals(x.LogicalDevice, logicalDevice, StringComparison.OrdinalIgnoreCase)))
        {
            var mmsName = ToMmsNamedVariableReference(point);
            var directoryName = ToMmsDirectoryItemName(mmsName);
            var parts = directoryName.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
                names.Add(parts[0]);
            if (parts.Length > 1)
                names.Add(string.Join('$', parts.Take(2)));
            names.Add(directoryName);
        }

        foreach (var rcb in Profile.ReportControlBlocks.Where(x => IsReferenceInLogicalDevice(x.Reference, logicalDevice)))
        {
            var mmsName = ToMmsControlBlockReference(rcb.Reference);
            var directoryName = ToMmsDirectoryItemName(mmsName);
            var parts = directoryName.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
                names.Add(parts[0]);
            if (parts.Length > 1)
                names.Add(string.Join('$', parts.Take(2)));
            names.Add(directoryName);
        }

        var orderedNames = names
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return orderedNames.Length == 0
            ? Fail(nameof(MmsReadOnlyOperation.GetNamedVariableDirectory), logicalDevice, "Logical device not found or has no named variables.")
            : Page(
                nameof(MmsReadOnlyOperation.GetNamedVariableDirectory),
                logicalDevice,
                orderedNames,
                continueAfter,
                "named variable(s), including logical-node and functional-constraint hierarchy");
    }

    private MmsReadOnlyServerResponse GetDataSetDirectory(string logicalDevice = "", string continueAfter = "")
    {
        var dataSets = string.IsNullOrWhiteSpace(logicalDevice)
            ? Profile.DataSets.ToArray()
            : Profile.DataSets.Where(x => x.Reference.StartsWith($"{logicalDevice}/", StringComparison.OrdinalIgnoreCase)).ToArray();

        return Page(
            nameof(MmsReadOnlyOperation.GetDataSetDirectory),
            logicalDevice,
            dataSets.Select(x => x.Reference).ToArray(),
            continueAfter,
            "DataSet(s)");
    }

    private MmsReadOnlyServerResponse GetReportControlBlockDirectory()
        => Ok(nameof(MmsReadOnlyOperation.GetReportControlBlockDirectory), string.Empty, $"Returned {Profile.ReportControlBlocks.Count.ToString(CultureInfo.InvariantCulture)} RCB(s).", Profile.ReportControlBlocks.Select(x => x.Reference).ToArray());

    private MmsReadOnlyServerResponse GetFileDirectory(string fileSpecification)
        => new()
        {
            IsSuccess = true,
            Operation = nameof(MmsReadOnlyOperation.GetFileDirectory),
            Target = fileSpecification,
            Message = "Returned empty virtual file directory; no file service entries are configured in this read-only simulator.",
            Items = Array.Empty<string>()
        };

    private MmsReadOnlyServerResponse GetVariableAccessAttributes(string target)
    {
        if (!_points.TryGetValue(target, out var point))
        {
            point = BuildHierarchyType(target) ?? BuildReportControlBlockType(target) ?? ResolvePointWithoutDomain(target);
            if (point == null && !IsKnownHierarchyReference(target))
            {
                // MMS permits VMD-specific ObjectName values. They are not the
                // normal IEC 61850 LD/LN path, but IED browsers may probe one
                // while building their initial type catalogue. Keep the
                // read-only association alive and return a conservative
                // structure specification instead of dropping the socket.
                if (target.Contains('/', StringComparison.Ordinal) || string.IsNullOrWhiteSpace(target))
                    return Fail(nameof(MmsReadOnlyOperation.GetVariableAccessAttributes), target, "Readable point not found.");

                point = new MmsReadOnlyPoint
                {
                    Reference = target,
                    Kind = "structure",
                    FunctionalConstraint = string.Empty,
                    Value = "structure",
                    Quality = "valid"
                };
            }

            if (point == null)
            {
                point = new MmsReadOnlyPoint
                {
                    Reference = target,
                    LogicalDevice = target[..target.IndexOf('/', StringComparison.Ordinal)],
                    Kind = "structure",
                    FunctionalConstraint = "",
                    Value = "structure",
                    Quality = "valid"
                };
            }
        }

        var items = new[]
        {
            $"fc={point.FunctionalConstraint}",
            $"kind={point.Kind}",
            $"unit={point.Unit}",
            $"quality={point.Quality}"
        };
        return new MmsReadOnlyServerResponse
        {
            IsSuccess = true,
            Operation = nameof(MmsReadOnlyOperation.GetVariableAccessAttributes),
            Target = target,
            Message = "Returned synthetic variable access attributes.",
            Items = items,
            Values = new[] { point }
        };
    }

    private MmsReadOnlyPoint? BuildHierarchyType(string target)
    {
        var slash = target.IndexOf('/');
        if (slash <= 0 || slash == target.Length - 1)
            return null;

        var domain = target[..slash];
        var item = target[(slash + 1)..].Replace('.', '$');
        var requestedParts = item.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (requestedParts.Length == 0)
            return null;

        var candidates = Profile.Points
            .Where(point => string.Equals(point.LogicalDevice, domain, StringComparison.OrdinalIgnoreCase))
            .Select(point => new MmsTypeCandidate(point, MmsItemParts(point)))
            .Where(x => x.Parts.Length >= requestedParts.Length && x.Parts.Take(requestedParts.Length).SequenceEqual(requestedParts, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 0)
            return null;

        return BuildHierarchyNode(
            reference: target,
            name: requestedParts[^1],
            functionalConstraint: candidates[0].Point.FunctionalConstraint,
            candidates,
            requestedParts.Length);
    }

    private MmsReadOnlyPoint? BuildReportControlBlockType(string target)
    {
        var slash = target.IndexOf('/');
        if (slash <= 0 || slash == target.Length - 1)
            return null;

        var logicalDevice = target[..slash];
        var item = target[(slash + 1)..].Replace('.', '$');
        var candidates = Profile.ReportControlBlocks
            .Where(rcb => IsReferenceInLogicalDevice(rcb.Reference, logicalDevice))
            .Where(rcb =>
            {
                var mmsName = ToMmsControlBlockReference(rcb.Reference);
                return mmsName.StartsWith(item + "$", StringComparison.OrdinalIgnoreCase) ||
                       item.StartsWith(mmsName + "$", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(mmsName, item, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
        if (candidates.Length == 0)
            return null;

        var exact = candidates.FirstOrDefault(rcb => string.Equals(ToMmsControlBlockReference(rcb.Reference), item, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return BuildReportControlBlockNode(logicalDevice, item, exact);

        var containing = candidates
            .Where(rcb => item.StartsWith(ToMmsControlBlockReference(rcb.Reference) + "$", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(rcb => ToMmsControlBlockReference(rcb.Reference).Length)
            .FirstOrDefault();
        if (containing is not null)
        {
            var mmsName = ToMmsControlBlockReference(containing.Reference);
            var descendantPath = item[(mmsName.Length + 1)..]
                .Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return ResolveReportControlBlockChild(BuildReportControlBlockNode(logicalDevice, mmsName, containing), descendantPath);
        }

        var parts = item.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var functionalConstraint = parts.Length > 1 ? parts[1] : string.Empty;
        return new MmsReadOnlyPoint
        {
            Name = parts.LastOrDefault() ?? item,
            Reference = target,
            LogicalDevice = logicalDevice,
            LogicalNode = parts.FirstOrDefault() ?? string.Empty,
            FunctionalConstraint = functionalConstraint,
            Kind = "structure",
            Value = "structure",
            Quality = "valid",
            Children = candidates
                .OrderBy(rcb => rcb.Reference, StringComparer.OrdinalIgnoreCase)
                .Select(rcb => BuildReportControlBlockNode(logicalDevice, ToMmsControlBlockReference(rcb.Reference), rcb))
                .ToArray()
        };
    }

    private static MmsReadOnlyPoint? ResolveReportControlBlockChild(MmsReadOnlyPoint parent, IReadOnlyList<string> path)
    {
        var current = parent;
        foreach (var part in path)
        {
            var child = current.Children.FirstOrDefault(candidate => string.Equals(candidate.Name, part, StringComparison.OrdinalIgnoreCase));
            if (child is null)
                return null;

            current = child;
        }

        return current;
    }

    private static MmsReadOnlyPoint BuildReportControlBlockNode(string logicalDevice, string mmsName, MmsReadOnlyReportControlBlock rcb)
    {
        var parts = mmsName.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var functionalConstraint = parts.Length > 1 ? parts[1] : string.Empty;
        var reference = $"{logicalDevice}/{mmsName}";
        return new MmsReadOnlyPoint
        {
            Name = parts.LastOrDefault() ?? mmsName,
            Reference = reference,
            LogicalDevice = logicalDevice,
            LogicalNode = parts.FirstOrDefault() ?? string.Empty,
            FunctionalConstraint = functionalConstraint,
            Kind = "structure",
            Value = "structure",
            Quality = "valid",
            Children =
            [
                ReportAttribute(reference, "RptID", rcb.ReportId),
                ReportAttribute(reference, "RptEna", "false"),
                ReportAttribute(reference, "DatSet", rcb.DataSetReference),
                ReportAttribute(reference, "ConfRev", rcb.ConfRev.ToString(CultureInfo.InvariantCulture)),
                ReportAttribute(reference, "BufTm", rcb.BufferTimeMs.ToString(CultureInfo.InvariantCulture)),
                ReportAttribute(reference, "IntgPd", rcb.IntegrityPeriodMs.ToString(CultureInfo.InvariantCulture)),
                ReportAttribute(reference, "GI", "false")
            ]
        };
    }

    private static MmsReadOnlyPoint ReportAttribute(string parentReference, string name, string value)
        => new()
        {
            Name = name,
            Reference = $"{parentReference}${name}",
            Kind = "report-attribute",
            Value = value ?? string.Empty,
            Quality = "valid"
        };

    private static MmsReadOnlyPoint BuildHierarchyNode(
        string reference,
        string name,
        string functionalConstraint,
        IReadOnlyList<MmsTypeCandidate> candidates,
        int nextPartIndex)
    {
        var exact = candidates.Where(x => x.Parts.Length == nextPartIndex).ToArray();
        var groups = candidates
            .Where(x => x.Parts.Length > nextPartIndex)
            .GroupBy(x => x.Parts[nextPartIndex], StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (groups.Length == 0 && exact.Length == 1)
            return exact[0].Point with { Name = name };

        var children = groups
            .Select(group => BuildHierarchyNode(
                reference + "$" + group.Key,
                group.Key,
                functionalConstraint,
                group.ToArray(),
                nextPartIndex + 1))
            .ToArray();

        return new MmsReadOnlyPoint
        {
            Name = name,
            Reference = reference,
            LogicalDevice = candidates[0].Point.LogicalDevice,
            LogicalNode = candidates[0].Point.LogicalNode,
            FunctionalConstraint = functionalConstraint,
            Kind = "structure",
            Value = "structure",
            Quality = "valid",
            Children = children
        };
    }

    private static string[] MmsItemParts(MmsReadOnlyPoint point)
    {
        var mmsReference = ToMmsNamedVariableReference(point);
        var slash = mmsReference.IndexOf('/');
        var item = slash >= 0 ? mmsReference[(slash + 1)..] : mmsReference;
        return item.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private MmsReadOnlyPoint? ResolvePointWithoutDomain(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Contains('/', StringComparison.Ordinal))
            return null;

        var normalized = target.Replace('.', '$');
        var matches = Profile.Points
            .Where(x =>
            {
                var item = ToMmsNamedVariableReference(x);
                var slash = item.IndexOf('/');
                var relative = slash >= 0 ? item[(slash + 1)..] : item;
                return string.Equals(relative, normalized, StringComparison.OrdinalIgnoreCase) ||
                       relative.EndsWith("$" + normalized, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private bool IsKnownHierarchyReference(string target)
    {
        var slash = target.IndexOf('/');
        if (slash <= 0 || slash == target.Length - 1)
            return false;

        var logicalDevice = target[..slash];
        var item = target[(slash + 1)..];
        var mmsItem = item.Replace('.', '$');
        if (Profile.LogicalNodes.Any(x =>
                string.Equals(x.LogicalDevice, logicalDevice, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(x.Name, mmsItem, StringComparison.OrdinalIgnoreCase) || mmsItem.StartsWith(x.Name + "$", StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        return Profile.Points.Any(x =>
            string.Equals(x.LogicalDevice, logicalDevice, StringComparison.OrdinalIgnoreCase) &&
            ToMmsNamedVariableReference(x).StartsWith(logicalDevice + "/" + mmsItem + "$", StringComparison.OrdinalIgnoreCase));
    }

    private MmsReadOnlyServerResponse Read(string target)
    {
        if (!_points.TryGetValue(target, out var point))
            point = BuildHierarchyType(target) ?? BuildReportControlBlockType(target);

        if (point is null)
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
        var resolvedTarget = target;
        if (!_dataSets.TryGetValue(resolvedTarget, out var dataSet))
        {
            var targetSlash = target.IndexOf('/');
            var requestedName = targetSlash >= 0 && targetSlash < target.Length - 1
                ? target[(targetSlash + 1)..].Replace('.', '$')
                : target.Replace('.', '$');
            var matches = Profile.DataSets
                .Where(x => DataSetReferenceMatches(x.Reference, target, requestedName))
                .ToArray();
            if (matches.Length == 0)
                return Fail(nameof(MmsReadOnlyOperation.ReadDataSet), target, "DataSet not found.");
            if (matches.Length > 1)
                return Fail(nameof(MmsReadOnlyOperation.ReadDataSet), target, "DataSet reference is ambiguous across logical devices.");

            dataSet = matches[0];
            resolvedTarget = dataSet.Reference;
        }

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
            Target = resolvedTarget,
            Message = $"Returned {values.Count.ToString(CultureInfo.InvariantCulture)} DataSet member value(s).",
            Items = values.Select(ToMmsNamedVariableReference).ToArray(),
            Values = values.ToArray()
        };
    }

    private static string ToMmsDataSetName(string reference)
    {
        var slash = reference.IndexOf('/');
        var item = slash >= 0 && slash < reference.Length - 1 ? reference[(slash + 1)..] : reference;
        return item.Replace('.', '$');
    }

    private static bool DataSetReferenceMatches(string candidateReference, string requestedReference, string requestedMmsName)
    {
        if (string.Equals(candidateReference, requestedReference, StringComparison.OrdinalIgnoreCase))
            return true;

        var candidateSlash = candidateReference.IndexOf('/');
        var requestedSlash = requestedReference.IndexOf('/');
        if (requestedSlash > 0 && candidateSlash > 0 &&
            !string.Equals(candidateReference[..candidateSlash], requestedReference[..requestedSlash], StringComparison.OrdinalIgnoreCase))
            return false;

        var candidateName = ToMmsDataSetName(candidateReference);
        return NormalizeDataSetName(candidateName).Equals(NormalizeDataSetName(requestedMmsName), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDataSetName(string value)
        => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string ToMmsDirectoryItemName(string reference)
    {
        var slash = reference.LastIndexOf('/');
        var item = slash >= 0 && slash < reference.Length - 1 ? reference[(slash + 1)..] : reference;
        return item.Replace('.', '$');
    }

    private static MmsReadOnlyServerResponse Page(
        string operation,
        string target,
        IReadOnlyList<string> allItems,
        string continueAfter,
        string itemDescription)
    {
        const int maxItems = 64;
        const int maxEncodedBytes = 8192;
        var start = 0;
        if (!string.IsNullOrWhiteSpace(continueAfter))
        {
            var exact = Array.FindIndex(allItems.ToArray(), x => string.Equals(x, continueAfter, StringComparison.OrdinalIgnoreCase));
            start = exact >= 0
                ? exact + 1
                : Array.FindIndex(allItems.ToArray(), x => string.Compare(x, continueAfter, StringComparison.OrdinalIgnoreCase) > 0);
            if (start < 0)
                start = allItems.Count;
        }

        var page = new List<string>(Math.Min(maxItems, Math.Max(0, allItems.Count - start)));
        var encodedBytes = 0;
        for (var index = start; index < allItems.Count && page.Count < maxItems; index++)
        {
            var item = allItems[index];
            var itemBytes = System.Text.Encoding.ASCII.GetByteCount(item) + 2;
            if (page.Count > 0 && encodedBytes + itemBytes > maxEncodedBytes)
                break;

            page.Add(item);
            encodedBytes += itemBytes;
        }

        var moreFollows = start + page.Count < allItems.Count;
        var firstItem = page.FirstOrDefault() ?? string.Empty;
        var lastItem = page.LastOrDefault() ?? string.Empty;
        return new MmsReadOnlyServerResponse
        {
            IsSuccess = true,
            Operation = operation,
            Target = target,
            Message = $"Returned page {page.Count.ToString(CultureInfo.InvariantCulture)} of {allItems.Count.ToString(CultureInfo.InvariantCulture)} {itemDescription}; first={firstItem}; last={lastItem}; continueAfter={continueAfter}; moreFollows={moreFollows}.",
            Items = page,
            MoreFollows = moreFollows
        };
    }

    private sealed record MmsTypeCandidate(MmsReadOnlyPoint Point, string[] Parts);

    private static MmsReadOnlyServerResponse RejectWrite(string target)
        => Fail(nameof(MmsReadOnlyOperation.Write), target, "Write operation rejected because this alpha server profile is read-only.");

    private static string ToMmsNamedVariableReference(MmsReadOnlyPoint point)
    {
        var path = point.Reference.Trim();
        var slash = path.LastIndexOf('/');
        var domain = string.IsNullOrWhiteSpace(point.LogicalDevice)
            ? slash > 0 ? path[..slash] : string.Empty
            : point.LogicalDevice;
        var itemPath = slash >= 0 && slash < path.Length - 1 ? path[(slash + 1)..] : path;
        var parts = itemPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return point.Reference;

        var fc = point.FunctionalConstraint.Trim();
        var mmsItem = string.IsNullOrWhiteSpace(fc) || (parts.Length > 1 && parts[1].Equals(fc, StringComparison.OrdinalIgnoreCase))
            ? string.Join('$', parts)
            : string.Join('$', new[] { parts[0], fc }.Concat(parts.Skip(1)));

        return string.IsNullOrWhiteSpace(domain) ? mmsItem : $"{domain}/{mmsItem}";
    }

    private static bool IsReferenceInLogicalDevice(string reference, string logicalDevice)
    {
        var slash = reference.IndexOf('/');
        var domain = slash > 0 ? reference[..slash] : string.Empty;
        return string.Equals(domain, logicalDevice, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToMmsControlBlockReference(string reference)
    {
        var slash = reference.LastIndexOf('/');
        var item = slash >= 0 && slash < reference.Length - 1 ? reference[(slash + 1)..] : reference;
        return item.Replace('.', '$');
    }

    private static MmsReadOnlyServerResponse Ok(string operation, string target, string message, IReadOnlyList<string> items)
        => new() { IsSuccess = true, Operation = operation, Target = target, Message = message, Items = items };

    private static MmsReadOnlyServerResponse Fail(string operation, string target, string message)
        => new() { IsSuccess = false, Operation = operation, Target = target, Message = message };
}
