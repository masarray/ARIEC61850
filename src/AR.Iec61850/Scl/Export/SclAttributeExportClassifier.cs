using AR.Iec61850.Discovery;

namespace AR.Iec61850.Scl.Export;

internal sealed record SclAttributeExportDecision(bool Include, string ReasonCode = "", string Reason = "");

internal static class SclAttributeExportClassifier
{
    public static SclAttributeExportDecision Evaluate(
        LiveIedSclExportProfile profile,
        LiveIedDataObjectModel dataObject,
        LiveIedDataAttributeModel attribute)
    {
        if (profile is LiveIedSclExportProfile.FullModel or LiveIedSclExportProfile.SimulatorSeed)
            return new SclAttributeExportDecision(true);

        var path = attribute.AttributePath ?? string.Empty;
        var fc = attribute.FunctionalConstraint ?? string.Empty;
        var cdc = dataObject.InferredCdc ?? string.Empty;
        var doName = dataObject.Name ?? string.Empty;
        var leaf = LeafName(path);

        if (string.Equals(fc, "CO", StringComparison.OrdinalIgnoreCase))
            return Exclude("ControlServiceParameter", "CO functional-constraint attributes are control service parameters; omit from IEDScout connection profile so the tool does not read Oper/SBOw/Cancel/ctlVal as ordinary values.");

        if (ContainsControlServicePath(path))
            return Exclude("ControlServiceParameter", "Oper/SBOw/Cancel service parameter omitted from IEDScout connection profile.");

        if (ContainsOrEqualsSegment(path, "origin"))
            return Exclude("ControlServiceParameter", "Originator structure omitted from IEDScout connection profile; many IEDs accept it only as a control-service parameter, not as an ordinary readable value.");

        if (IsControlServiceLeaf(leaf) && IsLikelyControlObject(cdc, doName, path))
            return Exclude("ControlServiceParameter", "Control-operation leaf omitted from IEDScout connection profile.");

        if (IsOptionalMeasurementOrConfigAttribute(path, leaf))
            return Exclude("OptionalConfigAttribute", "Optional measurement/configuration attribute omitted from IEDScout connection profile until read-proven by the IED.");

        if (IsKnownNoisyStatus(dataObject, leaf))
            return Exclude("NoisyOptionalStatus", "Known optional/status attribute frequently rejected by live IEDs is omitted from the clean connection profile.");

        if (dataObject.ConfidenceLevel is LiveIedDiscoveryConfidenceLevel.Low or LiveIedDiscoveryConfidenceLevel.Unknown)
            return Exclude("LowConfidenceType", "Low-confidence CDC/type inference omitted from IEDScout connection profile; retained in full discovery evidence.");

        return new SclAttributeExportDecision(true);
    }

    private static SclAttributeExportDecision Exclude(string code, string reason)
        => new(false, code, reason);

    private static bool ContainsControlServicePath(string path)
        => StartsWithSegment(path, "Oper") || ContainsSegment(path, "Oper") ||
           StartsWithSegment(path, "SBOw") || ContainsSegment(path, "SBOw") ||
           StartsWithSegment(path, "Cancel") || ContainsSegment(path, "Cancel");

    private static bool IsControlServiceLeaf(string leaf)
        => string.Equals(leaf, "ctlVal", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(leaf, "ctlNum", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(leaf, "Check", StringComparison.Ordinal) ||
           string.Equals(leaf, "T", StringComparison.Ordinal) ||
           string.Equals(leaf, "Test", StringComparison.Ordinal) ||
           string.Equals(leaf, "orCat", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(leaf, "orIdent", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyControlObject(string cdc, string doName, string path)
        => cdc is "SPC" or "DPC" or "INC" or "ENC" or "BSC" or "ISC" ||
           doName.Contains("CSO", StringComparison.OrdinalIgnoreCase) ||
           ContainsOrEqualsSegment(path, "origin");

    private static bool IsOptionalMeasurementOrConfigAttribute(string path, string leaf)
        => string.Equals(leaf, "db", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(leaf, "angRef", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(leaf, "seqT", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(leaf, "sboTimeout", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(leaf, "stSeld", StringComparison.OrdinalIgnoreCase) ||
           ContainsOrEqualsSegment(path, "units");

    private static bool IsKnownNoisyStatus(LiveIedDataObjectModel dataObject, string leaf)
    {
        if (!string.Equals(leaf, "stVal", StringComparison.OrdinalIgnoreCase))
            return false;

        var name = dataObject.Name ?? string.Empty;
        return string.Equals(name, "PhyHealth", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "FltNum", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "GriFltNum", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "CBOpCap", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "OpCnt", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("CntRs", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("ISCSO", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOrEqualsSegment(string path, string segment)
        => path.Equals(segment, StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith($"{segment}.", StringComparison.OrdinalIgnoreCase) ||
           path.EndsWith($".{segment}", StringComparison.OrdinalIgnoreCase) ||
           path.Contains($".{segment}.", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsSegment(string path, string segment)
        => path.Contains($".{segment}.", StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithSegment(string path, string segment)
        => path.Equals(segment, StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith($"{segment}.", StringComparison.OrdinalIgnoreCase);

    private static string LeafName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var dot = path.LastIndexOf(".", StringComparison.Ordinal);
        return dot >= 0 && dot < path.Length - 1 ? path[(dot + 1)..] : path;
    }
}
