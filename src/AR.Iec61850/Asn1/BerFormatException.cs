namespace AR.Iec61850.Asn1;

public sealed class BerFormatException : FormatException
{
    public BerFormatException(string message)
        : base(message)
    {
    }
}
