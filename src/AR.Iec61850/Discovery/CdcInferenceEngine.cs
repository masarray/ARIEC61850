namespace AR.Iec61850.Discovery;

public sealed class CdcInferenceResult
{
    public string Cdc { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public LiveIedDiscoveryConfidenceLevel Level { get; init; } = LiveIedDiscoveryConfidenceLevel.Unknown;
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

public static class CdcInferenceEngine
{
    private static readonly HashSet<string> KnownCdcValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "SPS", "DPS", "INS", "ACT", "ACD", "SEC", "BCR",
        "MV", "CMV", "SAV", "WYE", "DEL", "SEQ",
        "SPC", "DPC", "INC", "BSC", "ISC", "APC", "BAC",
        "SPG", "ING", "ASG", "CURVE", "ORG", "TSG", "CUG",
        "VSG", "ENG", "ENS", "ENC",
        "LPL", "DPL"
    };

    public static bool IsKnownCdc(string cdc)
        => !string.IsNullOrWhiteSpace(cdc) && KnownCdcValues.Contains(cdc.Trim());

    public static CdcInferenceResult Infer(
        string logicalNodeClass,
        string dataObjectName,
        IReadOnlyCollection<string> attributePaths,
        IReadOnlyCollection<string> functionalConstraints)
    {
        var evidence = new List<string>();
        var doName = dataObjectName.Trim();
        var lnClass = logicalNodeClass.Trim();
        var attrs = attributePaths
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToArray();
        var fcs = functionalConstraints
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (string.Equals(doName, "NamPlt", StringComparison.OrdinalIgnoreCase))
            return Result("LPL", 0.96, evidence.Append("standard logical-node nameplate DO"));

        if (string.Equals(doName, "PhyNam", StringComparison.OrdinalIgnoreCase))
            return Result("DPL", 0.96, evidence.Append("standard physical-device nameplate DO"));

        if (string.Equals(doName, "SGCB", StringComparison.OrdinalIgnoreCase))
            return Result(string.Empty, 0.0, evidence.Append("SGCB is a setting-group control block, not a CDC data object"));

        if (string.Equals(doName, "Mod", StringComparison.OrdinalIgnoreCase))
            return Result("INC", 0.90, evidence.Append("standard controllable mode DO"));

        if (IsIntegerStatusDo(doName))
            return Result("INS", 0.88, evidence.Append("standard integer status DO"));

        if (string.Equals(doName, "Proxy", StringComparison.OrdinalIgnoreCase))
            return Result("SPS", 0.86, evidence.Append("standard proxy single-point status DO"));

        if (IsCounterDo(doName, attrs))
            return Result("BCR", 0.84, evidence.Append("binary counter reading pattern"));

        if (IsSequenceDo(doName, attrs))
            return Result("SEQ", 0.82, evidence.Append("sequence component pattern"));

        if (ContainsAny(attrs, "phsAB", "phsBC", "phsCA"))
            return Result("DEL", 0.80, evidence.Append("phase-to-phase structured attributes detected"));

        if (IsProtectionActivationDo(doName, attrs, fcs))
            return Result("ACT", 0.86, evidence.Append("activation indication pattern"));

        if (IsProtectionStartDo(doName, attrs, fcs))
            return Result("ACD", 0.84, evidence.Append("directional/general start indication pattern"));

        if (IsDoublePointControlDo(doName, attrs))
            return Result("DPC", 0.80, evidence.Append("double-point controllable object pattern"));

        if (IsSinglePointControlDo(doName, attrs))
            return Result("SPC", 0.78, evidence.Append("single-point controllable object pattern"));

        if (ContainsAny(attrs, "stVal") && ContainsAny(attrs, "q") && ContainsAny(attrs, "t"))
        {
            evidence.Add("contains stVal/q/t status triplet");
            if (IsProtectionOperation(lnClass, doName))
                return Result("ACT", 0.86, evidence.Append("protection LN operation/status DO pattern"));

            if (string.Equals(doName, "Str", StringComparison.OrdinalIgnoreCase))
                return Result("ACD", 0.82, evidence.Append("Str DO with stVal/q/t is usually directional/general start indication family"));

            return Result("SPS", 0.78, evidence.Append("generic boolean status pattern"));
        }

        if (ContainsAny(attrs, "phsA", "phsB", "phsC", "neut") || attrs.Any(x => x.StartsWith("phsA.", StringComparison.OrdinalIgnoreCase)))
        {
            evidence.Add("phase structured attributes detected");
            if (string.Equals(lnClass, "MMXU", StringComparison.OrdinalIgnoreCase))
                return Result("WYE", 0.78, evidence.Append("MMXU phase measurement DO pattern"));

            return Result("ACD", 0.62, evidence.Append("phase indication structure without full type metadata"));
        }

        if (ContainsAny(attrs, "cVal", "instCVal") && ContainsAny(attrs, "q") && ContainsAny(attrs, "t") && !ContainsAny(attrs, "phsA", "phsB", "phsC", "neut"))
            return Result("CMV", 0.80, evidence.Append("complex measured value pattern"));

        if (ContainsAny(attrs, "mag", "mag.f", "mag.i") && ContainsAny(attrs, "q") && ContainsAny(attrs, "t"))
            return Result("MV", 0.80, evidence.Append("contains mag/q/t analogue measurement pattern"));

        if (ContainsAny(attrs, "ctlVal", "Oper", "SBOw", "Cancel") || fcs.Contains("CO", StringComparer.OrdinalIgnoreCase))
            return Result("SPC", 0.55, evidence.Append("control functional constraint or control operation attributes detected without exact CDC metadata"));

        if (ContainsAny(attrs, "setVal") || fcs.Contains("SP", StringComparer.OrdinalIgnoreCase) || fcs.Contains("SG", StringComparer.OrdinalIgnoreCase) || fcs.Contains("SE", StringComparer.OrdinalIgnoreCase))
            return Result("SPG", 0.45, evidence.Append("setting functional constraint or setVal detected without exact CDC metadata"));

        if (fcs.Contains("MX", StringComparer.OrdinalIgnoreCase))
            return Result("MV", 0.40, evidence.Append("MX functional constraint detected without enough MMS type detail"));

        if (fcs.Contains("ST", StringComparer.OrdinalIgnoreCase))
            return Result("SPS", 0.38, evidence.Append("ST functional constraint detected without enough MMS type detail"));

        return Result(string.Empty, 0.0, evidence.Append("CDC cannot be inferred from current online discovery only"));
    }

    private static bool IsProtectionOperation(string lnClass, string dataObjectName)
        => string.Equals(dataObjectName, "Op", StringComparison.OrdinalIgnoreCase) &&
           ((lnClass.Length > 0 && lnClass[0] == 'P') || string.Equals(lnClass, "RREC", StringComparison.OrdinalIgnoreCase));

    private static bool IsIntegerStatusDo(string dataObjectName)
        => string.Equals(dataObjectName, "Beh", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(dataObjectName, "Health", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(dataObjectName, "AutoRecSt", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(dataObjectName, "OpTmh", StringComparison.OrdinalIgnoreCase);

    private static bool IsCounterDo(string dataObjectName, IReadOnlyCollection<string> attributes)
        => ContainsAny(attributes, "actVal", "pulsQty") ||
           dataObjectName.StartsWith("Sum", StringComparison.OrdinalIgnoreCase) ||
           dataObjectName.StartsWith("Sup", StringComparison.OrdinalIgnoreCase) ||
           dataObjectName.StartsWith("Dmd", StringComparison.OrdinalIgnoreCase);

    private static bool IsSequenceDo(string dataObjectName, IReadOnlyCollection<string> attributes)
        => dataObjectName.StartsWith("Seq", StringComparison.OrdinalIgnoreCase) ||
           ContainsAny(attributes, "seqT") ||
           (ContainsAny(attributes, "c1") && ContainsAny(attributes, "c2") && ContainsAny(attributes, "c3"));

    private static bool IsProtectionActivationDo(string dataObjectName, IReadOnlyCollection<string> attributes, IReadOnlyCollection<string> functionalConstraints)
        => (string.Equals(dataObjectName, "Op", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dataObjectName, "Tr", StringComparison.OrdinalIgnoreCase) ||
            dataObjectName.StartsWith("Op", StringComparison.OrdinalIgnoreCase)) &&
           functionalConstraints.Contains("ST", StringComparer.OrdinalIgnoreCase) &&
           ContainsAny(attributes, "general", "q", "t");

    private static bool IsProtectionStartDo(string dataObjectName, IReadOnlyCollection<string> attributes, IReadOnlyCollection<string> functionalConstraints)
        => string.Equals(dataObjectName, "Str", StringComparison.OrdinalIgnoreCase) &&
           functionalConstraints.Contains("ST", StringComparer.OrdinalIgnoreCase) &&
           ContainsAny(attributes, "general", "dirGeneral", "q", "t");

    private static bool IsDoublePointControlDo(string dataObjectName, IReadOnlyCollection<string> attributes)
        => string.Equals(dataObjectName, "Pos", StringComparison.OrdinalIgnoreCase) ||
           dataObjectName.StartsWith("DPCSO", StringComparison.OrdinalIgnoreCase) ||
           ContainsAny(attributes, "stSeld");

    private static bool IsSinglePointControlDo(string dataObjectName, IReadOnlyCollection<string> attributes)
        => dataObjectName.StartsWith("SPCSO", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(dataObjectName, "BlkOpn", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(dataObjectName, "BlkCls", StringComparison.OrdinalIgnoreCase) ||
           (ContainsAny(attributes, "ctlVal") && !IsDoublePointControlDo(dataObjectName, attributes));

    private static bool ContainsAny(IEnumerable<string> values, params string[] candidates)
    {
        foreach (var value in values)
        {
            foreach (var candidate in candidates)
            {
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase) ||
                    value.EndsWith('.' + candidate, StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith(candidate + '.', StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static CdcInferenceResult Result(string cdc, double confidence, IEnumerable<string> evidence)
        => new()
        {
            Cdc = cdc.Trim(),
            Confidence = confidence,
            Level = ToLevel(confidence),
            Evidence = evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };

    private static LiveIedDiscoveryConfidenceLevel ToLevel(double confidence)
        => confidence switch
        {
            >= 0.85 => LiveIedDiscoveryConfidenceLevel.High,
            >= 0.60 => LiveIedDiscoveryConfidenceLevel.Medium,
            >= 0.35 => LiveIedDiscoveryConfidenceLevel.Low,
            _ => LiveIedDiscoveryConfidenceLevel.Unknown
        };
}
