using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed class MmsInformationReportItem
{
    public int Index { get; init; }
    public MmsDataValue? Value { get; init; }
    public int? FailureCode { get; init; }
    public string DisplayValue => Value == null ? $"failure={FailureCode}" : MmsDataValueRenderer.ToCompactString(Value);
}

public sealed class MmsInformationReport
{
    public bool IsSuccess { get; init; }
    public IReadOnlyList<MmsInformationReportItem> Items { get; init; } = Array.Empty<MmsInformationReportItem>();
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public static class MmsInformationReportDecoder
{
    public static bool IsInformationReport(ReadOnlyMemory<byte> presentationPayload)
    {
        var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
        return mms.Length > 0 && mms[0] == 0xA3;
    }

    public static MmsInformationReport Decode(ReadOnlyMemory<byte> presentationPayload)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail("Empty MMS InformationReport payload.", hex);

            if (mms[0] != 0xA3)
                return Fail($"Expected MMS Unconfirmed-PDU [3] (0xA3), received 0x{mms[0]:X2}.", hex);

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
                return Fail("MMS InformationReport PDU could not be decoded as BER.", hex);

            var info = BerReader.ReadChildren(outer.Value)
                .FirstOrDefault(x => x.EncodedTag == 0xA0 || (x.Class == BerClass.ContextSpecific && x.TagNumber == 0));
            if (info.EncodedTag == 0)
                return Fail("MMS Unconfirmed-PDU has no informationReport service node [0].", hex);

            var accessResults = DecodeInformationReportAccessResults(info).ToArray();

            return new MmsInformationReport
            {
                IsSuccess = accessResults.Length > 0,
                Items = accessResults,
                Message = accessResults.Length > 0
                    ? $"MMS InformationReport decoded {accessResults.Length} access result(s)."
                    : "MMS InformationReport was decoded, but no access results were found.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail($"MMS InformationReport decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static IEnumerable<MmsInformationReportItem> DecodeInformationReportAccessResults(BerTlv informationReport)
    {
        if (!informationReport.Constructed)
            yield break;

        var children = BerReader.ReadChildren(informationReport.Value);

        // InformationReport ::= SEQUENCE {
        //   variableAccessSpecification VariableAccessSpecification,
        //   listOfAccessResult [0] IMPLICIT SEQUENCE OF AccessResult
        // }
        //
        // Both variableAccessSpecification.listOfVariable and listOfAccessResult can
        // use tag [0].  The access-result list is the trailing service field, so take
        // the last constructed [0] child instead of recursively decoding object-name
        // metadata as reported values.
        var list = children.LastOrDefault(x =>
            x.Class == BerClass.ContextSpecific &&
            x.TagNumber == 0 &&
            x.Constructed);

        if (list.EncodedTag == 0)
            yield break;

        var index = 0;
        foreach (var accessResult in BerReader.ReadChildren(list.Value))
        {
            if (IsAccessResultFailure(accessResult))
            {
                var code = BerReader.ReadUnsignedInteger(accessResult);
                yield return new MmsInformationReportItem
                {
                    Index = index++,
                    FailureCode = code.HasValue ? (int)code.Value : null
                };
                continue;
            }

            if (IsMmsDataTlv(accessResult))
            {
                yield return new MmsInformationReportItem
                {
                    Index = index++,
                    Value = MmsDataCodec.Decode(accessResult)
                };
                continue;
            }
        }
    }

    private static bool IsAccessResultFailure(BerTlv tlv)
        => tlv.Class == BerClass.ContextSpecific && tlv.TagNumber == 0 && !tlv.Constructed;

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

    private static MmsInformationReport Fail(string message, string hex)
        => new()
        {
            IsSuccess = false,
            Message = message,
            ResponseHexPreview = hex
        };
}
