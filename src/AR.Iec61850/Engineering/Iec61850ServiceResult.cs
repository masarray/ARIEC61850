namespace AR.Iec61850.Engineering;

public sealed class Iec61850ServiceResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public IReadOnlyList<Iec61850DiagnosticMessage> Diagnostics { get; init; } = Array.Empty<Iec61850DiagnosticMessage>();
    public string Message { get; init; } = string.Empty;

    public static Iec61850ServiceResult<T> Success(T value, string message, IEnumerable<Iec61850DiagnosticMessage>? diagnostics = null)
        => new()
        {
            IsSuccess = true,
            Value = value,
            Message = message,
            Diagnostics = (diagnostics ?? Array.Empty<Iec61850DiagnosticMessage>()).ToArray()
        };

    public static Iec61850ServiceResult<T> Failure(string code, string message, string recommendation = "")
        => new()
        {
            IsSuccess = false,
            Message = message,
            Diagnostics =
            [
                new Iec61850DiagnosticMessage
                {
                    Severity = Iec61850DiagnosticSeverity.Error,
                    Code = code,
                    Message = message,
                    Recommendation = recommendation
                }
            ]
        };
}
