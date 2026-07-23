using AR.Iec61850.Ethernet;
using AR.Iec61850.Scl;

namespace AR.Iec61850.SampledValues;

public enum SampledValuesPublisherValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record SampledValuesPublisherValidationFinding(
    SampledValuesPublisherValidationSeverity Severity,
    string Code,
    string Message,
    string Detail = "");

public sealed class SampledValuesPublisherValidationReport
{
    public SampledValuesPublisherValidationReport(IReadOnlyList<SampledValuesPublisherValidationFinding> findings)
    {
        Findings = findings;
    }

    public IReadOnlyList<SampledValuesPublisherValidationFinding> Findings { get; }
    public bool HasErrors => Findings.Any(f => f.Severity == SampledValuesPublisherValidationSeverity.Error);
    public int ErrorCount => Findings.Count(f => f.Severity == SampledValuesPublisherValidationSeverity.Error);
    public int WarningCount => Findings.Count(f => f.Severity == SampledValuesPublisherValidationSeverity.Warning);
    public int InfoCount => Findings.Count(f => f.Severity == SampledValuesPublisherValidationSeverity.Info);
}

public static class SampledValuesPublisherValidator
{
    public static SampledValuesPublisherValidationReport Validate(SclSampledValuesStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var findings = new List<SampledValuesPublisherValidationFinding>();
        Add(findings, SampledValuesPublisherValidationSeverity.Info, "SV_STREAM", stream.ControlBlockReference, $"svID={Text(stream.SvId)}, datSet={Text(stream.DataSetReference)}");

        if (!stream.Address.AppId.HasValue)
            Add(findings, SampledValuesPublisherValidationSeverity.Error, "SV_APPID_MISSING", "APPID is missing.", "Add Communication/SubNetwork/ConnectedAP/SMV/Address/P type=APPID.");
        else if (stream.Address.AppId.Value == 0)
            Add(findings, SampledValuesPublisherValidationSeverity.Warning, "SV_APPID_ZERO", "APPID is 0x0000.", "Use the APPID expected by the subscriber unless this is intentional.");
        if (!stream.Address.DestinationMac.HasValue)
            Add(findings, SampledValuesPublisherValidationSeverity.Error, "SV_DEST_MAC_MISSING", "Destination MAC is missing or invalid.", stream.Address.DestinationMacText);
        else
            ValidateDestinationMac(stream.Address.DestinationMac.Value, findings);

        if (string.IsNullOrWhiteSpace(stream.SvId))
            Add(findings, SampledValuesPublisherValidationSeverity.Error, "SV_ID_MISSING", "svID/smvID is missing.");
        if (string.IsNullOrWhiteSpace(stream.DataSetReference) || stream.Entries.Count == 0)
            Add(findings, SampledValuesPublisherValidationSeverity.Error, "SV_DATASET_MISSING", "DataSet cannot be resolved.", stream.DataSetName);

        if (stream.ConfigurationRevision == 0)
            Add(findings, SampledValuesPublisherValidationSeverity.Warning, "SV_CONFREV_ZERO", "confRev is 0.", "Most engineering files use a positive configuration revision.");
        if (stream.SampleRate == 0)
            Add(findings, SampledValuesPublisherValidationSeverity.Warning, "SV_SMPRATE_MISSING", "smpRate is missing or 0.", "The operator must select an explicit sample rate in the publisher application.");

        var noAsdu = stream.NoAsdu == 0 ? (ushort)1 : stream.NoAsdu;
        if (noAsdu > SampledValuesPublisherProfile.MaxAsduPerFrame)
            Add(findings, SampledValuesPublisherValidationSeverity.Error, "SV_NOFASDU_UNSUPPORTED", $"nofASDU={noAsdu} is above the supported limit.", $"This publisher supports 1..{SampledValuesPublisherProfile.MaxAsduPerFrame} ASDU(s) per frame.");
        else if (noAsdu > 1)
            Add(findings, SampledValuesPublisherValidationSeverity.Info, "SV_NOFASDU_PACKING", $"nofASDU={noAsdu} ASDU(s) per Ethernet frame.", "The publisher will pack sequential samples into one SavPdu.");

        var layout = SampledValuesPayloadLayout.FromDataSet(stream.Entries);
        if (!layout.IsFullySupported)
            Add(findings, SampledValuesPublisherValidationSeverity.Error, "SV_PAYLOAD_UNSUPPORTED", "Unsupported SV payload layout.", string.Join("; ", layout.UnsupportedElements.Select(x => $"{x.SignalReference} bType={x.BType}")));
        else
            Add(findings, SampledValuesPublisherValidationSeverity.Info, "SV_PAYLOAD_LAYOUT", "Payload layout supported.", $"entries={stream.Entries.Count}, payload={layout.PayloadByteLength} bytes per ASDU.");

        return new SampledValuesPublisherValidationReport(findings);
    }

    private static void ValidateDestinationMac(MacAddress mac, List<SampledValuesPublisherValidationFinding> findings)
    {
        var bytes = mac.ToArray();
        if (bytes.All(value => value == 0))
            Add(findings, SampledValuesPublisherValidationSeverity.Error, "SV_DEST_MAC_ZERO", "Destination MAC is all zeros.", mac.ToString());
        else if (bytes.All(value => value == 0xFF))
            Add(findings, SampledValuesPublisherValidationSeverity.Error, "SV_DEST_MAC_BROADCAST", "Destination MAC should not be broadcast for SV.", mac.ToString());
        else if ((bytes[0] & 0x01) == 0)
            Add(findings, SampledValuesPublisherValidationSeverity.Warning, "SV_DEST_MAC_UNICAST", "Destination MAC is not multicast.", mac.ToString());
        else if (!(bytes[0] == 0x01 && bytes[1] == 0x0C && bytes[2] == 0xCD && bytes[3] == 0x04))
            Add(findings, SampledValuesPublisherValidationSeverity.Warning, "SV_DEST_MAC_RANGE", "Destination MAC is multicast but outside the common SV multicast range.", mac.ToString());
    }

    private static void Add(
        List<SampledValuesPublisherValidationFinding> findings,
        SampledValuesPublisherValidationSeverity severity,
        string code,
        string message,
        string detail = "")
        => findings.Add(new SampledValuesPublisherValidationFinding(severity, code, message, detail));

    private static string Text(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
}
