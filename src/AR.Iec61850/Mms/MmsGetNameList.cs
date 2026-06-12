using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public enum MmsGetNameListObjectClass
{
    NamedVariable = 0,
    NamedVariableList = 2,
    Domain = 9
}

public sealed class MmsNameListResult
{
    public bool IsSuccess { get; init; }
    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();
    public bool MoreFollows { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public static class MmsGetNameListRequest
{
    public static byte[] Build(int invokeId, MmsGetNameListObjectClass objectClass, string? domainId = null, string? continueAfter = null)
    {
        var mmsPdu = BuildConfirmedGetNameListPdu(invokeId, objectClass, domainId, continueAfter);
        return MmsPresentation.WrapIsoPresentationPData(mmsPdu);
    }

    public static byte[] BuildConfirmedGetNameListPdu(int invokeId, MmsGetNameListObjectClass objectClass, string? domainId = null, string? continueAfter = null)
    {
        var objectClassNode = BerWriter.EncodeTlv(0x80, MmsPresentation.EncodeIntegerContent((int)objectClass));
        var objectClassField = BerWriter.EncodeTlv(0xA0, objectClassNode);

        var objectScopeChoice = string.IsNullOrWhiteSpace(domainId)
            ? BerWriter.EncodeTlv(0x80, ReadOnlySpan<byte>.Empty)
            : BerWriter.EncodeTlv(0x81, BerWriter.EncodeAscii(domainId.Trim()));

        var objectScopeField = BerWriter.EncodeTlv(0xA1, objectScopeChoice);
        var body = MmsPresentation.Concat(objectClassField, objectScopeField);

        if (!string.IsNullOrWhiteSpace(continueAfter))
            body = MmsPresentation.Concat(body, BerWriter.EncodeTlv(0x82, BerWriter.EncodeAscii(continueAfter.Trim())));

        var getNameList = BerWriter.EncodeTlv(0xA1, body);
        return BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(MmsPresentation.Integer(invokeId), getNameList));
    }
}

public static class MmsGetNameListResponseDecoder
{
    public static MmsNameListResult Decode(ReadOnlyMemory<byte> presentationPayload, int expectedInvokeId)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail("Empty MMS GetNameList response payload.", hex);

            if (mms[0] == 0xA2)
                return Fail($"MMS Confirmed-Error PDU during GetNameList: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return Fail($"MMS Reject/Abort PDU during GetNameList: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] != 0xA1)
                return Fail($"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms[0]:X2}.", hex);

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
                return Fail("MMS Confirmed-Response PDU could not be decoded as BER.", hex);

            var children = BerReader.ReadChildren(outer.Value);
            if (children.Count == 0)
                return Fail("MMS Confirmed-Response PDU is empty.", hex);

            var invoke = children[0];
            if (invoke.EncodedTag != 0x02)
                return Fail($"MMS GetNameList response did not start with invokeID. First inner tag=0x{invoke.EncodedTag:X2}.", hex);

            var actualInvoke = BerReader.ReadUnsignedInteger(invoke);
            if (actualInvoke != (ulong)expectedInvokeId)
                return Fail($"MMS GetNameList invokeID mismatch. Expected {expectedInvokeId}, received {actualInvoke}.", hex);

            var service = children.Skip(1).FirstOrDefault(x => x.EncodedTag == 0xA1);
            if (service.EncodedTag != 0xA1)
                return Fail("MMS GetNameList response has no service response node [1].", hex);

            var names = new List<string>();
            var moreFollows = false;

            foreach (var field in BerReader.ReadChildren(service.Value))
            {
                if (field.EncodedTag == 0xA0)
                {
                    foreach (var id in BerReader.ReadChildren(field.Value))
                    {
                        if (id.EncodedTag == 0x1A || id.EncodedTag == 0x16)
                            names.Add(BerReader.ReadAsciiString(id));
                    }
                }
                else if (field.EncodedTag == 0x81 && field.Value.Length > 0)
                {
                    moreFollows = field.Value.Span[0] != 0;
                }
            }

            var distinct = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new MmsNameListResult
            {
                IsSuccess = true,
                Names = distinct,
                MoreFollows = moreFollows,
                Message = $"MMS GetNameList decoded {distinct.Length} name(s), moreFollows={moreFollows}.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail($"MMS GetNameList response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static MmsNameListResult Fail(string message, string hex)
    {
        return new MmsNameListResult
        {
            IsSuccess = false,
            Names = Array.Empty<string>(),
            MoreFollows = false,
            Message = message,
            ResponseHexPreview = hex
        };
    }
}
