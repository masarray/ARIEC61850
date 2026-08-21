namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Exact local IP address bound by the TCP socket for the current MMS association.
    /// Empty when no TCP socket is connected.
    /// </summary>
    public string LocalTcpAddress => _tpkt.LocalAddress;
}
