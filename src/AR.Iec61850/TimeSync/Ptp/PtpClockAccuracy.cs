namespace AR.Iec61850.TimeSync.Ptp;

public enum PtpClockAccuracy : byte
{
    Within25Ns = 0x20,
    Within100Ns = 0x21,
    Within250Ns = 0x22,
    Within1Us = 0x23,
    Within2_5Us = 0x24,
    Within10Us = 0x25,
    Within25Us = 0x26,
    Within100Us = 0x27,
    Within250Us = 0x28,
    Within1Ms = 0x29,
    Within2_5Ms = 0x2A,
    Within10Ms = 0x2B,
    GreaterThan10s = 0x31,
    Unknown = 0xFE
}
