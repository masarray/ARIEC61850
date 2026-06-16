using AR.Iec61850.Scl;

namespace AR.Iec61850.SvPublisher.Models;

public enum InjectionMode
{
    Manual,
    Ramp,
    Sequencer
}

public enum SvSyncPolicyMode
{
    AutoPtp,
    ForceUnsynchronized,
    ForceLocal,
    ForceGlobal
}

public enum PtpPublisherMode
{
    MonitorOnly,
    LabPublisher
}

public sealed class SvStreamChoice
{
    public required int Index { get; init; }
    public required SclSampledValuesStream Stream { get; init; }

    public string Label =>
        $"#{Index} {Stream.ControlBlockReference}  svID={TextOrDash(Stream.SvId)}  entries={Stream.Entries.Count}";

    private static string TextOrDash(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;
}


public sealed class RampSignalChoice
{
    public required string KeyCsv { get; init; }
    public required string Name { get; init; }
    public string Quantity { get; init; } = "Magnitude";
    public string Unit { get; init; } = string.Empty;

    public IReadOnlyList<string> Keys =>
        KeyCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public bool AppliesTo(string channelKey) =>
        Keys.Any(key => string.Equals(key, channelKey, StringComparison.OrdinalIgnoreCase));
}

public sealed class AdapterChoice
{
    public required string Selector { get; init; }
    public required string DisplayName { get; init; }
    public string MacAddress { get; init; } = string.Empty;
}

public sealed class SignalChannelSnapshot
{
    public string Key { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public double Magnitude { get; init; }
    public double AngleDegrees { get; init; }
    public double FrequencyHz { get; init; }
}

public sealed class SequenceStateSnapshot
{
    public string Name { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public double CurrentScale { get; init; } = 1;
    public double VoltageScale { get; init; } = 1;
    public double AngleShiftDegrees { get; init; }
    public double FrequencyHz { get; init; } = 50;
}

public sealed class SvPublisherConfigSnapshot
{
    public string SclPath { get; init; } = string.Empty;
    public string StreamControlBlock { get; init; } = string.Empty;
    public string StreamId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public string DestinationMac { get; init; } = string.Empty;
    public bool UseVlan { get; init; }
    public int VlanId { get; init; }
    public int VlanPriority { get; init; }
    public string SourceMac { get; init; } = string.Empty;
    public double SampleRateHz { get; init; }
    public double NominalFrequencyHz { get; init; }
    public double CurrentDlsb { get; init; }
    public double VoltageDlsb { get; init; }
    public double DurationSeconds { get; init; }
    public bool Continuous { get; init; }
    public InjectionMode Mode { get; init; }
    public string ManualSetMode { get; init; } = "Direct";
    public bool AutoApplyWhileRunning { get; init; } = true;
    public bool LinkFrequencies { get; init; } = true;
    public SvSyncPolicyMode SyncPolicyMode { get; init; } = SvSyncPolicyMode.AutoPtp;
    public int ExpectedPtpDomain { get; init; }
    public bool PtpAllowLocalFallback { get; init; } = true;
    public PtpPublisherMode PtpPublisherMode { get; init; } = PtpPublisherMode.MonitorOnly;
    public string PtpClockIdentity { get; init; } = "02:00:00:FF:FE:00:00:01";
    public int PtpAnnounceIntervalMs { get; init; } = 1000;
    public int PtpSyncIntervalMs { get; init; } = 250;
    public bool PtpRespondToPeerDelay { get; init; } = true;
    public string RampSignalKey { get; init; } = string.Empty;
    public double RampTargetMagnitude { get; init; }
    public double RampDurationSeconds { get; init; }
    public IReadOnlyList<SignalChannelSnapshot> Channels { get; init; } = Array.Empty<SignalChannelSnapshot>();
    public IReadOnlyList<SequenceStateSnapshot> SequenceStates { get; init; } = Array.Empty<SequenceStateSnapshot>();
}
