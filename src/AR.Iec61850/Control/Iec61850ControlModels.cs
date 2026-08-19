using AR.Iec61850.Mms;

namespace AR.Iec61850.Control;

public enum Iec61850ControlModel
{
    StatusOnly = 0,
    DirectNormal = 1,
    SelectBeforeOperateNormal = 2,
    DirectEnhanced = 3,
    SelectBeforeOperateEnhanced = 4,
    Unknown = 255
}

public enum Iec61850ControlAction
{
    Select,
    SelectWithValue,
    Operate,
    Cancel
}

public enum Iec61850ControlCompletionState
{
    NotStarted,
    Rejected,
    Accepted,
    PositiveTermination,
    NegativeTermination,
    TimedOut,
    AssociationLost,
    Cancelled,
    Unsupported
}

public enum Iec61850ControlStatusState
{
    Unknown,
    Intermediate,
    Open,
    Closed,
    Off,
    On,
    Bad,
    Numeric
}

public enum Iec61850OriginCategory
{
    NotSupported = 0,
    BayControl = 1,
    StationControl = 2,
    RemoteControl = 3,
    AutomaticBay = 4,
    AutomaticStation = 5,
    AutomaticRemote = 6,
    Maintenance = 7,
    Process = 8
}

public enum Iec61850DoublePointValue
{
    Intermediate = 0,
    Off = 1,
    On = 2,
    Bad = 3
}

public enum Iec61850ControlValueKind
{
    Boolean,
    DoublePoint,
    Integer,
    Unsigned,
    FloatingPoint,
    StepPosition,
    RawMms
}

public sealed class Iec61850ControlValue
{
    private Iec61850ControlValue(Iec61850ControlValueKind kind, object value)
    {
        Kind = kind;
        Value = value;
    }

    public Iec61850ControlValueKind Kind { get; }
    public object Value { get; }

    public static Iec61850ControlValue Boolean(bool value) => new(Iec61850ControlValueKind.Boolean, value);
    public static Iec61850ControlValue On() => Boolean(true);
    public static Iec61850ControlValue Off() => Boolean(false);
    public static Iec61850ControlValue Open() => DoublePoint(Iec61850DoublePointValue.Off);
    public static Iec61850ControlValue Close() => DoublePoint(Iec61850DoublePointValue.On);
    public static Iec61850ControlValue DoublePoint(Iec61850DoublePointValue value) => new(Iec61850ControlValueKind.DoublePoint, value);
    public static Iec61850ControlValue Integer(long value) => new(Iec61850ControlValueKind.Integer, value);
    public static Iec61850ControlValue Raise() => Integer(1);
    public static Iec61850ControlValue Lower() => Integer(-1);
    public static Iec61850ControlValue Unsigned(ulong value) => new(Iec61850ControlValueKind.Unsigned, value);
    public static Iec61850ControlValue Analogue(double value) => new(Iec61850ControlValueKind.FloatingPoint, value);
    public static Iec61850ControlValue StepPosition(long position, bool transient = false) => new(Iec61850ControlValueKind.StepPosition, new Iec61850StepPosition(position, transient));
    public static Iec61850ControlValue Raw(MmsDataValue value) => new(Iec61850ControlValueKind.RawMms, value ?? throw new ArgumentNullException(nameof(value)));

    public string Fingerprint => Kind switch
    {
        Iec61850ControlValueKind.StepPosition when Value is Iec61850StepPosition step => $"Step:{step.Position}:{step.Transient}",
        Iec61850ControlValueKind.RawMms when Value is MmsDataValue raw => $"Raw:{MmsDataValueRenderer.ToCompactString(raw)}",
        _ => $"{Kind}:{Value}"
    };
}

public readonly record struct Iec61850StepPosition(long Position, bool Transient);

public sealed class Iec61850Origin
{
    public Iec61850OriginCategory Category { get; init; } = Iec61850OriginCategory.StationControl;
    public byte[] Identifier { get; init; } = System.Text.Encoding.ASCII.GetBytes("ARIEC61850");

    public static Iec61850Origin FromText(
        string identifier,
        Iec61850OriginCategory category = Iec61850OriginCategory.StationControl)
        => new()
        {
            Category = category,
            Identifier = System.Text.Encoding.ASCII.GetBytes(identifier ?? string.Empty)
        };

    internal string Fingerprint => $"{(int)Category}:{Convert.ToHexString(Identifier)}";
}

public sealed class Iec61850ControlRequest
{
    public required Iec61850ControlValue ControlValue { get; init; }
    public Iec61850Origin Origin { get; init; } = new();
    public byte? ControlNumber { get; init; }
    public bool Test { get; init; }
    public bool InterlockCheck { get; init; }
    public bool SynchroCheck { get; init; }
    public DateTimeOffset? OperateAtUtc { get; init; }
    public bool AutoSelect { get; init; } = true;
    public TimeSpan? CommandTerminationTimeout { get; init; }

