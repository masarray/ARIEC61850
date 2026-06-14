using System.Text;
using System.Text.Json;
using AR.Iec61850.Acse;
using AR.Iec61850.Diagnostics;
using AR.Iec61850.Osi;

namespace AR.Iec61850.Simulation;

public sealed class MmsHandshakeCodecProfile
{
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool IsServerTransportReady { get; init; }
    public IReadOnlyList<MmsHandshakeCodecStep> Steps { get; init; } = Array.Empty<MmsHandshakeCodecStep>();
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MMS Handshake Codec Profile");
        sb.AppendLine();
        sb.AppendLine($"Created UTC: `{CreatedAtUtc:O}`");
        sb.AppendLine($"Server transport readiness: **{(IsServerTransportReady ? "READY" : "BLOCKED")}**");
        sb.AppendLine();
        sb.AppendLine("## Codec steps");
        sb.AppendLine();
        sb.AppendLine("| Step | Area | Result | Message |");
        sb.AppendLine("|---:|---|---|---|");
        foreach (var step in Steps)
            sb.AppendLine($"| {step.Index} | {Escape(step.Area)} | {(step.IsPass ? "PASS" : "FAIL")} | {Escape(step.Message)} |");

        sb.AppendLine();
        sb.AppendLine("## Findings");
        sb.AppendLine();
        if (Findings.Count == 0)
        {
            sb.AppendLine("- No blocking finding from the offline handshake codec profile.");
        }
        else
        {
            foreach (var finding in Findings)
                sb.AppendLine($"- {finding}");
        }

        return sb.ToString();
    }

    public string ToJson(JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(this, options ?? new JsonSerializerOptions { WriteIndented = true });

    private static string Escape(string value)
        => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}

public sealed class MmsHandshakeCodecStep
{
    public int Index { get; init; }
    public string Area { get; init; } = string.Empty;
    public bool IsPass { get; init; }
    public string Message { get; init; } = string.Empty;
    public string HexPreview { get; init; } = string.Empty;
}

public sealed class MmsHandshakeCodecProfileBuilder
{
    public MmsHandshakeCodecProfile BuildDefault()
    {
        var steps = new List<MmsHandshakeCodecStep>();
        var findings = new List<string>();
        var index = 1;

        var connectRequest = CotpFrameCodec.EncodeDefaultConnectRequest();
        var crTpkt = TpktFrameCodec.Encode(connectRequest);
        var decodedCrTpkt = TpktFrameCodec.Decode(crTpkt);
        AddStep(steps, index++, "TPKT", decodedCrTpkt.IsValid, decodedCrTpkt.Message, crTpkt);
        if (!decodedCrTpkt.IsValid)
            findings.Add(decodedCrTpkt.Message);

        var decodedCr = CotpFrameCodec.Decode(decodedCrTpkt.Payload);
        AddStep(steps, index++, "COTP-CR", decodedCr.IsValid && decodedCr.Kind == CotpTpduKind.ConnectionRequest, decodedCr.Message, decodedCrTpkt.Payload);
        if (!decodedCr.IsValid || decodedCr.Kind != CotpTpduKind.ConnectionRequest)
            findings.Add($"Client COTP connect request cannot be decoded safely: {decodedCr.Message}");

        var connectionConfirm = CotpFrameCodec.EncodeConnectionConfirm(decodedCr.SourceReference, 0x1001);
        var ccTpkt = TpktFrameCodec.Encode(connectionConfirm);
        var decodedCcTpkt = TpktFrameCodec.Decode(ccTpkt);
        var decodedCc = CotpFrameCodec.Decode(decodedCcTpkt.Payload);
        AddStep(steps, index++, "COTP-CC", decodedCcTpkt.IsValid && decodedCc.IsValid && decodedCc.Kind == CotpTpduKind.ConnectionConfirm, decodedCc.Message, ccTpkt);
        if (!decodedCcTpkt.IsValid || !decodedCc.IsValid || decodedCc.Kind != CotpTpduKind.ConnectionConfirm)
            findings.Add($"Server COTP connection confirm cannot be encoded/decoded safely: {decodedCc.Message}");

        foreach (var associationProfile in AcseMmsInitiateRequest.BuildAssociationProfiles())
        {
            var dataTpdu = CotpFrameCodec.EncodeData(associationProfile.Payload);
            var dataTpkt = TpktFrameCodec.Encode(dataTpdu);
            var decodedDataTpkt = TpktFrameCodec.Decode(dataTpkt);
            var decodedData = CotpFrameCodec.Decode(decodedDataTpkt.Payload);
            var dataPass = decodedDataTpkt.IsValid && decodedData.IsValid && decodedData.Kind == CotpTpduKind.Data && decodedData.EndOfTransmission;
            AddStep(steps, index++, $"COTP-DATA:{associationProfile.Name}", dataPass, decodedData.Message, dataTpkt);
            if (!dataPass)
                findings.Add($"{associationProfile.Name}: COTP data wrapper is not safe: {decodedData.Message}");

            var inspection = AcseAssociationPayloadInspector.Inspect(decodedData.UserData);
            AddStep(steps, index++, $"ACSE:{associationProfile.Name}", inspection.LooksLikeClientAssociateRequest, inspection.Message, decodedData.UserData);
            if (!inspection.LooksLikeClientAssociateRequest)
                findings.Add($"{associationProfile.Name}: association payload does not look like a complete IEC 61850 MMS associate request.");
        }

        return new MmsHandshakeCodecProfile
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsServerTransportReady = findings.Count == 0,
            Steps = steps,
            Findings = findings
        };
    }

    private static void AddStep(List<MmsHandshakeCodecStep> steps, int index, string area, bool pass, string message, ReadOnlySpan<byte> bytes)
    {
        steps.Add(new MmsHandshakeCodecStep
        {
            Index = index,
            Area = area,
            IsPass = pass,
            Message = message,
            HexPreview = HexDump.ToCompactString(bytes)
        });
    }
}
