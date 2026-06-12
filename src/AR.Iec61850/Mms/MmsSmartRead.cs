namespace AR.Iec61850.Mms;

public sealed class MmsSmartReadResult
{
    public MmsFcResolveResult ResolveResult { get; init; } = new();
    public MmsReadResult ReadResult { get; init; } = new();
    public MmsFcResolvedPoint? SelectedPoint { get; init; }
    public bool IsSuccess => SelectedPoint != null && ReadResult.IsSuccess;
    public string Message => IsSuccess
        ? $"Smart read OK: {SelectedPoint!.UserReference} [{SelectedPoint.FunctionalConstraint}] = {FormatValue(ReadResult.Value)}"
        : ReadResult.Message.Length > 0 ? ReadResult.Message : ResolveResult.Message;

    private static string FormatValue(MmsDataValue? value)
        => value == null ? "-" : MmsDataCodec.ToDisplayString(value);
}

