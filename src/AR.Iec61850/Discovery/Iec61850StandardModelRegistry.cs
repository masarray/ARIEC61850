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
        [Key("LLN0", "Mod")] = Def("LLN0", "Mod", "ENC", 0.98, "enumerated mode control"),
        [Key("LLN0", "Beh")] = Def("LLN0", "Beh", "ENS", 0.98, "enumerated behaviour status"),
        [Key("LLN0", "Health")] = Def("LLN0", "Health", "ENS", 0.98, "enumerated health status"),
        [Key("LLN0", "MltLev")] = Def("LLN0", "MltLev", "SPG", 0.96, "multiple setting-level selection"),

        [Key("LPHD", "PhyNam")] = Def("LPHD", "PhyNam", "DPL", 0.98, "physical device nameplate"),
        [Key("LPHD", "Proxy")] = Def("LPHD", "Proxy", "SPS", 0.92, "proxy status"),
        [Key("LPHD", "PhyHealth")] = Def("LPHD", "PhyHealth", "ENS", 0.98, "physical device health status"),

        [Key("PTOC", "Op")] = Def("PTOC", "Op", "ACT", 0.94, "protection operation indication"),
        [Key("PTOC", "Str")] = Def("PTOC", "Str", "ACD", 0.94, "protection start indication"),
        [Key("PTRC", "Op")] = Def("PTRC", "Op", "ACT", 0.94, "trip conditioning operation indication"),
        [Key("RREC", "Op")] = Def("RREC", "Op", "ACT", 0.92, "reclosing operation indication"),

        [Key("CSWI", "Pos")] = Def("CSWI", "Pos", "DPC", 0.94, "controllable switch position"),
        [Key("XCBR", "Pos")] = Def("XCBR", "Pos", "DPC", 0.94, "breaker position"),
        [Key("XCBR", "CBOpCap")] = Def("XCBR", "CBOpCap", "INS", 0.90, "breaker operation capability integer/enumerated status"),
        [Key("XCBR", "OpCnt")] = Def("XCBR", "OpCnt", "INS", 0.86, "operation counter"),
        [Key("XCBR", "EEName")] = Def("XCBR", "EEName", "DPL", 0.98, "external equipment nameplate"),
        [Key("XSWI", "EEName")] = Def("XSWI", "EEName", "DPL", 0.98, "external equipment nameplate"),

        [Key("RDRE", "FltNum")] = Def("RDRE", "FltNum", "INS", 0.90, "fault number"),
        [Key("RDRE", "GriFltNum")] = Def("RDRE", "GriFltNum", "INS", 0.90, "grid fault number"),

        [Key("MMXU", "PhV")] = Def("MMXU", "PhV", "WYE", 0.94, "phase-to-ground voltage"),
        [Key("MMXU", "A")] = Def("MMXU", "A", "WYE", 0.94, "phase current"),
        [Key("MMXU", "PPV")] = Def("MMXU", "PPV", "DEL", 0.92, "phase-to-phase voltage"),
        [Key("MMXU", "W")] = Def("MMXU", "W", "WYE", 0.86, "three phase active power"),
        [Key("MMXU", "VAr")] = Def("MMXU", "VAr", "WYE", 0.86, "three phase reactive power"),
        [Key("MMXU", "VA")] = Def("MMXU", "VA", "WYE", 0.86, "three phase apparent power"),
        [Key("MMXU", "PF")] = Def("MMXU", "PF", "WYE", 0.84, "power factor"),

        // Harmonic measurements use phase-group CDCs. Physical AA1E1F06R4
        // plus independent interoperability SCL prove that MHAI ThdA / ThdPhV are WYE objects whose
        // phase children are CMV SDOs, not ACD indication structures.
        [Key("MHAI", "ThdA")] = Def("MHAI", "ThdA", "WYE", 0.98, "three-phase current THD"),
        [Key("MHAI", "ThdPhV")] = Def("MHAI", "ThdPhV", "WYE", 0.98, "three-phase voltage THD"),

        // Instrument-transformer semantics are standardized and were also observed in
        // the AA1E1F06R4 independent interoperability engineering model. Exact registry entries prevent
        // live discovery from dropping these configuration DOs when attribute-name
        // heuristics alone cannot infer their CDC safely.
        [Key("TCTR", "Mod")] = Def("TCTR", "Mod", "ENC", 0.98, "mode control"),
        [Key("TCTR", "Beh")] = Def("TCTR", "Beh", "ENS", 0.98, "behaviour status"),
        [Key("TCTR", "Health")] = Def("TCTR", "Health", "ENS", 0.98, "health status"),
        [Key("TCTR", "NamPlt")] = Def("TCTR", "NamPlt", "LPL", 0.98, "logical-node nameplate"),
        [Key("TCTR", "ARtg")] = Def("TCTR", "ARtg", "ASG", 0.98, "rated current"),
        [Key("TCTR", "Rat")] = Def("TCTR", "Rat", "ASG", 0.98, "transformation ratio"),
        [Key("TCTR", "HzRtg")] = Def("TCTR", "HzRtg", "ASG", 0.98, "rated frequency"),
        [Key("TCTR", "Cor")] = Def("TCTR", "Cor", "ASG", 0.98, "correction"),
        [Key("TCTR", "AmpSv")] = Def("TCTR", "AmpSv", "SAV", 0.98, "sampled current value"),
        [Key("TCTR", "Trp")] = Def("TCTR", "Trp", "ING", 0.98, "transient response parameter"),
        [Key("TCTR", "ScndTmms")] = Def("TCTR", "ScndTmms", "ING", 0.98, "secondary time parameter"),
        [Key("TCTR", "Clip")] = Def("TCTR", "Clip", "ASG", 0.98, "clipping limit"),
        [Key("TCTR", "AccMeas")] = Def("TCTR", "AccMeas", "ING", 0.98, "measurement accuracy class"),
        [Key("TCTR", "AccPro")] = Def("TCTR", "AccPro", "ING", 0.98, "protection accuracy class"),
        [Key("TCTR", "HoldTmms")] = Def("TCTR", "HoldTmms", "ING", 0.98, "hold time"),

        [Key("TVTR", "Mod")] = Def("TVTR", "Mod", "ENC", 0.98, "mode control"),
        [Key("TVTR", "Beh")] = Def("TVTR", "Beh", "ENS", 0.98, "behaviour status"),
        [Key("TVTR", "Health")] = Def("TVTR", "Health", "ENS", 0.98, "health status"),
        [Key("TVTR", "NamPlt")] = Def("TVTR", "NamPlt", "LPL", 0.98, "logical-node nameplate"),
        [Key("TVTR", "VRtg")] = Def("TVTR", "VRtg", "ASG", 0.98, "rated voltage"),
        [Key("TVTR", "Rat")] = Def("TVTR", "Rat", "ASG", 0.98, "transformation ratio"),
        [Key("TVTR", "HzRtg")] = Def("TVTR", "HzRtg", "ASG", 0.98, "rated frequency"),
        [Key("TVTR", "Cor")] = Def("TVTR", "Cor", "ASG", 0.98, "correction"),
        [Key("TVTR", "VolSv")] = Def("TVTR", "VolSv", "SAV", 0.98, "sampled voltage value"),
        [Key("TVTR", "Clip")] = Def("TVTR", "Clip", "ASG", 0.98, "clipping limit"),
        [Key("TVTR", "AccMeas")] = Def("TVTR", "AccMeas", "ING", 0.98, "measurement accuracy class"),
        [Key("TVTR", "AccPro")] = Def("TVTR", "AccPro", "ING", 0.98, "protection accuracy class"),
        [Key("TVTR", "HoldTmms")] = Def("TVTR", "HoldTmms", "ING", 0.98, "hold time"),

        [Key("LTIM", "Mod")] = Def("LTIM", "Mod", "ENC", 0.98, "mode control"),
        [Key("LTIM", "Beh")] = Def("LTIM", "Beh", "ENS", 0.98, "behaviour status"),
        [Key("LTIM", "Health")] = Def("LTIM", "Health", "ENS", 0.98, "health status"),
        [Key("LTIM", "NamPlt")] = Def("LTIM", "NamPlt", "LPL", 0.98, "logical-node nameplate"),
        [Key("LTIM", "TmChgDT")] = Def("LTIM", "TmChgDT", "TSG", 0.98, "daylight-saving change time"),
        [Key("LTIM", "TmChgST")] = Def("LTIM", "TmChgST", "TSG", 0.98, "standard-time change time"),
        [Key("LTIM", "TmDT")] = Def("LTIM", "TmDT", "SPS", 0.98, "daylight-saving status"),
        [Key("LTIM", "TmOfsTmm")] = Def("LTIM", "TmOfsTmm", "ING", 0.98, "time offset"),
        [Key("LTIM", "TmUseDT")] = Def("LTIM", "TmUseDT", "SPG", 0.98, "daylight-saving enable"),

        // IEC 61850 Edition 2 service tracking LN. These CDCs are intentionally
        // schema-gated by the SCL exporter for Edition 2 output.
        [Key("LTRK", "SpcTrk")] = Def("LTRK", "SpcTrk", "CTS", 0.98, "single-point control service tracking"),
        [Key("LTRK", "DpcTrk")] = Def("LTRK", "DpcTrk", "CTS", 0.98, "double-point control service tracking"),
        [Key("LTRK", "IncTrk")] = Def("LTRK", "IncTrk", "CTS", 0.98, "integer control service tracking"),
        [Key("LTRK", "EncTrk1")] = Def("LTRK", "EncTrk1", "CTS", 0.98, "enumerated control service tracking"),
        [Key("LTRK", "EncTrk2")] = Def("LTRK", "EncTrk2", "CTS", 0.98, "enumerated control service tracking"),
        [Key("LTRK", "EncTrk3")] = Def("LTRK", "EncTrk3", "CTS", 0.98, "enumerated control service tracking"),
        [Key("LTRK", "EncTrk4")] = Def("LTRK", "EncTrk4", "CTS", 0.98, "enumerated control service tracking"),
        [Key("LTRK", "ApcFTrk")] = Def("LTRK", "ApcFTrk", "CTS", 0.98, "analogue control service tracking"),
        [Key("LTRK", "BscTrk")] = Def("LTRK", "BscTrk", "CTS", 0.98, "binary step control service tracking"),
        [Key("LTRK", "IscTrk")] = Def("LTRK", "IscTrk", "CTS", 0.98, "integer step control service tracking"),
        [Key("LTRK", "BacTrk")] = Def("LTRK", "BacTrk", "CTS", 0.98, "binary analogue control service tracking"),
        [Key("LTRK", "GenTrk")] = Def("LTRK", "GenTrk", "CST", 0.98, "common service tracking"),
        [Key("LTRK", "UrcbTrk")] = Def("LTRK", "UrcbTrk", "UTS", 0.98, "unbuffered report control tracking"),
        [Key("LTRK", "BrcbTrk")] = Def("LTRK", "BrcbTrk", "BTS", 0.98, "buffered report control tracking"),
        [Key("LTRK", "SgcbTrk")] = Def("LTRK", "SgcbTrk", "STS", 0.98, "setting-group control tracking"),

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

        // Common Data Objects inherited by many logical-node classes. These
        // semantics are standard model authority, not device-specific guesses.
        // Keeping them here prevents raw integer/string wire types from degrading
        // the engineering CDC in canonical SCL.
        if (doName.Equals("Mod", StringComparison.OrdinalIgnoreCase))
        {
            definition = Def(lnClass, doName, "ENC", 0.98, "common enumerated mode control");
            return true;
        }

        if (doName.Equals("Beh", StringComparison.OrdinalIgnoreCase) ||
            doName.Equals("Health", StringComparison.OrdinalIgnoreCase) ||
            doName.Equals("PhyHealth", StringComparison.OrdinalIgnoreCase))
        {
            definition = Def(lnClass, doName, "ENS", 0.98, "common enumerated status");
            return true;
        }

        if (doName.Equals("GrRef", StringComparison.OrdinalIgnoreCase))
        {
            definition = Def(lnClass, doName, "ORG", 0.98, "common object-reference setting group reference");
            return true;
        }

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