    internal string SequenceFingerprint => string.Join('|',
        ControlValue.Fingerprint,
        Origin.Fingerprint,
        ControlNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "auto",
        Test,
        InterlockCheck,
        SynchroCheck,
        OperateAtUtc?.ToUniversalTime().ToString("O") ?? "immediate");
}

public sealed class Iec61850ControlObjectDescriptor
{
    public string ObjectReference { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public Iec61850ControlModel ControlModel { get; init; } = Iec61850ControlModel.Unknown;
    public MmsTypeSpecificationNode CtlValSpecification { get; init; } = new();
    public MmsTypeSpecificationNode OperSpecification { get; init; } = new();
    public MmsTypeSpecificationNode? SelectWithValueSpecification { get; init; }
    public MmsTypeSpecificationNode? CancelSpecification { get; init; }
    public string StatusReference { get; init; } = string.Empty;
    public string StatusFunctionalConstraint { get; init; } = string.Empty;
    public TimeSpan? SboTimeout { get; init; }
    public TimeSpan? OperTimeout { get; init; }
    public bool SupportsTimeActivatedOperate { get; init; }
    public bool SupportsCommandTermination { get; init; }
    public string DiscoveryEvidence { get; init; } = string.Empty;

    internal Iec61850ControlObjectReferences References { get; init; } = null!;

    public bool IsOperationallyReady =>
        ControlModel is not Iec61850ControlModel.StatusOnly and not Iec61850ControlModel.Unknown &&
        !string.IsNullOrWhiteSpace(ObjectReference) &&
        !string.IsNullOrWhiteSpace(CtlValSpecification.MmsType) &&
        (!RequiresSelect || CancelSpecification != null) &&
        (ControlModel != Iec61850ControlModel.SelectBeforeOperateEnhanced || SelectWithValueSpecification != null) &&
        (!IsEnhanced || SupportsCommandTermination);

    public bool IsEnhanced => ControlModel is Iec61850ControlModel.DirectEnhanced or Iec61850ControlModel.SelectBeforeOperateEnhanced;
    public bool RequiresSelect => ControlModel is Iec61850ControlModel.SelectBeforeOperateNormal or Iec61850ControlModel.SelectBeforeOperateEnhanced;
}

public sealed class Iec61850ControlStatusResult
{
    public bool IsSuccess { get; init; }
    public string Reference { get; init; } = string.Empty;
    public Iec61850ControlStatusState State { get; init; } = Iec61850ControlStatusState.Unknown;
    public string DisplayValue { get; init; } = "Unknown";
    public string Message { get; init; } = string.Empty;
    public MmsDataValue? RawValue { get; init; }
    public string ResponseHex { get; init; } = string.Empty;
    public DateTimeOffset ReadAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Iec61850ControlWireStep
{
    public Iec61850ControlAction Action { get; init; }
    public string Reference { get; init; } = string.Empty;
    public bool RequestAccepted { get; init; }
    public string RequestHex { get; init; } = string.Empty;
    public string ResponseHex { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class Iec61850ControlActionResult
{
    public Iec61850ControlAction Action { get; init; }
    public Iec61850ControlCompletionState CompletionState { get; init; }
    public bool RequestAccepted { get; init; }
    public bool CommandTerminationReceived { get; init; }
    public bool PositiveTermination { get; init; }
    public string ClientError { get; init; } = string.Empty;
    public string ControlError { get; init; } = string.Empty;
    public string AddCause { get; init; } = string.Empty;
    public string LastApplErrorText { get; init; } = string.Empty;
    public string RequestHex { get; init; } = string.Empty;
    public string ResponseHex { get; init; } = string.Empty;
    public byte? ControlNumber { get; init; }
    public DateTimeOffset? SequenceTimestamp { get; init; }
    public TimeSpan Elapsed { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Ordered wire-service evidence for the complete control transaction. For an
    /// auto-selected SBO sequence this contains the Select/SBOw step before Operate,
    /// rather than exposing only the last request on the association.
    /// </summary>
    public IReadOnlyList<Iec61850ControlWireStep> WireSteps { get; init; } = Array.Empty<Iec61850ControlWireStep>();

    public bool IsSuccess => RequestAccepted &&
        CompletionState is Iec61850ControlCompletionState.Accepted or Iec61850ControlCompletionState.PositiveTermination;
}

public sealed class Iec61850ControlServiceOptions
{
    public TimeSpan DefaultSboTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan DefaultOperateTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ApplicationErrorGracePeriod { get; init; } = TimeSpan.FromMilliseconds(400);
    public bool RequireExactNamedControlFields { get; init; } = true;
}

public interface IIec61850ControlService
{
    Task<Iec61850ControlObjectSession> OpenAsync(
        MmsClientSession session,
        string objectReference,
        CancellationToken cancellationToken = default);
}
