namespace AR.Iec61850.Engineering;

public enum Iec61850CapabilityStatus
{
    NotAssessed,
    Ready,
    Partial,
    Blocked
}

public sealed class Iec61850CapabilityAssessment
{
    public string Area { get; init; } = string.Empty;
    public Iec61850CapabilityStatus Status { get; init; } = Iec61850CapabilityStatus.NotAssessed;
    public string Evidence { get; init; } = string.Empty;
    public string NextAction { get; init; } = string.Empty;

    public string Summary => $"{Area}: {Status} - {Evidence}" + (string.IsNullOrWhiteSpace(NextAction) ? string.Empty : $" Next: {NextAction}");
}
