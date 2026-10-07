namespace AR.Iec61850.Mms;

/// <summary>
/// Binds a well-known IEC 61850 Report Control Block scalar field to the exact MMS
/// value kind used on the wire. This is the semantic boundary between reporting
/// policy and MMS encoding: callers request an IEC field/value, not a raw MMS scalar.
///
/// The contract intentionally covers only scalar mutation fields whose semantics are
/// stable across BRCB/URCB implementations. Bit-string and object-reference fields keep
/// their dedicated codecs.
/// </summary>
public readonly record struct MmsReportControlSemanticWrite
{
    private MmsReportControlSemanticWrite(
        string attribute,
        MmsDataValue value,
        MmsDataKind expectedKind,
        string expectedMmsType)
    {
        Attribute = attribute;
        Value = value;
        ExpectedKind = expectedKind;
        ExpectedMmsType = expectedMmsType;
    }

    public string Attribute { get; }
    public MmsDataValue Value { get; }
    public MmsDataKind ExpectedKind { get; }
    public string ExpectedMmsType { get; }

    public static MmsReportControlSemanticWrite ReportEnable(bool enabled)
        => Boolean("RptEna", enabled);

    public static MmsReportControlSemanticWrite GeneralInterrogation(bool requested)
        => Boolean("GI", requested);

    public static MmsReportControlSemanticWrite Reservation(bool reserved)
        => Boolean("Resv", reserved);

    /// <summary>
    /// IEC 61850 BRCB ResvTms is mapped to MMS INTEGER with signed INT16 wire semantics.
    /// Policy about particular reservation-time values belongs above this codec; this
    /// boundary validates only the standardized scalar representation and width.
    /// </summary>
    public static MmsReportControlSemanticWrite ReservationTime(int seconds)
    {
        if (seconds is < short.MinValue or > short.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                seconds,
                $"BRCB ResvTms must fit the signed IEC INT16 range {short.MinValue}..{short.MaxValue}.");
        }

        return new MmsReportControlSemanticWrite(
            "ResvTms",
            MmsDataValue.Integer(seconds),
            MmsDataKind.Integer,
            "integer");
    }

    /// <summary>
    /// Checks exact live MMS TypeSpecification compatibility without guessing or coercing.
    /// This is a building block for the bounded type-aware mutation preflight.
    /// </summary>
    public bool IsCompatibleWith(MmsTypeSpecificationNode? liveType)
        => liveType is not null &&
           Value.Kind == ExpectedKind &&
           string.Equals(liveType.MmsType, ExpectedMmsType, StringComparison.OrdinalIgnoreCase);

    private static MmsReportControlSemanticWrite Boolean(string attribute, bool value)
        => new(
            attribute,
            MmsDataValue.Boolean(value),
            MmsDataKind.Boolean,
            "boolean");
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Normal RCB scalar mutation path. Attribute identity and wire type travel together
    /// so reporting business logic cannot accidentally pair ResvTms with MMS Unsigned,
    /// or a boolean RCB field with a numeric encoder.
    /// </summary>
    private Task<MmsReportAttributeWriteStep> WriteReportSemanticAttributeAsync(
        MmsReportControlCandidate rcb,
        MmsReportControlSemanticWrite write,
        CancellationToken cancellationToken)
        => WriteReportAttributeAsync(rcb, write.Attribute, write.Value, cancellationToken);

    /// <summary>
    /// Cleanup counterpart to <see cref="WriteReportSemanticAttributeAsync"/>.
    /// </summary>
    private Task<MmsReportAttributeWriteStep> TryWriteReportSemanticAttributeForCleanupAsync(
        MmsReportControlCandidate rcb,
        MmsReportControlSemanticWrite write,
        CancellationToken cancellationToken)
        => TryWriteReportAttributeForCleanupAsync(rcb, write.Attribute, write.Value, cancellationToken);
}
