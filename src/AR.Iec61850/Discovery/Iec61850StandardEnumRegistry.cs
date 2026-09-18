namespace AR.Iec61850.Discovery;

public sealed record Iec61850StandardEnumValue(int Ord, string Symbol);

public sealed record Iec61850StandardEnumDefinition(
    string Id,
    string Description,
    IReadOnlyList<Iec61850StandardEnumValue> Values);

/// <summary>
/// Small IEC 61850-oriented enum registry used when SCL synthesis emits standard status/control
/// values that are transported online as integers but are normally represented in SCL as Enum.
/// The entries are conservative generated engineering enums; they are not vendor-original IDs.
/// </summary>
public static class Iec61850StandardEnumRegistry
{
    private static readonly Iec61850StandardEnumDefinition GenericStatus = new(
        "ARIEC61850_GenEnumStatusKind",
        "Generic enumerated status fallback for live-discovered status objects.",
        [
            new(0, "unknown"),
            new(1, "value1"),
            new(2, "value2"),
            new(3, "value3"),
            new(4, "value4")
        ]);

    private static readonly Iec61850StandardEnumDefinition Behaviour = new(
        "ARIEC61850_BehaviourKind",
        "IEC 61850 behaviour/mode style enumeration.",
        [
            new(1, "on"),
            new(2, "blocked"),
            new(3, "test"),
            new(4, "testBlocked"),
            new(5, "off")
        ]);

    private static readonly Iec61850StandardEnumDefinition Health = new(
        "ARIEC61850_HealthKind",
        "IEC 61850 health style enumeration.",
        [
            new(1, "ok"),
            new(2, "warning"),
            new(3, "alarm")
        ]);

    private static readonly Iec61850StandardEnumDefinition BreakerOperationCapability = new(
        "ARIEC61850_CbOpCapKind",
        "Breaker operation capability style enumeration.",
        [
            new(1, "none"),
            new(2, "open"),
            new(3, "close"),
            new(4, "openClose")
        ]);

    private static readonly Iec61850StandardEnumDefinition ControlModel = new(
        "ARIEC61850_CtlModelKind",
        "IEC 61850 ctlModel style enumeration.",
        [
            new(0, "statusOnly"),
            new(1, "directWithNormalSecurity"),
            new(2, "sboWithNormalSecurity"),
            new(3, "directWithEnhancedSecurity"),
            new(4, "sboWithEnhancedSecurity")
        ]);

    private static readonly Iec61850StandardEnumDefinition OriginatorCategory = new(
        "ARIEC61850_OriginatorCategoryKind",
        "IEC 61850 originator category.",
        [
            new(0, "not-supported"),
            new(1, "bay-control"),
            new(2, "station-control"),
            new(3, "remote-control"),
            new(4, "automatic-bay"),
            new(5, "automatic-station"),
            new(6, "automatic-remote"),
            new(7, "maintenance"),
            new(8, "process")
        ]);

    private static readonly Iec61850StandardEnumDefinition SiUnit = new(
        "ARIEC61850_SIUnitKind",
        "IEC 61850 SI unit enumeration.",
        [
            new(1, ""), new(2, "m"), new(3, "kg"), new(4, "s"), new(5, "A"),
            new(6, "K"), new(7, "mol"), new(8, "cd"), new(9, "deg"), new(10, "rad"),
            new(11, "sr"), new(21, "Gy"), new(22, "Bq"), new(23, "°C"), new(24, "Sv"),
            new(25, "F"), new(26, "C"), new(27, "S"), new(28, "H"), new(29, "V"),
            new(30, "ohm"), new(31, "J"), new(32, "N"), new(33, "Hz"), new(34, "lx"),
            new(35, "Lm"), new(36, "Wb"), new(37, "T"), new(38, "W"), new(39, "Pa"),
            new(41, "m²"), new(42, "m³"), new(43, "m/s"), new(44, "m/s²"), new(45, "m³/s"),
            new(46, "m/m³"), new(47, "M"), new(48, "kg/m³"), new(49, "m²/s"), new(50, "W/m K"),
            new(51, "J/K"), new(52, "ppm"), new(53, "1/s"), new(54, "rad/s"), new(55, "W/m²"),
            new(56, "J/m²"), new(57, "S/m"), new(58, "K/s"), new(59, "Pa/s"), new(60, "J/kg K"),
            new(61, "VA"), new(62, "Watts"), new(63, "VAr"), new(64, "phi"), new(65, "cos(phi)"),
            new(66, "Vs"), new(67, "V²"), new(68, "As"), new(69, "A²"), new(70, "A²t"),
            new(71, "VAh"), new(72, "Wh"), new(73, "VArh"), new(74, "V/Hz"), new(75, "Hz/s"),
            new(76, "char"), new(77, "char/s"), new(78, "kgm²"), new(79, "dB"), new(80, "J/Wh"),
            new(81, "W/s"), new(82, "l/s"), new(83, "dBm")
        ]);

