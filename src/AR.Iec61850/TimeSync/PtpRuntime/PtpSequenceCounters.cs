
using AR.Iec61850.TimeSync.Ptp;

namespace AR.Iec61850.TimeSync.PtpRuntime;

internal sealed class PtpSequenceCounters
{
    private readonly Dictionary<PtpMessageType, ushort> _counters = new();

    public ushort Next(PtpMessageType messageType)
    {
        _counters.TryGetValue(messageType, out var current);
        var next = unchecked((ushort)(current + 1));
        _counters[messageType] = next;
        return next;
    }
}
