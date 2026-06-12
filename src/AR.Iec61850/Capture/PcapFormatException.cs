namespace AR.Iec61850.Capture;

public sealed class PcapFormatException : FormatException
{
    public PcapFormatException(string message)
        : base(message)
    {
    }
}
