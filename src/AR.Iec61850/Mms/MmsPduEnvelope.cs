using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public enum MmsPduKind
{
    Unknown,
    ConfirmedRequest,
    ConfirmedResponse,
    ConfirmedError,
    Unconfirmed,
    Reject,
    CancelRequest,
    CancelResponse,
    CancelError,
    InitiateRequest,
    InitiateResponse,
    InitiateError,
    ConcludeRequest,
    ConcludeResponse,
    ConcludeError
}

public sealed class MmsPduEnvelope
{
    public MmsPduKind Kind { get; init; }
    public int? InvokeId { get; init; }
    public bool IsInformationReport { get; init; }
    public byte TopLevelTag { get; init; }
    public byte[] PresentationPayload { get; init; } = Array.Empty<byte>();
    public byte[] MmsPayload { get; init; } = Array.Empty<byte>();
    public string ResponseHexPreview { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public bool IsConfirmedServiceResult =>
        Kind is MmsPduKind.ConfirmedResponse or MmsPduKind.ConfirmedError or MmsPduKind.Reject;

    public bool MatchesInvoke(int invokeId)
        => InvokeId.HasValue && InvokeId.Value == invokeId;

    public static MmsPduEnvelope Decode(ReadOnlyMemory<byte> presentationPayload)
    {
        var presentation = presentationPayload.ToArray();
        var hex = HexDump.ToCompactString(presentation);
        var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);

        if (mms.Length == 0)
        {
            return new MmsPduEnvelope
            {
                Kind = MmsPduKind.Unknown,
                PresentationPayload = presentation,
                MmsPayload = Array.Empty<byte>(),
                ResponseHexPreview = hex,
                Message = "Empty MMS payload."
            };
        }

        var topLevelTag = mms[0];
        var kind = ToKind(topLevelTag);
        int? invokeId = null;
        var isInformationReport = false;
        var message = string.Empty;

        try
        {
            var offset = 0;
            if (BerReader.TryReadTlv(mms, ref offset, out var outer))
            {
                var children = outer.Constructed
                    ? BerReader.ReadChildren(outer.Value)
                    : Array.Empty<BerTlv>();

                invokeId = TryReadInvokeId(kind, children);
                isInformationReport = kind == MmsPduKind.Unconfirmed && HasInformationReportService(children);
            }
            else
            {
                message = "MMS payload is not valid BER.";
            }
        }
        catch (BerFormatException ex)
        {
            message = $"MMS PDU envelope decode failed: {ex.Message}";
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            var invokeText = invokeId.HasValue ? $" invokeID={invokeId.Value}" : string.Empty;
            var infoText = isInformationReport ? " informationReport" : string.Empty;
            message = $"MMS {kind}{invokeText}{infoText}.";
        }

        return new MmsPduEnvelope
        {
            Kind = kind,
            InvokeId = invokeId,
            IsInformationReport = isInformationReport,
            TopLevelTag = topLevelTag,
            PresentationPayload = presentation,
            MmsPayload = mms,
            ResponseHexPreview = hex,
            Message = message
        };
    }

    private static MmsPduKind ToKind(byte topLevelTag)
        => topLevelTag switch
        {
            0xA0 => MmsPduKind.ConfirmedRequest,
            0xA1 => MmsPduKind.ConfirmedResponse,
            0xA2 => MmsPduKind.ConfirmedError,
            0xA3 => MmsPduKind.Unconfirmed,
            0xA4 => MmsPduKind.Reject,
            0xA5 => MmsPduKind.CancelRequest,
            0xA6 => MmsPduKind.CancelResponse,
            0xA7 => MmsPduKind.CancelError,
            0xA8 => MmsPduKind.InitiateRequest,
            0xA9 => MmsPduKind.InitiateResponse,
            0xAA => MmsPduKind.InitiateError,
            0x8B => MmsPduKind.ConcludeRequest,
            0x8C => MmsPduKind.ConcludeResponse,
            0xAD => MmsPduKind.ConcludeError,
            _ => MmsPduKind.Unknown
        };

    private static int? TryReadInvokeId(MmsPduKind kind, IReadOnlyList<BerTlv> children)
    {
        if (children.Count == 0)
            return null;

        if (kind is not (MmsPduKind.ConfirmedRequest or
                         MmsPduKind.ConfirmedResponse or
                         MmsPduKind.ConfirmedError or
                         MmsPduKind.Reject or
                         MmsPduKind.CancelRequest or
                         MmsPduKind.CancelResponse or
                         MmsPduKind.CancelError))
        {
            return null;
        }

        foreach (var child in children.Take(2))
        {
            var value = child.EncodedTag == 0x02 ||
                        (child.Class == BerClass.ContextSpecific && child.TagNumber == 0 && !child.Constructed)
                ? BerReader.ReadUnsignedInteger(child)
                : null;

            if (value.HasValue && value.Value <= int.MaxValue)
                return (int)value.Value;
        }

        return null;
    }

    private static bool HasInformationReportService(IReadOnlyList<BerTlv> children)
        => children.Any(x => x.Class == BerClass.ContextSpecific && x.TagNumber == 0 && x.Constructed);
}
