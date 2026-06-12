using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed class MmsWriteAccessResult
{
    public bool IsSuccess { get; init; }
    public int? FailureCode { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsWriteResult
{
    public bool IsSuccess { get; init; }
    public IReadOnlyList<MmsWriteAccessResult> AccessResults { get; init; } = Array.Empty<MmsWriteAccessResult>();
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public static class MmsWriteRequest
{
    public static byte[] BuildSingleVariableWrite(int invokeId, MmsObjectReference reference, MmsDataValue value)
    {
        if (string.IsNullOrWhiteSpace(reference.Domain))
            throw new ArgumentException("MMS domain is empty. Use an object reference such as LD0/LLN0.Mod.stVal.", nameof(reference));

        if (string.IsNullOrWhiteSpace(reference.Item))
            throw new ArgumentException("MMS item is empty.", nameof(reference));

        ArgumentNullException.ThrowIfNull(value);

        var variableAccessSpecification = BuildListOfVariable(reference);
        var listOfData = BerWriter.EncodeTlv(0xA0, MmsDataCodec.Encode(value));
        var writeRequest = BerWriter.EncodeTlv(0xA5, MmsPresentation.Concat(variableAccessSpecification, listOfData));
        var confirmedRequest = BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(MmsPresentation.Integer(invokeId), writeRequest));
        return MmsPresentation.WrapIsoPresentationPData(confirmedRequest);
    }

    internal static byte[] BuildListOfVariable(MmsObjectReference reference)
    {
        var objectName = MmsDataSetDirectoryRequest.EncodeDomainSpecificObjectName(reference.Domain, reference.Item);
        var variableSpecificationName = BerWriter.EncodeTlv(0xA0, objectName);
        var variableDef = BerWriter.EncodeTlv(0x30, variableSpecificationName);
        return BerWriter.EncodeTlv(0xA0, variableDef);
    }
}

public static class MmsWriteResponseDecoder
{
    public static MmsWriteResult Decode(ReadOnlyMemory<byte> presentationPayload, int expectedInvokeId)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail("Empty MMS write response payload.", hex);

            if (mms[0] == 0xA2)
                return Fail($"MMS Confirmed-Error PDU during write: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return Fail($"MMS Reject/Abort PDU during write: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] != 0xA1)
                return Fail($"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms[0]:X2}.", hex);

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
                return Fail("MMS write response PDU could not be decoded as BER.", hex);

            var children = BerReader.ReadChildren(outer.Value);
            if (children.Count == 0 || children[0].EncodedTag != 0x02)
                return Fail("MMS write response did not start with invokeID.", hex);

            var actualInvoke = BerReader.ReadUnsignedInteger(children[0]);
            if (actualInvoke != (ulong)expectedInvokeId)
                return Fail($"MMS write invokeID mismatch. Expected {expectedInvokeId}, received {actualInvoke}.", hex);

            var service = children.Skip(1).FirstOrDefault(x => x.EncodedTag == 0xA5 || x.EncodedTag == 0x85 || (x.Class == BerClass.ContextSpecific && x.TagNumber == 5));
            if (service.EncodedTag == 0)
                return Fail("MMS response has no Write service response node [5].", hex);

            var accessResults = DecodeAccessResults(service).ToArray();
            var success = accessResults.Length > 0 && accessResults.All(x => x.IsSuccess);
            return new MmsWriteResult
            {
                IsSuccess = success,
                AccessResults = accessResults,
                Message = success
                    ? $"MMS Confirmed-Write succeeded for {accessResults.Length} item(s)."
                    : $"MMS Confirmed-Write returned {accessResults.Count(x => !x.IsSuccess)} failure(s).",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail($"MMS write response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static IEnumerable<MmsWriteAccessResult> DecodeAccessResults(BerTlv service)
    {
        if (!service.Constructed && service.Value.Length == 0)
        {
            yield return new MmsWriteAccessResult { IsSuccess = true, Message = "success" };
            yield break;
        }

        foreach (var child in BerReader.ReadChildren(service.Value))
        {
            if (child.EncodedTag == 0x81 || (child.Class == BerClass.ContextSpecific && child.TagNumber == 1))
            {
                yield return new MmsWriteAccessResult { IsSuccess = true, Message = "success" };
                continue;
            }

            if (child.EncodedTag == 0x80 || (child.Class == BerClass.ContextSpecific && child.TagNumber == 0))
            {
                var code = BerReader.ReadUnsignedInteger(child);
                yield return new MmsWriteAccessResult
                {
                    IsSuccess = false,
                    FailureCode = code.HasValue ? (int)code.Value : null,
                    Message = code.HasValue ? $"failure code {code.Value}" : "failure"
                };
            }
        }
    }

    private static MmsWriteResult Fail(string message, string hex)
        => new()
        {
            IsSuccess = false,
            Message = message,
            ResponseHexPreview = hex
        };
}
