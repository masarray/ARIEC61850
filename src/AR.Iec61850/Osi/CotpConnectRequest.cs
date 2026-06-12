namespace AR.Iec61850.Osi;

public static class CotpConnectRequest
{
    public static byte[] BuildDefault()
    {
        return
        [
            0x11,
            0xE0,
            0x00, 0x00,
            0x00, 0x01,
            0x00,
            0xC0, 0x01, 0x0A,
            0xC1, 0x02, 0x00, 0x01,
            0xC2, 0x02, 0x00, 0x01
        ];
    }
}