    private static readonly Iec61850StandardEnumDefinition Multiplier = new(
        "ARIEC61850_MultiplierKind",
        "IEC 61850 unit multiplier enumeration.",
        [
            new(-24, "y"), new(-21, "z"), new(-18, "a"), new(-15, "f"), new(-12, "p"),
            new(-9, "n"), new(-6, "µ"), new(-3, "m"), new(-2, "c"), new(-1, "d"),
            new(0, ""), new(1, "da"), new(2, "h"), new(3, "k"), new(6, "M"),
            new(9, "G"), new(12, "T"), new(15, "P"), new(18, "E"), new(21, "Z"), new(24, "Y")
        ]);

    public static bool RequiresEnumType(string cdc, string attributeName)
        => TryResolve(string.Empty, string.Empty, cdc, attributeName, out _);

    public static Iec61850StandardEnumDefinition Resolve(string logicalNodeClass, string dataObjectName, string cdc, string attributeName)
        => TryResolve(logicalNodeClass, dataObjectName, cdc, attributeName, out var definition)
            ? definition
            : GenericStatus;

    public static bool TryResolve(string logicalNodeClass, string dataObjectName, string cdc, string attributeName, out Iec61850StandardEnumDefinition definition)
    {
        definition = default!;
        if (string.IsNullOrWhiteSpace(cdc) || string.IsNullOrWhiteSpace(attributeName))
            return false;

        var cdcValue = cdc.Trim();
        var daName = attributeName.Trim();
        var doName = dataObjectName?.Trim() ?? string.Empty;

        if (daName.Equals("ctlModel", StringComparison.OrdinalIgnoreCase))
        {
            definition = ControlModel;
            return true;
        }

        if (daName.Equals("orCat", StringComparison.OrdinalIgnoreCase))
        {
            definition = OriginatorCategory;
            return true;
        }

        if (daName.Equals("SIUnit", StringComparison.OrdinalIgnoreCase))
        {
            definition = SiUnit;
            return true;
        }

        if (daName.Equals("multiplier", StringComparison.OrdinalIgnoreCase))
        {
            definition = Multiplier;
            return true;
        }

        if (!daName.Equals("stVal", StringComparison.OrdinalIgnoreCase) &&
            !daName.Equals("ctlVal", StringComparison.OrdinalIgnoreCase) &&
            !daName.Equals("setVal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (doName.Equals("Beh", StringComparison.OrdinalIgnoreCase) ||
            doName.Equals("Mod", StringComparison.OrdinalIgnoreCase))
        {
            definition = Behaviour;
            return true;
        }

        if (doName.Equals("Health", StringComparison.OrdinalIgnoreCase) ||
            doName.Equals("PhyHealth", StringComparison.OrdinalIgnoreCase))
        {
            definition = Health;
            return true;
        }

        if (doName.Equals("CBOpCap", StringComparison.OrdinalIgnoreCase))
        {
            definition = BreakerOperationCapability;
            return true;
        }

        // ENC/ENS/ENG are explicit enumerated CDC families. Keep a fallback enum when the
        // object-specific enum is not known yet. INS is intentionally not fallback-enumerated:
        // many INS objects (FltNum, OpCnt, integer counters) must remain INT32.
        if (cdcValue.Equals("ENS", StringComparison.OrdinalIgnoreCase) ||
            cdcValue.Equals("ENC", StringComparison.OrdinalIgnoreCase) ||
            cdcValue.Equals("ENG", StringComparison.OrdinalIgnoreCase))
        {
            definition = GenericStatus;
            return true;
        }

        return false;
    }
}
