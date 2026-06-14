using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Acse;

public enum AcseAssociationPayloadKind
{
    Unknown,
    SessionConnect,
    SessionAccept,
    SessionRejectOrRefuse,
    SessionAbort,
    PresentationData
}

public sealed class AcseAssociationPayloadInspection
{
    public AcseAssociationPayloadKind Kind { get; init; }
    public bool LooksLikeClientAssociateRequest { get; init; }
    public bool LooksLikeServerAssociateResponse { get; init; }
    public bool HasPresentationContext { get; init; }
    public bool HasAcseAarq { get; init; }
    public bool HasAcseAare { get; init; }
    public bool HasUserInformation { get; init; }
    public bool HasMmsInitiateRequestMarker { get; init; }
    public bool HasMmsInitiateResponseMarker { get; init; }
    public int Length { get; init; }
    public string HexPreview { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public static class AcseAssociationPayloadInspector
{
    public static AcseAssociationPayloadInspection Inspect(ReadOnlySpan<byte> payload)
    {
        var first = payload.Length > 0 ? payload[0] : (byte)0x00;
        var kind = first switch
        {
            0x0D => AcseAssociationPayloadKind.SessionConnect,
            0x0E => AcseAssociationPayloadKind.SessionAccept,
            0x0A or 0x0C => AcseAssociationPayloadKind.SessionRejectOrRefuse,
            0x19 => AcseAssociationPayloadKind.SessionAbort,
            0x01 or 0x61 or 0xA0 or 0xA1 or 0xA8 or 0xA9 => AcseAssociationPayloadKind.PresentationData,
            _ => AcseAssociationPayloadKind.Unknown
        };

        var hasPresentationContext = HexDump.Contains(payload, [0x31]) || HexDump.Contains(payload, [0x61]);
        var hasAarq = HexDump.Contains(payload, [0x60]);
        var hasAare = HexDump.Contains(payload, [0x61]);
        var hasUserInformation = HexDump.Contains(payload, [0xBE]);
        var hasInitiateRequest = HexDump.Contains(payload, [0xA8]) || HexDump.Contains(payload, [0x00, 0xFD, 0xE8]);
        var hasInitiateResponse = HexDump.Contains(payload, [0xA9]);
        var looksRequest = kind == AcseAssociationPayloadKind.SessionConnect && hasPresentationContext && hasAarq && hasUserInformation && hasInitiateRequest;
        var looksResponse = kind == AcseAssociationPayloadKind.SessionAccept && hasPresentationContext && hasAare;

        return new AcseAssociationPayloadInspection
        {
            Kind = kind,
            Length = payload.Length,
            LooksLikeClientAssociateRequest = looksRequest,
            LooksLikeServerAssociateResponse = looksResponse,
            HasPresentationContext = hasPresentationContext,
            HasAcseAarq = hasAarq,
            HasAcseAare = hasAare,
            HasUserInformation = hasUserInformation,
            HasMmsInitiateRequestMarker = hasInitiateRequest,
            HasMmsInitiateResponseMarker = hasInitiateResponse,
            HexPreview = HexDump.ToCompactString(payload),
            Message = BuildMessage(kind, payload.Length, looksRequest, looksResponse, hasAarq, hasAare, hasUserInformation, hasInitiateRequest, hasInitiateResponse)
        };
    }

    private static string BuildMessage(
        AcseAssociationPayloadKind kind,
        int length,
        bool looksRequest,
        bool looksResponse,
        bool hasAarq,
        bool hasAare,
        bool hasUserInformation,
        bool hasInitiateRequest,
        bool hasInitiateResponse)
    {
        if (looksRequest)
            return $"ACSE/MMS client associate request profile detected ({length} byte).";

        if (looksResponse)
            return $"ACSE/MMS server associate response profile detected ({length} byte).";

        return $"Association payload inspection: kind={kind}, length={length}, AARQ={hasAarq}, AARE={hasAare}, userInfo={hasUserInformation}, initiateReq={hasInitiateRequest}, initiateRsp={hasInitiateResponse}.";
    }
}
