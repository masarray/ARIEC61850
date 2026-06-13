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
            new(1, "off"),
            new(2, "blocked"),
            new(3, "test"),
            new(4, "testBlocked"),
            new(5, "on")
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
