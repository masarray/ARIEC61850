namespace AR.Iec61850.Mms;

/// <summary>
/// Protocol-level failure categories used by planning/execution code. These are derived
/// from structured MMS DataAccessError codes, never from human-readable message text.
/// </summary>
public enum MmsInteropFailureKind
{
    None,
    ObjectInvalidated,
    HardwareFault,
    TemporarilyUnavailable,
    AccessDenied,
    ObjectUndefined,
    InvalidAddress,
    TypeUnsupported,
    TypeMismatch,
    AttributeInconsistent,
    AccessUnsupported,
    ObjectNotFound,
    InvalidValue,
    Unknown
}

/// <summary>
/// Evidence-oriented recovery hint. This does not authorize an automatic retry; it tells
/// higher layers what must be revalidated before any bounded follow-up operation.
/// </summary>
public enum MmsInteropRecoveryHint
{
    None,
    RevalidateType,
    RevalidateRuntimeState,
    RevalidateObjectIdentity,
    RetryLaterAfterStateRefresh,
    FailClosed
}

public readonly record struct MmsInteropFailureClassification(
    MmsInteropFailureKind Kind,
    MmsInteropRecoveryHint RecoveryHint)
{
    public static MmsInteropFailureClassification Success =>
        new(MmsInteropFailureKind.None, MmsInteropRecoveryHint.None);
}

public static class MmsInteropFailureClassifier
{
    public static MmsInteropFailureClassification Classify(MmsWriteAccessResult? access)
    {
        if (access is null || access.IsSuccess)
            return MmsInteropFailureClassification.Success;

        return access.FailureCode switch
        {
            0 => new(MmsInteropFailureKind.ObjectInvalidated, MmsInteropRecoveryHint.RevalidateRuntimeState),
            1 => new(MmsInteropFailureKind.HardwareFault, MmsInteropRecoveryHint.FailClosed),
            2 => new(MmsInteropFailureKind.TemporarilyUnavailable, MmsInteropRecoveryHint.RetryLaterAfterStateRefresh),
            3 => new(MmsInteropFailureKind.AccessDenied, MmsInteropRecoveryHint.FailClosed),
            4 => new(MmsInteropFailureKind.ObjectUndefined, MmsInteropRecoveryHint.RevalidateObjectIdentity),
            5 => new(MmsInteropFailureKind.InvalidAddress, MmsInteropRecoveryHint.RevalidateObjectIdentity),
            6 => new(MmsInteropFailureKind.TypeUnsupported, MmsInteropRecoveryHint.RevalidateType),
            7 => new(MmsInteropFailureKind.TypeMismatch, MmsInteropRecoveryHint.RevalidateType),
            8 => new(MmsInteropFailureKind.AttributeInconsistent, MmsInteropRecoveryHint.RevalidateRuntimeState),
            9 => new(MmsInteropFailureKind.AccessUnsupported, MmsInteropRecoveryHint.FailClosed),
            10 => new(MmsInteropFailureKind.ObjectNotFound, MmsInteropRecoveryHint.RevalidateObjectIdentity),
            11 => new(MmsInteropFailureKind.InvalidValue, MmsInteropRecoveryHint.FailClosed),
            _ => new(MmsInteropFailureKind.Unknown, MmsInteropRecoveryHint.FailClosed)
        };
    }
}
