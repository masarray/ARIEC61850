namespace AR.Iec61850.TimeSync.Health;

/// <summary>
/// IEC 61850 Sampled Values smpSynch recommendation derived from timing health.
/// </summary>
public enum SmpSynchValue : byte
{
    NotSynchronized = 0,
    LocalSynchronized = 1,
    GlobalSynchronized = 2
}
