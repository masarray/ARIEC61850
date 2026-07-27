namespace AR.Iec61850.SampledValues.Profiles;

public enum SvComparisonMode { Strict, Compatible }
public enum SvConfigurationFindingSeverity { Info, Warning, Error }

public sealed record SvExpectedStreamConfiguration
{
    public ushort? EtherType { get; init; }
    public ushort? AppId { get; init; }
    public string DestinationMac { get; init; } = string.Empty;
    public ushort? VlanId { get; init; }
    public byte? VlanPriority { get; init; }
    public string SvId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public uint? ConfigurationRevision { get; init; }
    public int? AsduPerFrame { get; init; }
    public int? PayloadBytesPerAsdu { get; init; }
    public ushort? DeclaredSampleRate { get; init; }
    public ushort? DeclaredSampleMode { get; init; }
    public IReadOnlyList<SvDatasetElementSignature> DataSetSignature { get; init; } = Array.Empty<SvDatasetElementSignature>();
}

public sealed record SvConfigurationFinding(
    SvConfigurationFindingSeverity Severity,
    string Code,
    string Field,
    string Expected,
    string Observed,
    string Message);

public sealed record SvConfigurationComparisonResult
{
    public SvComparisonMode Mode { get; init; }
    public IReadOnlyList<SvConfigurationFinding> Findings { get; init; } = Array.Empty<SvConfigurationFinding>();
    public bool HasBlockingErrors => Findings.Any(item => item.Severity == SvConfigurationFindingSeverity.Error);
    public int InfoCount => Findings.Count(item => item.Severity == SvConfigurationFindingSeverity.Info);
    public int ErrorCount => Findings.Count(item => item.Severity == SvConfigurationFindingSeverity.Error);
    public int WarningCount => Findings.Count(item => item.Severity == SvConfigurationFindingSeverity.Warning);
    public bool IsExactMatch => Findings.Count == 0;
    public string Summary => IsExactMatch ? "Exact" : ErrorCount > 0 ? CountText(ErrorCount, "error") : WarningCount > 0 ? CountText(WarningCount, "warning") : CountText(InfoCount, "info");
    private static string CountText(int count, string label) => $"{count} {label}{(count == 1 ? string.Empty : "s")}";
}

public sealed class SvConfigurationComparer
{
    public SvConfigurationComparisonResult Compare(SvExpectedStreamConfiguration expected, SvObservedStreamFacts observed, SvComparisonMode mode)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);
        var findings = new List<SvConfigurationFinding>();
        CompareNullable("SV_ETHERTYPE", "EtherType", expected.EtherType, observed.EtherType, value => $"0x{value:X4}", mode, findings);
        CompareNullable("SV_APPID", "APPID", expected.AppId, observed.AppId, value => $"0x{value:X4}", mode, findings);
        CompareText("SV_DEST_MAC", "Destination MAC", expected.DestinationMac, observed.DestinationMac, NormalizeMacAddress, mode, findings);
        CompareNullable("SV_VLAN_ID", "VLAN ID", expected.VlanId, observed.VlanId, value => value.ToString(), mode, findings);
        CompareNullable("SV_VLAN_PRIORITY", "VLAN priority", expected.VlanPriority, observed.VlanPriority, value => value.ToString(), mode, findings);
        CompareText("SV_ID", "svID", expected.SvId, observed.SvId, NormalizeIdentifier, mode, findings);
        CompareText("SV_DATASET", "Dataset reference", expected.DataSetReference, observed.DataSetReference, NormalizeIdentifier, mode, findings);
        CompareNullable("SV_CONFREV", "confRev", expected.ConfigurationRevision, observed.ConfigurationRevision, value => value.ToString(), mode, findings);
        CompareNullable("SV_ASDU_COUNT", "ASDU per frame", expected.AsduPerFrame, observed.AsduPerFrame, value => value.ToString(), mode, findings);
        CompareNullable("SV_PAYLOAD_LENGTH", "Payload bytes per ASDU", expected.PayloadBytesPerAsdu, observed.PayloadBytesPerAsdu, value => value.ToString(), mode, findings);
        CompareNullable("SV_SAMPLE_RATE", "Declared sample rate", expected.DeclaredSampleRate, observed.DeclaredSampleRate, value => value.ToString(), mode, findings);
        CompareNullable("SV_SAMPLE_MODE", "Declared sample mode", expected.DeclaredSampleMode, observed.DeclaredSampleMode, value => value.ToString(), mode, findings);
        CompareSignature(expected.DataSetSignature, observed.DataSetSignature, mode, findings);
        return new SvConfigurationComparisonResult { Mode = mode, Findings = findings };
    }

    private static void CompareText(string code, string field, string expected, string observed, Func<string,string> normalize, SvComparisonMode mode, List<SvConfigurationFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(expected)) return;
        if (string.IsNullOrWhiteSpace(observed)) { findings.Add(Missing(code, field, expected, mode)); return; }
        if (!string.Equals(normalize(expected), normalize(observed), StringComparison.Ordinal)) findings.Add(Mismatch(code, field, expected, observed, mode));
    }

    private static void CompareNullable<T>(string code, string field, T? expected, T? observed, Func<T,string> format, SvComparisonMode mode, List<SvConfigurationFinding> findings) where T : struct, IEquatable<T>
    {
        if (!expected.HasValue) return;
        if (!observed.HasValue) { findings.Add(Missing(code, field, format(expected.Value), mode)); return; }
        if (!expected.Value.Equals(observed.Value)) findings.Add(Mismatch(code, field, format(expected.Value), format(observed.Value), mode));
    }

    private static void CompareSignature(IReadOnlyList<SvDatasetElementSignature> expected, IReadOnlyList<SvDatasetElementSignature> observed, SvComparisonMode mode, List<SvConfigurationFinding> findings)
    {
        if (expected.Count == 0) return;
        if (observed.Count == 0) { findings.Add(Missing("SV_DATASET_SIGNATURE", "Dataset signature", SignatureText(expected), mode)); return; }
        if (!expected.Select(ToKey).SequenceEqual(observed.Select(ToKey), StringComparer.Ordinal)) findings.Add(Mismatch("SV_DATASET_SIGNATURE", "Dataset signature", SignatureText(expected), SignatureText(observed), mode));
    }

    private static SvConfigurationFinding Missing(string code, string field, string expected, SvComparisonMode mode)
        => new(Severity(mode), $"{code}_MISSING", field, expected, "-", $"Observed traffic does not expose the expected {field}. Capture and decoding remain active.");
    private static SvConfigurationFinding Mismatch(string code, string field, string expected, string observed, SvComparisonMode mode)
        => new(Severity(mode), $"{code}_MISMATCH", field, expected, observed, $"Configured {field} differs from observed traffic. Capture and decoding remain active.");
    private static SvConfigurationFindingSeverity Severity(SvComparisonMode mode) => mode == SvComparisonMode.Strict ? SvConfigurationFindingSeverity.Error : SvConfigurationFindingSeverity.Warning;
    private static string NormalizeMacAddress(string value) => new((value ?? string.Empty).Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
    private static string NormalizeIdentifier(string value) => (value ?? string.Empty).Trim();
    private static string SignatureText(IReadOnlyList<SvDatasetElementSignature> signature) => string.Join(", ", signature.Select(item => item.NormalizedBType));
    private static string ToKey(SvDatasetElementSignature item) => $"{item.NormalizedBType}|{item.NormalizedCdc}|{item.IsQuality}|{item.IsTimestamp}";
}
