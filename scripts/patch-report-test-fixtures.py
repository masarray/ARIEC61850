from pathlib import Path

path = Path("tests/AR.Iec61850.Tests/Mms/MmsWriteAndDynamicDataSetTests.cs")
text = path.read_text(encoding="utf-8")

replacements = [
    (
        'new MmsInformationReportItem { Index = 1, Value = MmsDataValue.BitString(6, [0x7C, 0x80]) },',
        'new MmsInformationReportItem { Index = 1, Value = MmsDataValue.BitString(6, [0x7C, 0x00]) },'
    ),
    (
        '        Assert.Contains("conf-revision", frame.Header.OptionalFields.Names);\n',
        ''
    ),
    (
        'new MmsInformationReportItem { Index = 1, Value = MmsDataValue.BitString(6, [0x10, 0x00]) },',
        'new MmsInformationReportItem { Index = 1, Value = MmsDataValue.BitString(6, [0x78, 0x00]) },'
    ),
    (
        'new MmsInformationReportItem { Index = 8, Value = MmsDataValue.BitString(2, [0b1000_0000]) },\n                new MmsInformationReportItem { Index = 9, Value = MmsDataValue.BitString(2, [0b0100_0000]) }',
        'new MmsInformationReportItem { Index = 8, Value = MmsDataValue.BitString(2, [0b0100_0000]) },\n                new MmsInformationReportItem { Index = 9, Value = MmsDataValue.BitString(2, [0b0010_0000]) }'
    ),
]

for old, new in replacements:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected one match, found {count}: {old[:100]!r}")
    text = text.replace(old, new, 1)

path.write_text(text, encoding="utf-8", newline="\n")
print("Updated synthetic report fixtures to protocol-consistent OptFlds/reason bits.")
