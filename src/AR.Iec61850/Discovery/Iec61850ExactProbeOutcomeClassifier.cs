using AR.Iec61850.Mms;

namespace AR.Iec61850.Discovery;

/// <summary>
/// Owns the protocol-evidence boundary between raw MMS Confirmed-Read results and
/// reconciliation probe outcomes. Consumers must not reinterpret MMS failure codes.
/// </summary>
public static class Iec61850ExactProbeOutcomeClassifier
{
    public static Iec61850ExactProbeStatus Classify(
        MmsReadResult result,
        bool sessionInitiatedAfterRead)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
            return Iec61850ExactProbeStatus.Readable;

        if (!sessionInitiatedAfterRead)
            return Iec61850ExactProbeStatus.TransportFailure;

        return result.FailureCode switch
        {
            4 or 10 => Iec61850ExactProbeStatus.Absent,
            5 => Iec61850ExactProbeStatus.InvalidTarget,
            _ => Iec61850ExactProbeStatus.Unreadable
        };
    }
}
