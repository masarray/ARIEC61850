using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public enum MmsReadPayloadProfile
{
    PresentationDataValues,
    PresentationDataValuesWithSpecificationResult,
    SessionDataOnly,
    RawMmsPdu
}

public sealed class MmsReadResult
{
    public bool IsSuccess { get; init; }
    public MmsDataValue? Value { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public sealed class MmsReadAttempt
{
    public string ObjectProfile { get; init; } = string.Empty;
    public MmsReadPayloadProfile PayloadProfile { get; init; }
    public MmsObjectReference Reference { get; init; }
    public string RequestHexPreview { get; init; } = string.Empty;
    public MmsReadResult Result { get; init; } = new();

    public string Summary => $"{ObjectProfile}/{PayloadProfile}: {Reference} => {(Result.IsSuccess ? "OK" : Result.Message)}";
}

public static class MmsReadRequest
{
    public static byte[] BuildSingleVariableRead(int invokeId, MmsObjectReference reference, MmsReadPayloadProfile payloadProfile = MmsReadPayloadProfile.PresentationDataValues)
    {
        if (string.IsNullOrWhiteSpace(reference.Domain))
            throw new ArgumentException("MMS domain is empty. Use an object reference such as LD0/LLN0.Mod.stVal.", nameof(reference));

        if (string.IsNullOrWhiteSpace(reference.Item))
            throw new ArgumentException("MMS item is empty.", nameof(reference));

        var includeSpecificationWithResult = payloadProfile == MmsReadPayloadProfile.PresentationDataValuesWithSpecificationResult;
        var mmsPdu = BuildConfirmedReadPdu(invokeId, reference, includeSpecificationWithResult);
        return WrapForPayloadProfile(mmsPdu, payloadProfile);
    }

    public static byte[] BuildConfirmedReadPdu(int invokeId, MmsObjectReference reference, bool includeSpecificationWithResult = false)
    {
        var domainSpecificObjectName = BerWriter.EncodeTlv(
            0xA1,
            MmsPresentation.Concat(
                MmsPresentation.VisibleString(reference.Domain),
                MmsPresentation.VisibleString(reference.Item)));

        var variableSpecificationName = BerWriter.EncodeTlv(0xA0, domainSpecificObjectName);
        var variableSpecificationSequence = BerWriter.EncodeTlv(0x30, variableSpecificationName);
        var listOfVariable = BerWriter.EncodeTlv(0xA0, variableSpecificationSequence);
        var variableAccessSpecification = BerWriter.EncodeTlv(0xA1, listOfVariable);

        var readRequestBody = includeSpecificationWithResult
            ? MmsPresentation.Concat([0x80, 0x01, 0xFF], variableAccessSpecification)
            : variableAccessSpecification;

        var readRequest = BerWriter.EncodeTlv(0xA4, readRequestBody);
        return BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(MmsPresentation.Integer(invokeId), readRequest));
    }

    private static byte[] WrapForPayloadProfile(byte[] mmsPdu, MmsReadPayloadProfile payloadProfile)
    {
        return payloadProfile switch
        {
            MmsReadPayloadProfile.PresentationDataValues or MmsReadPayloadProfile.PresentationDataValuesWithSpecificationResult
                => MmsPresentation.WrapIsoPresentationPData(mmsPdu),
            MmsReadPayloadProfile.SessionDataOnly => MmsPresentation.Concat([0x01, 0x00], mmsPdu),
            MmsReadPayloadProfile.RawMmsPdu => mmsPdu,
            _ => MmsPresentation.WrapIsoPresentationPData(mmsPdu)
        };
    }
}

public static class MmsReadResponseDecoder
{
    public static MmsReadResult DecodeSingleVariable(ReadOnlyMemory<byte> presentationPayload, int? expectedInvokeId = null)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail("Empty MMS response payload.", hex);

            if (mms[0] == 0xA2)
                return Fail($"MMS Confirmed-Error PDU received: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return Fail($"MMS Reject/Abort PDU received: {HexDump.ToCompactString(mms)}", hex);

            if (!TryValidateConfirmedResponse(mms, expectedInvokeId, out var message))
                return Fail(message, hex);

            var values = new List<BerTlv>();
            CollectTlv(mms, values, depth: 0);

            var failure = values.LastOrDefault(v => v.EncodedTag == 0x81 && v.Value.Length is > 0 and <= 4);
            if (failure.EncodedTag == 0x81)
                return Fail($"MMS read returned AccessResult.failure code {BerReader.ReadUnsignedInteger(failure)}.", hex);

            foreach (var tlv in values.AsEnumerable().Reverse())
            {
                if (TryDecodeDataValue(tlv, out var value))
                {
                    return new MmsReadResult
                    {
                        IsSuccess = true,
                        Value = value,
                        Message = $"Native MMS Confirmed-Read decoded value: {MmsDataCodec.ToDisplayString(value)}.",
                        ResponseHexPreview = hex
                    };
                }
            }

            return Fail("MMS read response was received, but no decodable MMS Data value was found.", hex);
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail($"MMS read response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static bool TryValidateConfirmedResponse(ReadOnlyMemory<byte> mms, int? expectedInvokeId, out string message)
    {
        message = string.Empty;
        if (mms.IsEmpty)
        {
            message = "Empty MMS PDU.";
            return false;
        }

        if (mms.Span[0] != 0xA1)
        {
            message = $"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms.Span[0]:X2}.";
            return false;
        }

        if (!expectedInvokeId.HasValue)
            return true;

        var offset = 0;
        if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
        {
            message = "MMS Confirmed-Response PDU could not be decoded as BER.";
            return false;
        }

        var children = BerReader.ReadChildren(outer.Value);
        if (children.Count == 0 || children[0].EncodedTag != 0x02)
        {
            message = "MMS Confirmed-Response did not start with invokeID.";
            return false;
        }

        var actual = BerReader.ReadUnsignedInteger(children[0]);
        if (actual != (ulong)expectedInvokeId.Value)
        {
            message = $"MMS invokeID mismatch. Expected {expectedInvokeId.Value}, received {actual}.";
            return false;
        }

        return true;
    }

    private static void CollectTlv(ReadOnlyMemory<byte> buffer, ICollection<BerTlv> output, int depth)
    {
        if (depth > 24 || buffer.Length < 2)
            return;

        foreach (var tlv in BerReader.ReadChildren(buffer))
        {
            output.Add(tlv);
            if (tlv.Constructed)
                CollectTlv(tlv.Value, output, depth + 1);
        }
    }

    private static bool TryDecodeDataValue(BerTlv tlv, out MmsDataValue value)
    {
        value = default!;

        if (tlv.Class != BerClass.ContextSpecific)
            return false;

        if (tlv.EncodedTag is 0x81 or 0x82)
            return false;

        if (tlv.TagNumber is < 3 or > 17)
            return false;

        value = MmsDataCodec.Decode(tlv);
        return value.Kind != MmsDataKind.Unknown;
    }

    private static MmsReadResult Fail(string message, string hex)
    {
        return new MmsReadResult
        {
            IsSuccess = false,
            Message = message,
            ResponseHexPreview = hex
        };
    }
}
