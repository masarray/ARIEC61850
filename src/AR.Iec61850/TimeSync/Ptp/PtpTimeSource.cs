namespace AR.Iec61850.TimeSync.Ptp;

public enum PtpTimeSource : byte
{
    AtomicClock = 0x10,
    Gps = 0x20,
    TerrestrialRadio = 0x30,
    Ptp = 0x40,
    Ntp = 0x50,
    HandSet = 0x60,
    Other = 0x90,
    InternalOscillator = 0xA0
}
