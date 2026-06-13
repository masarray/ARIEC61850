namespace AR.Iec61850.Monitoring;

public enum ProcessBusSequenceStatus
{
    Unknown,
    MissingSampleCount,
    First,
    InSequence,
    Wrapped,
    Duplicate,
    Jump,
    OutOfOrder
}
