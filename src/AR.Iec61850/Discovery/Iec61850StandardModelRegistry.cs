namespace AR.Iec61850.Discovery;

public sealed record Iec61850StandardDataObjectDefinition(
    string LogicalNodeClass,
    string DataObjectName,
    string Cdc,
    double Confidence,
    string Description);

/// <summary>
/// Minimal built-in IEC 61850 LN/DO semantic registry used by live-discovery SCL synthesis.
/// This registry is intentionally conservative: it does not claim vendor-original template IDs,
/// it only supplies standard-aware CDC hints for well-known LN/DO pairs so generated SCL is
/// closer to a normal engineering model than raw MMS path reconstruction.
/// </summary>
public static class Iec61850StandardModelRegistry
{
    private static readonly Dictionary<string, Iec61850StandardDataObjectDefinition> ExactDefinitions = new(StringComparer.OrdinalIgnoreCase)
    {
        [Key("LLN0", "NamPlt")] = Def("LLN0", "NamPlt", "LPL", 0.98, "logical-node nameplate"),
        [Key("LLN0", "Mod")] = Def("LLN0", "Mod", "INC", 0.94, "controllable mode"),
        [Key("LLN0", "Beh")] = Def("LLN0", "Beh", "INS", 0.94, "behaviour integer/enumerated status"),
        [Key("LLN0", "Health")] = Def("LLN0", "Health", "INS", 0.94, "health integer/enumerated status"),

        [Key("LPHD", "PhyNam")] = Def("LPHD", "PhyNam", "DPL", 0.98, "physical device nameplate"),
        [Key("LPHD", "Proxy")] = Def("LPHD", "Proxy", "SPS", 0.92, "proxy status"),
        [Key("LPHD", "PhyHealth")] = Def("LPHD", "PhyHealth", "INS", 0.94, "physical device health integer/enumerated status"),

        [Key("PTOC", "Op")] = Def("PTOC", "Op", "ACT", 0.94, "protection operation indication"),
        [Key("PTOC", "Str")] = Def("PTOC", "Str", "ACD", 0.94, "protection start indication"),
        [Key("PTRC", "Op")] = Def("PTRC", "Op", "ACT", 0.94, "trip conditioning operation indication"),
        [Key("RREC", "Op")] = Def("RREC", "Op", "ACT", 0.92, "reclosing operation indication"),

        [Key("CSWI", "Pos")] = Def("CSWI", "Pos", "DPC", 0.94, "controllable switch position"),
        [Key("XCBR", "Pos")] = Def("XCBR", "Pos", "DPC", 0.94, "breaker position"),
        [Key("XCBR", "CBOpCap")] = Def("XCBR", "CBOpCap", "INS", 0.90, "breaker operation capability integer/enumerated status"),
        [Key("XCBR", "OpCnt")] = Def("XCBR", "OpCnt", "INS", 0.86, "operation counter"),

        [Key("RDRE", "FltNum")] = Def("RDRE", "FltNum", "INS", 0.90, "fault number"),
        [Key("RDRE", "GriFltNum")] = Def("RDRE", "GriFltNum", "INS", 0.90, "grid fault number"),

        [Key("MMXU", "PhV")] = Def("MMXU", "PhV", "WYE", 0.94, "phase-to-ground voltage"),
        [Key("MMXU", "A")] = Def("MMXU", "A", "WYE", 0.94, "phase current"),
        [Key("MMXU", "PPV")] = Def("MMXU", "PPV", "DEL", 0.92, "phase-to-phase voltage"),
        [Key("MMXU", "W")] = Def("MMXU", "W", "WYE", 0.86, "three phase active power"),
        [Key("MMXU", "VAr")] = Def("MMXU", "VAr", "WYE", 0.86, "three phase reactive power"),
        [Key("MMXU", "VA")] = Def("MMXU", "VA", "WYE", 0.86, "three phase apparent power"),
        [Key("MMXU", "PF")] = Def("MMXU", "PF", "WYE", 0.84, "power factor"),

        [Key("MSQI", "SeqA")] = Def("MSQI", "SeqA", "SEQ", 0.92, "current sequence components"),
        [Key("MSQI", "SeqV")] = Def("MSQI", "SeqV", "SEQ", 0.92, "voltage sequence components")
    };

    public static bool TryResolve(string logicalNodeClass, string dataObjectName, out Iec61850StandardDataObjectDefinition definition)
    {
        definition = default!;
        if (string.IsNullOrWhiteSpace(dataObjectName))
            return false;

        var lnClass = logicalNodeClass.Trim();
        var doName = dataObjectName.Trim();
        if (!string.IsNullOrWhiteSpace(lnClass) && ExactDefinitions.TryGetValue(Key(lnClass, doName), out definition!))
            return true;

        if (TryPatternResolve(lnClass, doName, out definition!))
            return true;

        return false;
    }

    private static bool TryPatternResolve(string logicalNodeClass, string dataObjectName, out Iec61850StandardDataObjectDefinition definition)
    {
        definition = default!;
        var doName = dataObjectName.Trim();
        var lnClass = logicalNodeClass.Trim();

        if (doName.StartsWith("DPCSO", StringComparison.OrdinalIgnoreCase))
        {
            definition = Def(lnClass, doName, "DPC", 0.88, "GGIO/vendor double-point controllable object");
            return true;
        }

        if (doName.StartsWith("SPCSO", StringComparison.OrdinalIgnoreCase))
        {
            definition = Def(lnClass, doName, "SPC", 0.88, "GGIO/vendor single-point controllable object");
            return true;
        }

        if (doName.StartsWith("ISCSO", StringComparison.OrdinalIgnoreCase))
        {
            definition = Def(lnClass, doName, "ISC", 0.86, "GGIO/vendor integer controllable object");
            return true;
        }

        if (doName.EndsWith("CntRs", StringComparison.OrdinalIgnoreCase) || doName.EndsWith("CntRst", StringComparison.OrdinalIgnoreCase))
        {
            definition = Def(lnClass, doName, "INC", 0.82, "counter reset controllable object");
            return true;
        }

        if (doName.StartsWith("Sum", StringComparison.OrdinalIgnoreCase) ||
            doName.StartsWith("Sup", StringComparison.OrdinalIgnoreCase) ||
            doName.StartsWith("Dmd", StringComparison.OrdinalIgnoreCase))
        {
            definition = Def(lnClass, doName, "BCR", 0.80, "binary counter reading pattern");
            return true;
        }

        if (doName.StartsWith("Seq", StringComparison.OrdinalIgnoreCase))
        {
            definition = Def(lnClass, doName, "SEQ", 0.80, "sequence component pattern");
            return true;
        }

        return false;
    }

    private static Iec61850StandardDataObjectDefinition Def(string logicalNodeClass, string dataObjectName, string cdc, double confidence, string description)
        => new(logicalNodeClass, dataObjectName, cdc, confidence, description);

    private static string Key(string logicalNodeClass, string dataObjectName)
        => $"{logicalNodeClass.Trim().ToUpperInvariant()}.{dataObjectName.Trim().ToUpperInvariant()}";
}
