namespace AR.Iec61850.Mms;

/// <summary>
/// Evidence classification is orthogonal to MMS Data type: a failed access
/// never becomes a scalar, including when an expected SCL type exists.
/// </summary>
public enum MmsReadEvidenceFailureKind
{
    None,
    AccessDenied,
    NotSupported,
    TypeMismatch,
    ObjectMissing,
    TemporarilyUnavailable,
    RemoteAccessFailure,
    InvalidResponse,
    ProtocolRejected
}

public static class MmsReadEvidenceClassifier
{
    public static MmsReadEvidenceFailureKind FromAccessCode(int? code)
        => code switch
        {
            2 => MmsReadEvidenceFailureKind.TemporarilyUnavailable,
            3 => MmsReadEvidenceFailureKind.AccessDenied,
            4 or 5 or 10 => MmsReadEvidenceFailureKind.ObjectMissing,
            6 or 9 => MmsReadEvidenceFailureKind.NotSupported,
            7 or 8 => MmsReadEvidenceFailureKind.TypeMismatch,
            _ => MmsReadEvidenceFailureKind.RemoteAccessFailure
        };
}
