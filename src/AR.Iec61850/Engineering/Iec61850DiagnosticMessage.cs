namespace AR.Iec61850.Engineering;

public enum Iec61850DiagnosticSeverity
{
    Info,
    Advisory,
    Warning,
    Error
}

public sealed class Iec61850DiagnosticMessage
{
    public Iec61850DiagnosticSeverity Severity { get; init; } = Iec61850DiagnosticSeverity.Info;
    public string Code { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;

    public string Summary
    {
        get
        {
            var reference = string.IsNullOrWhiteSpace(Reference) ? string.Empty : $" [{Reference}]";
            var recommendation = string.IsNullOrWhiteSpace(Recommendation) ? string.Empty : $" Recommended action: {Recommendation}";
            return $"{Severity} {Code}{reference}: {Message}{recommendation}";
        }
    }
}
