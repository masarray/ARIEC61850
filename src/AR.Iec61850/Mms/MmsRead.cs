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
    public int? FailureCode { get; init; }
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

            if (TryDecodeConfirmedReadAccessResult(mms, out var accessValue, out var accessMessage, out var failureCode))
            {
                return new MmsReadResult
                {
                    IsSuccess = true,
                    Value = accessValue,
                    Message = $"Native MMS Confirmed-Read decoded value: {MmsDataValueRenderer.ToCompactString(accessValue)}.",
                    ResponseHexPreview = hex
                };
            }

            if (!string.IsNullOrWhiteSpace(accessMessage))
                return Fail(accessMessage, hex, failureCode);

            // Legacy fallback for tolerant decoding of non-standard/short responses.
            var values = new List<BerTlv>();
            CollectTlv(mms, values, depth: 0);

            var failure = values.LastOrDefault(v => v.EncodedTag == 0x80 && v.Value.Length is > 0 and <= 4);
            if (failure.EncodedTag == 0x80)
            {
                var code = BerReader.ReadUnsignedInteger(failure);
                return Fail(FormatDataAccessFailure(code), hex, code.HasValue ? (int)code.Value : null);
            }

            foreach (var tlv in values.AsEnumerable().Reverse())
            {
                if (TryDecodeDataValue(tlv, out var value))
                {
                    return new MmsReadResult
                    {
                        IsSuccess = true,
                        Value = value,
                        Message = $"Native MMS Confirmed-Read decoded value: {MmsDataValueRenderer.ToCompactString(value)}.",
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


    private static bool TryDecodeConfirmedReadAccessResult(
        ReadOnlyMemory<byte> mms,
        out MmsDataValue value,
        out string message,
        out int? failureCode)
    {
        value = default!;
        message = string.Empty;
        failureCode = null;

        var offset = 0;
        if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
        {
            message = "MMS Confirmed-Response PDU could not be decoded as BER.";
            return false;
        }

        var children = BerReader.ReadChildren(outer.Value);
        var readService = children.Skip(1).FirstOrDefault(x => x.Class == BerClass.ContextSpecific && x.TagNumber == 4);
        if (readService.EncodedTag == 0)
        {
            message = string.Empty;
            return false;
        }

        if (!TryGetReadAccessResults(readService, out var accessResults))
        {
            message = "MMS read response has no AccessResult nodes.";
            return false;
        }

        var first = accessResults[0];
        if (IsAccessResultFailure(first))
        {
            var code = BerReader.ReadUnsignedInteger(first);
            failureCode = code.HasValue ? (int)code.Value : null;
            message = FormatDataAccessFailure(code);
            return false;
        }

        // In MMS ReadResponse, AccessResult.success is encoded directly as MMS Data.
        // It is not wrapped in an extra [0] success node.  The previous decoder only
        // accepted a synthetic [0] wrapper and therefore rejected primitive RCB
        // attributes such as RptEna(boolean), ConfRev(unsigned), BufTm(unsigned),
        // TrgOps(bit-string), and OptFlds(bit-string).  Accept direct MMS Data here.
        if (IsMmsDataTlv(first))
        {
            value = MmsDataCodec.Decode(first);
            return value.Kind != MmsDataKind.Unknown;
        }

        // Keep a tolerant path for non-standard servers or older decoder tests that
        // expose a success wrapper containing one Data child.
        if (first.Class == BerClass.ContextSpecific && first.TagNumber == 0 && first.Constructed)
        {
            var successChildren = BerReader.ReadChildren(first.Value);
            var data = successChildren.FirstOrDefault(IsMmsDataTlv);
            if (data.EncodedTag != 0)
            {
                value = MmsDataCodec.Decode(data);
                return value.Kind != MmsDataKind.Unknown;
            }
        }

        message = string.Empty;
        return false;
    }

    private static bool TryGetReadAccessResults(BerTlv readService, out IReadOnlyList<BerTlv> accessResults)
    {
        accessResults = Array.Empty<BerTlv>();
        if (!readService.Constructed)
            return false;

        var children = BerReader.ReadChildren(readService.Value);

        // Read-Response ::= SEQUENCE {
        //   variableAccessSpecification [0] OPTIONAL,
        //   listOfAccessResult [1] IMPLICIT SEQUENCE OF AccessResult
        // }
        //
        // The [1] wrapper is not an MMS Data.array.  Older tolerant decoding treated
        // it as a data value or failure and produced large bogus failure codes such
        // as 0x80010A.  Decode the wrapper first, then interpret each AccessResult.
        var list = children.LastOrDefault(x =>
            x.Class == BerClass.ContextSpecific &&
            x.TagNumber == 1 &&
            x.Constructed);

        if (list.EncodedTag != 0)
        {
            accessResults = BerReader.ReadChildren(list.Value);
            return accessResults.Count > 0;
        }

        // Legacy fallback for older tests/non-standard responses where the read
        // service directly contains access result TLVs without the [1] list wrapper.
        accessResults = children
            .Where(x => IsAccessResultFailure(x) || IsMmsDataTlv(x) || IsLegacySuccessWrapper(x))
            .ToArray();
        return accessResults.Count > 0;
    }

    private static bool IsAccessResultFailure(BerTlv tlv)
        => tlv.Class == BerClass.ContextSpecific && tlv.TagNumber == 0 && !tlv.Constructed;

    private static bool IsLegacySuccessWrapper(BerTlv tlv)
        => tlv.Class == BerClass.ContextSpecific && tlv.TagNumber == 0 && tlv.Constructed;

    private static bool IsMmsDataTlv(BerTlv tlv)
    {
        if (tlv.Class != BerClass.ContextSpecific)
            return false;

        return tlv.TagNumber switch
        {
            1 or 2 => tlv.Constructed,
            >= 3 and <= 17 => !tlv.Constructed,
            _ => false
        };
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

    private static string FormatDataAccessFailure(ulong? code)
        => code.HasValue
            ? $"MMS read returned AccessResult.failure code {code.Value} ({DataAccessErrorName(code.Value)})."
            : "MMS read returned AccessResult.failure code <undecodable>.";

    private static string DataAccessErrorName(ulong code)
        => code switch
        {
            0 => "object-invalidated",
            1 => "hardware-fault",
            2 => "temporarily-unavailable",
            3 => "object-access-denied",
            4 => "object-undefined",
            5 => "invalid-address",
            6 => "type-unsupported",
            7 => "type-inconsistent",
            8 => "object-attribute-inconsistent",
            9 => "object-access-unsupported",
            10 => "object-non-existent",
            _ => "unknown"
        };

    private static MmsReadResult Fail(string message, string hex, int? failureCode = null)
    {
        return new MmsReadResult
        {
            IsSuccess = false,
            FailureCode = failureCode,
            Message = message,
            ResponseHexPreview = hex
        };
    }
}
