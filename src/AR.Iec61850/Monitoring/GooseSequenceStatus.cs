namespace AR.Iec61850.Monitoring;

public enum GooseSequenceStatus
{
    Unknown,
    First,
    Retransmission,
    StateChange,
    Duplicate,
    SequenceJump,
    SequenceRegression,
    StateRegression
}
