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

            var accessResults = new List<MmsInformationReportItem>();
            foreach (var child in BerReader.ReadChildren(info.Value))
                CollectAccessResults(child, accessResults, depth: 0);

            return new MmsInformationReport
            {
                IsSuccess = accessResults.Count > 0,
                Items = accessResults,
                Message = accessResults.Count > 0
                    ? $"MMS InformationReport decoded {accessResults.Count} access result(s)."
                    : "MMS InformationReport was decoded, but no access results were found.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail($"MMS InformationReport decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static void CollectAccessResults(BerTlv tlv, List<MmsInformationReportItem> output, int depth)
    {
        if (depth > 32)
            return;

        if (tlv.Class == BerClass.ContextSpecific && tlv.TagNumber == 1 && !tlv.Constructed)
        {
            var code = BerReader.ReadUnsignedInteger(tlv);
            output.Add(new MmsInformationReportItem
            {
                Index = output.Count,
                FailureCode = code.HasValue ? (int)code.Value : null
            });
            return;
        }

        if (tlv.Class == BerClass.ContextSpecific && tlv.TagNumber == 0 && tlv.Constructed)
        {
            IReadOnlyList<BerTlv> children;
            try
            {
                children = BerReader.ReadChildren(tlv.Value);
            }
            catch (BerFormatException)
            {
                return;
            }

            if (children.Count == 1 && children[0].Class == BerClass.ContextSpecific && children[0].TagNumber is >= 1 and <= 17)
            {
                output.Add(new MmsInformationReportItem
                {
                    Index = output.Count,
                    Value = MmsDataCodec.Decode(children[0])
                });
                return;
            }

            foreach (var child in children)
                CollectAccessResults(child, output, depth + 1);

            return;
        }

        if (tlv.Class == BerClass.ContextSpecific && tlv.TagNumber is >= 1 and <= 17 && tlv.EncodedTag != 0xA0)
        {
            output.Add(new MmsInformationReportItem
            {
                Index = output.Count,
                Value = MmsDataCodec.Decode(tlv)
            });
            return;
        }

        if (!tlv.Constructed)
            return;

        foreach (var child in BerReader.ReadChildren(tlv.Value))
            CollectAccessResults(child, output, depth + 1);
    }

    private static MmsInformationReport Fail(string message, string hex)
        => new()
        {
            IsSuccess = false,
            Message = message,
            ResponseHexPreview = hex
        };
}
