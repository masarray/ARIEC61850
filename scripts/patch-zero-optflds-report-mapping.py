from pathlib import Path

source_path = Path("src/AR.Iec61850/Mms/MmsReportLiveSession.cs")
test_path = Path("tests/AR.Iec61850.Tests/Mms/MmsReportExactDecoderTests.cs")
source = source_path.read_text(encoding="utf-8")
test = test_path.read_text(encoding="utf-8")

replacements = [
    (
'''        var values = TryMapIec61850ReportValues(decoded.Items, members, out var mapped)
            ? mapped.Values
            : decoded.Items.Select(item => new MmsReportValue
            {
                Index = item.Index,
                Member = item.Index >= 0 && item.Index < members.Count ? members[item.Index] : null,
                Value = item.Value,
                FailureCode = item.FailureCode
            }).ToArray();

        return new MmsReportFrame
        {
            ReceivedAt = receivedAt,
            Header = mapped.Header,
            Values = values,
            RawAccessResultCount = decoded.Items.Count,
            InclusionBitstringItemIndex = mapped.InclusionBitstringItemIndex,
            IncludedDataSetIndexes = mapped.IncludedDataSetIndexes,
            DecoderMode = mapped.DecoderMode,
            ParseWarnings = mapped.ParseWarnings,
            Message = mapped.Message ?? decoded.Message,
            ResponseHexPreview = decoded.ResponseHexPreview
        };''',
'''        var isMapped = TryMapIec61850ReportValues(decoded.Items, members, out var mapped);
        var values = isMapped ? mapped.Values : Array.Empty<MmsReportValue>();
        var parseWarnings = isMapped
            ? mapped.ParseWarnings
            : new[]
            {
                "REPORT_FRAME_REJECTED: IEC 61850 report metadata/value mapping failed; raw AccessResults were quarantined and were not exposed as process values."
            };

        return new MmsReportFrame
        {
            ReceivedAt = receivedAt,
            Header = isMapped ? mapped.Header : DecodeHeader(decoded),
            Values = values,
            RawAccessResultCount = decoded.Items.Count,
            InclusionBitstringItemIndex = mapped.InclusionBitstringItemIndex,
            IncludedDataSetIndexes = mapped.IncludedDataSetIndexes,
            DecoderMode = isMapped ? mapped.DecoderMode : "rejected-unmapped",
            ParseWarnings = parseWarnings,
            Message = isMapped
                ? mapped.Message ?? decoded.Message
                : $"IEC 61850 InformationReport rejected for process-value projection: rawAccessResults={decoded.Items.Count}; raw report metadata was quarantined. {decoded.Message}",
            ResponseHexPreview = decoded.ResponseHexPreview
        };'''
    ),
    (
'''        if (TryMapOptFldsDrivenReportValues(items, members, out mapping))
            return true;

        for (var index = 5; index < items.Count; index++)''',
'''        if (TryMapOptFldsDrivenReportValues(items, members, out mapping))
            return true;

        // A canonical IEC 61850 report envelope starts with RptID + OptFlds.
        // If strict OptFlds-driven mapping rejects that frame, never reinterpret
        // another bit-string as inclusion: doing so can publish OptFlds/inclusion/
        // reason metadata as process values. Quarantine the frame instead.
        if (items.Count >= 2 && IsTextValue(items[0].Value) && items[1].Value?.Kind == MmsDataKind.BitString)
            return false;

        for (var index = 5; index < items.Count; index++)'''
    ),
    (
'''        var optionalFields = DecodeOptionalFields(items[cursor].Value!);
        if (optionalFields.Names.Count == 0 && optionalFields.SetBitIndexes.Count == 0)
            return false;

        cursor++;''',
'''        // OptFlds is mandatory report framing, but all optional bits may legally
        // be zero. Zero OptFlds therefore still means the next item is inclusion;
        // rejecting it here used to fall through to raw AccessResult projection.
        var optionalFields = DecodeOptionalFields(items[cursor].Value!);

        cursor++;'''
    ),
    (
'''        var totalBits = dataBytes.Length * 8 - unusedBits;
        if (totalBits < memberCount)
            return false;''',
'''        var totalBits = dataBytes.Length * 8 - unusedBits;
        // Inclusion is defined over the complete DataSet. Exact length prevents a
        // process/quality/reason bit-string from being mistaken for inclusion.
        if (totalBits != memberCount)
            return false;'''
    ),
    (
'''    private static string ReasonForInclusionName(int bitIndex)
        => bitIndex switch
        {
            0 => "data-change",
            1 => "quality-change",
            2 => "data-update",
            3 => "integrity",
            4 => "general-interrogation",
            5 => "application-trigger",
            _ => $"bit-{bitIndex}"
        };''',
'''    private static string ReasonForInclusionName(int bitIndex)
        => bitIndex switch
        {
            0 => "reserved",
            1 => "data-change",
            2 => "quality-change",
            3 => "data-update",
            4 => "integrity",
            5 => "general-interrogation",
            6 => "application-trigger",
            _ => $"bit-{bitIndex}"
        };'''
    ),
]

for old, new in replacements:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one source match, found {count}: {old[:90]!r}")
    source = source.replace(old, new, 1)

old_test = 'Assert.All(frame.Values, value => Assert.Equal(["application-trigger"], value.ReasonForInclusion));'
new_test = 'Assert.All(frame.Values, value => Assert.Equal(["general-interrogation"], value.ReasonForInclusion));'
if test.count(old_test) != 1:
    raise SystemExit("Expected old reason-for-inclusion assertion exactly once.")
test = test.replace(old_test, new_test, 1)

source_path.write_text(source, encoding="utf-8", newline="\n")
test_path.write_text(test, encoding="utf-8", newline="\n")
print("Applied zero-OptFlds report mapping field fix and safety quarantine.")
