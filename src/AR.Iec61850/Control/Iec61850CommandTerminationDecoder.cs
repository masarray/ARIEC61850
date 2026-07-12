using AR.Iec61850.Mms;

namespace AR.Iec61850.Control;

internal sealed class Iec61850CommandTermination
{
    public bool IsForControlObject { get; init; }
    public bool IsTermination { get; init; }
    public bool Positive { get; init; }
    public string ControlError { get; init; } = string.Empty;
    public string AddCause { get; init; } = string.Empty;
    public string LastApplErrorText { get; init; } = string.Empty;
    public string ResponseHex { get; init; } = string.Empty;
}

internal static class Iec61850CommandTerminationDecoder
{
    private static readonly IReadOnlyDictionary<long, string> ControlErrors = new Dictionary<long, string>
    {
        [0] = "no-error",
        [1] = "unknown",
        [2] = "timeout-test",
        [3] = "operator-test"
    };

    private static readonly IReadOnlyDictionary<long, string> AddCauses = new Dictionary<long, string>
    {
        [0] = "unknown",
        [1] = "not-supported",
        [2] = "blocked-by-switching-hierarchy",
        [3] = "select-failed",
        [4] = "invalid-position",
        [5] = "position-reached",
        [6] = "parameter-change-in-execution",
        [7] = "step-limit",
        [8] = "blocked-by-mode",
        [9] = "blocked-by-process",
        [10] = "blocked-by-interlocking",
        [11] = "blocked-by-synchrocheck",
        [12] = "command-already-in-execution",
        [13] = "blocked-by-health",
        [14] = "one-of-n-control",
        [15] = "abortion-by-cancel",
        [16] = "time-limit-over",
        [17] = "abortion-by-trip",
        [18] = "object-not-selected",
        [19] = "object-already-selected",
        [20] = "no-access-authority",
        [21] = "ended-with-overshoot",
        [22] = "abortion-due-to-deviation",
        [23] = "abortion-by-communication-loss",
        [24] = "abortion-by-command",
        [25] = "none",
        [26] = "inconsistent-parameters",
        [27] = "locked-by-other-client"
    };

    public static Iec61850CommandTermination Decode(MmsPduEnvelope envelope, Iec61850ControlObjectReferences references)
    {
        var report = MmsInformationReportDecoder.Decode(envelope.PresentationPayload);
        if (!report.IsSuccess)
            return new Iec61850CommandTermination { ResponseHex = report.ResponseHexPreview };

        var matchingObject = report.VariableReferences.Any(references.MatchesReportedReference);
        var matchingOperate = report.VariableReferences.Any(references.MatchesOperateReference);

        foreach (var item in report.Items)
        {
            if (item.Value is not { Kind: MmsDataKind.Structure } value)
                continue;

            if (!TryDecodeLastApplError(value, out var error, out var addCause, out var text, out var controlObject))
                continue;

            var embeddedObjectMatches = !string.IsNullOrWhiteSpace(controlObject) &&
                (references.MatchesReportedReference(controlObject) || references.MatchesOperateReference(controlObject));
            if (!matchingObject && !matchingOperate && !embeddedObjectMatches)
                continue;

            return new Iec61850CommandTermination
            {
                IsForControlObject = true,
                IsTermination = true,
                Positive = error == 0 && (addCause is 0 or 25),
                ControlError = Name(ControlErrors, error, "control-error"),
                AddCause = Name(AddCauses, addCause, "add-cause"),
                LastApplErrorText = text,
                ResponseHex = report.ResponseHexPreview
            };
        }

        // Enhanced-security positive CommandTermination commonly carries the Oper
        // variable with no LastApplError structure. Require an exact CO/Oper
        // reference so an ordinary ST/MX process report cannot complete a command.
        if (!matchingOperate)
            return new Iec61850CommandTermination { ResponseHex = report.ResponseHexPreview };

        return new Iec61850CommandTermination
        {
            IsForControlObject = true,
            IsTermination = true,
            Positive = true,
            ControlError = "no-error",
            AddCause = "none",
            LastApplErrorText = "Positive CommandTermination received.",
            ResponseHex = report.ResponseHexPreview
        };
    }

    internal static bool TryDecodeLastApplError(MmsDataValue value, out long error, out long addCause, out string text)
        => TryDecodeLastApplError(value, out error, out addCause, out text, out _);

    private static bool TryDecodeLastApplError(
        MmsDataValue value,
        out long error,
        out long addCause,
        out string text,
        out string controlObject)
    {
        error = 0;
        addCause = 25;
        text = string.Empty;
        controlObject = string.Empty;
        var children = value.Children;
        if (children.Count < 2)
            return false;

        // LastApplError ::= ctlObj, error, origin, ctlNum, addCause.
        // Some implementations omit ctlObj in an unbuffered information report,
        // so detect the numeric fields from both standard layouts.
        controlObject = ReadText(children[0]);
        var numeric = children
            .Select((child, index) => (child, index, number: ReadNumber(child)))
            .Where(x => x.number.HasValue)
            .ToArray();

        if (numeric.Length < 2)
            return false;

        var errorCandidate = numeric.FirstOrDefault(x => x.index is 0 or 1);
        var addCauseCandidate = numeric.Last();
        if (!errorCandidate.number.HasValue || !addCauseCandidate.number.HasValue)
            return false;

        error = errorCandidate.number.Value;
        addCause = addCauseCandidate.number.Value;
        text = $"LastApplError: error={Name(ControlErrors, error, "control-error")}, AddCause={Name(AddCauses, addCause, "add-cause")}.";
        return true;
    }

    private static string ReadText(MmsDataValue value)
        => value.Kind is MmsDataKind.VisibleString or MmsDataKind.MmsString
            ? Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;

    private static long? ReadNumber(MmsDataValue value)
        => value.Kind switch
        {
            MmsDataKind.Integer => Convert.ToInt64(value.Value, System.Globalization.CultureInfo.InvariantCulture),
            MmsDataKind.Unsigned => checked((long)Convert.ToUInt64(value.Value, System.Globalization.CultureInfo.InvariantCulture)),
            _ => null
        };

    private static string Name(IReadOnlyDictionary<long, string> map, long value, string prefix)
        => map.TryGetValue(value, out var name) ? name : $"{prefix}-{value}";
}
