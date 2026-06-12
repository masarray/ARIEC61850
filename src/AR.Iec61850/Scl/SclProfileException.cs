namespace AR.Iec61850.Scl;

public sealed class SclProfileException : InvalidOperationException
{
    public SclProfileException(string message)
        : base(message)
    {
    }
}
