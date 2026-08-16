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
    public IReadOnlyList<string> VariableReferences { get; init; } = Array.Empty<string>();
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

            var variableReferences = DecodeVariableReferences(info).ToArray();
            var wireAccessResults = DecodeInformationReportAccessResults(info).ToArray();
            var accessResults = NormalizeIec61850ReportAccessResultOrder(wireAccessResults);

            return new MmsInformationReport
            {
                IsSuccess = accessResults.Count > 0,
                VariableReferences = variableReferences,
                Items = accessResults,
                Message = accessResults.Count > 0
                    ? $"MMS InformationReport decoded {accessResults.Count} access result(s) and {variableReferences.Length} variable reference(s)."
                    : "MMS InformationReport was decoded, but no access results were found.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail($"MMS InformationReport decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    // IEC 61850 reports place optional DataRef entries immediately after the
    // inclusion bit-string and BEFORE the process values.  MmsReportFrameMapper
    // historically consumes its normalized Items as inclusion -> values -> DataRef
    // -> reason.  Normalize only the unambiguous DataRef report shape here so a
    // VisibleString DataRef can never shift process values and make a reason/quality
    // bit-string appear as the reported process value.
    private static IReadOnlyList<MmsInformationReportItem> NormalizeIec61850ReportAccessResultOrder(
        IReadOnlyList<MmsInformationReportItem> items)
    {
        if (items.Count < 4 || !IsText(items[0].Value) || items[1].Value?.Kind != MmsDataKind.BitString)
            return items;

        var optFlds = items[1].Value!;
        if (!IsBitSet(optFlds, 5)) // data-reference
            return items;

        var cursor = 2;
        if (IsBitSet(optFlds, 1)) cursor++; // SqNum
        if (IsBitSet(optFlds, 2)) cursor++; // TimeOfEntry
        if (IsBitSet(optFlds, 4)) cursor++; // DatSet
        if (IsBitSet(optFlds, 6)) cursor++; // BufOvfl
        if (IsBitSet(optFlds, 7)) cursor++; // EntryID
        if (IsBitSet(optFlds, 8)) cursor++; // ConfRev
        if (IsBitSet(optFlds, 9)) cursor += 2; // SubSqNum + MoreSegmentsFollow

        if (cursor >= items.Count || items[cursor].Value?.Kind != MmsDataKind.BitString)
            return items;

        var includedCount = CountSetBits(items[cursor].Value!);
        if (includedCount <= 0)
            return items;

        var dataRefStart = cursor + 1;
        var valueStart = dataRefStart + includedCount;
        var valueEnd = valueStart + includedCount;
        if (valueEnd > items.Count)
            return items;

        for (var i = 0; i < includedCount; i++)
        {
            if (!IsText(items[dataRefStart + i].Value))
                return items;
        }

        var normalized = new List<MmsInformationReportItem>(items.Count);
        for (var i = 0; i <= cursor; i++)
            normalized.Add(items[i]);
        for (var i = valueStart; i < valueEnd; i++)
            normalized.Add(items[i]);
        for (var i = dataRefStart; i < valueStart; i++)
            normalized.Add(items[i]);
        for (var i = valueEnd; i < items.Count; i++)
            normalized.Add(items[i]);

        return normalized
            .Select((item, index) => new MmsInformationReportItem
            {
                Index = index,
                Value = item.Value,
                FailureCode = item.FailureCode
            })
            .ToArray();
    }

    private static bool IsText(MmsDataValue? value)
        => value?.Kind is MmsDataKind.VisibleString or MmsDataKind.MmsString;

    private static bool IsBitSet(MmsDataValue bitString, int bitIndex)
    {
        if (bitString.Kind != MmsDataKind.BitString || bitString.RawValue.Count < 2 || bitIndex < 0)
            return false;

        var unused = bitString.RawValue[0];
        var bytes = bitString.RawValue.Skip(1).ToArray();
        var totalBits = bytes.Length * 8 - unused;
        if (bitIndex >= totalBits)
            return false;

        return ((bytes[bitIndex / 8] >> (7 - (bitIndex % 8))) & 0x01) != 0;
    }

    private static int CountSetBits(MmsDataValue bitString)
    {
        if (bitString.Kind != MmsDataKind.BitString || bitString.RawValue.Count < 2)
            return 0;

        var unused = bitString.RawValue[0];
        var bytes = bitString.RawValue.Skip(1).ToArray();
        var totalBits = bytes.Length * 8 - unused;
        var count = 0;
        for (var bit = 0; bit < totalBits; bit++)
        {
            if (((bytes[bit / 8] >> (7 - (bit % 8))) & 0x01) != 0)
                count++;
        }
        return count;
    }

    private static IEnumerable<string> DecodeVariableReferences(BerTlv informationReport)
    {
        if (!informationReport.Constructed)
            yield break;

        var children = BerReader.ReadChildren(informationReport.Value);
        if (children.Count == 0)
            yield break;

        // VariableAccessSpecification is the first service field. In IEC 61850
        // InformationReport traffic it is normally listOfVariable [0], but some
        // servers use variableListName [1]. Preserve all decodable names.
        var specification = children[0];
        foreach (var reference in DecodeObjectNames(specification))
            yield return reference;
    }

    private static IEnumerable<string> DecodeObjectNames(BerTlv node)
    {
        if (!node.Constructed)
            yield break;

        if (TryDecodeObjectName(node, out var direct))
            yield return direct;

        foreach (var child in BerReader.ReadChildren(node.Value))
        {
            foreach (var nested in DecodeObjectNames(child))
                yield return nested;
        }
    }

    private static bool TryDecodeObjectName(BerTlv node, out string reference)
    {
        reference = string.Empty;
        if (!node.Constructed)
            return false;

        var children = BerReader.ReadChildren(node.Value);

        // ObjectName.domain-specific [1] ::= SEQUENCE { domainID, itemID }
        if (node.Class == BerClass.ContextSpecific && node.TagNumber == 1 && children.Count >= 2)
        {
            var domain = TryReadIdentifier(children[0]);
            var item = TryReadIdentifier(children[1]);
            if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(item))
            {
                reference = $"{domain}/{item}";
                return true;
            }
        }

        // ObjectName.vmd-specific [0] or aa-specific [2].
        if (node.Class == BerClass.ContextSpecific && node.TagNumber is 0 or 2 && children.Count == 0)
        {
            var value = System.Text.Encoding.ASCII.GetString(node.Value.Span);
            if (!string.IsNullOrWhiteSpace(value))
            {
                reference = value;
                return true;
            }
        }

        return false;
    }

    private static string TryReadIdentifier(BerTlv node)
    {
        if (!node.Constructed)
            return System.Text.Encoding.ASCII.GetString(node.Value.Span);

        var child = BerReader.ReadChildren(node.Value).FirstOrDefault();
        return child.EncodedTag == 0 ? string.Empty : TryReadIdentifier(child);
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
        // use tag [0]. The access-result list is the trailing service field, so take
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
