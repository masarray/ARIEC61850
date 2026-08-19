from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"expected source block not found in {path}")
    text = text.replace(old, new, 1)
    path.write_text(text, encoding="utf-8", newline="\n")


mms = ROOT / "src/AR.Iec61850/Mms/MmsVariableAccessAttributes.cs"
replace_once(
    mms,
    """public sealed class MmsTypeSpecificationNode\n{\n    public string Name { get; init; } = string.Empty;\n    public string MmsType { get; init; } = string.Empty;\n    public string SclBType { get; init; } = string.Empty;\n    public int? Size { get; init; }\n    public string Detail { get; init; } = string.Empty;\n""",
    """public enum MmsTypeSizeConstraintKind\n{\n    None,\n    Fixed,\n    Maximum\n}\n\npublic sealed class MmsTypeSpecificationNode\n{\n    public string Name { get; init; } = string.Empty;\n    public string MmsType { get; init; } = string.Empty;\n    public string SclBType { get; init; } = string.Empty;\n    /// <summary>\n    /// Fixed size or maximum variable size, depending on <see cref=\"SizeConstraintKind\"/>.\n    /// Negative MMS TypeSpecification size values are represented by their absolute maximum.\n    /// </summary>\n    public int? Size { get; init; }\n    public MmsTypeSizeConstraintKind SizeConstraintKind { get; init; }\n    public int? RawSizeConstraint { get; init; }\n    public bool IsVariableLength => SizeConstraintKind == MmsTypeSizeConstraintKind.Maximum;\n    public string Detail { get; init; } = string.Empty;\n""",
)

replace_once(
    mms,
    """                SclBType = type.SclBType,\n                Size = type.Size,\n                Detail = type.Detail,\n""",
    """                SclBType = type.SclBType,\n                Size = type.Size,\n                SizeConstraintKind = type.SizeConstraintKind,\n                RawSizeConstraint = type.RawSizeConstraint,\n                Detail = type.Detail,\n""",
)

replace_once(
    mms,
    """    private static MmsTypeSpecificationNode Basic(string componentName, string mmsType, string sclBType, BerTlv tlv)\n        => new()\n        {\n            Name = componentName,\n            MmsType = mmsType,\n            SclBType = sclBType,\n            Size = TryReadSize(tlv),\n            Detail = FormatDetail(tlv)\n        };\n\n    private static int? TryReadSize(BerTlv tlv)\n    {\n        if (tlv.Value.IsEmpty || tlv.Value.Length > 4)\n            return null;\n\n        var parsed = BerReader.ReadUnsignedInteger(tlv);\n        return parsed.HasValue && parsed.Value <= int.MaxValue ? (int)parsed.Value : null;\n    }\n\n    private static string FormatDetail(BerTlv tlv)\n    {\n        var size = TryReadSize(tlv);\n        return size.HasValue ? $\"size={size.Value}\" : string.Empty;\n    }\n""",
    """    private static MmsTypeSpecificationNode Basic(string componentName, string mmsType, string sclBType, BerTlv tlv)\n    {\n        var constraint = DecodeSizeConstraint(tlv);\n        return new MmsTypeSpecificationNode\n        {\n            Name = componentName,\n            MmsType = mmsType,\n            SclBType = sclBType,\n            Size = constraint.Size,\n            SizeConstraintKind = constraint.Kind,\n            RawSizeConstraint = constraint.Raw,\n            Detail = FormatDetail(constraint)\n        };\n    }\n\n    private static MmsTypeSizeConstraint DecodeSizeConstraint(BerTlv tlv)\n    {\n        if (tlv.Value.IsEmpty || tlv.Value.Length > 4)\n            return default;\n\n        // MMS TypeSpecification primitive size fields are signed Integer32 values.\n        // A negative value denotes variable length with the absolute value as the\n        // maximum. Reading these bytes as unsigned turns -2 (FE) into 254 and breaks\n        // IEC 61850 Check, which is semantically a two-bit BIT STRING.\n        var parsed = BerReader.ReadSignedInteger(tlv);\n        if (!parsed.HasValue || parsed.Value < int.MinValue || parsed.Value > int.MaxValue)\n            return default;\n\n        var raw = (int)parsed.Value;\n        if (raw == int.MinValue)\n            return new MmsTypeSizeConstraint(null, MmsTypeSizeConstraintKind.None, raw);\n\n        return raw < 0\n            ? new MmsTypeSizeConstraint(Math.Abs(raw), MmsTypeSizeConstraintKind.Maximum, raw)\n            : new MmsTypeSizeConstraint(raw, MmsTypeSizeConstraintKind.Fixed, raw);\n    }\n\n    private static string FormatDetail(MmsTypeSizeConstraint constraint)\n        => constraint.Kind switch\n        {\n            MmsTypeSizeConstraintKind.Fixed when constraint.Size.HasValue => $\"size={constraint.Size.Value}; fixed\",\n            MmsTypeSizeConstraintKind.Maximum when constraint.Size.HasValue => $\"max={constraint.Size.Value}; variable\",\n            _ => string.Empty\n        };\n\n    private readonly record struct MmsTypeSizeConstraint(\n        int? Size,\n        MmsTypeSizeConstraintKind Kind,\n        int? Raw);\n""",
)

binder = ROOT / "src/AR.Iec61850/Control/Iec61850ControlValueBinder.cs"
replace_once(
    binder,
    """            var actualBits = checked((encoded.Length - 1) * 8 - unusedBits);\n            if (actualBits != specification.Size.Value)\n                throw new InvalidOperationException($\"MMS bit-string size mismatch at {path}: expected {specification.Size.Value} bits, received {actualBits}.\");\n""",
    """            var actualBits = checked((encoded.Length - 1) * 8 - unusedBits);\n            if (specification.SizeConstraintKind == MmsTypeSizeConstraintKind.Maximum)\n            {\n                if (actualBits > specification.Size.Value)\n                    throw new InvalidOperationException($\"MMS bit-string size mismatch at {path}: maximum {specification.Size.Value} bits, received {actualBits}.\");\n            }\n            else if (actualBits != specification.Size.Value)\n            {\n                // Existing manually constructed specifications have SizeConstraintKind=None;\n                // preserve their historical exact-width behavior. Live decoded positive\n                // constraints are marked Fixed and follow the same rule.\n                throw new InvalidOperationException($\"MMS bit-string size mismatch at {path}: expected {specification.Size.Value} bits, received {actualBits}.\");\n            }\n""",
)

test = ROOT / "tests/AR.Iec61850.Tests/Control/G1ControlTypeSpecificationTests.cs"
test.write_text(r'''using AR.Iec61850.Asn1;
using AR.Iec61850.Control;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Control;

public sealed class G1ControlTypeSpecificationTests
{
    [Fact]
    public void VariableAccessAttributes_NegativeBitStringSize_DecodesAsMaximumTwoBits()
    {
        var reference = new MmsObjectReference("LD0", "CSWI1$CO$Pos$SBOw", "CO");
        var check = BuildComponent("Check", BerWriter.EncodeTlv(0x84, new byte[] { 0xFE }));
        var structure = BerWriter.EncodeTlv(0xA2, BerWriter.EncodeTlv(0xA1, check));
        var result = MmsVariableAccessAttributesResponseDecoder.Decode(
            BuildResponse(7, BerWriter.EncodeTlv(0xA2, structure)),
            7,
            reference);

        Assert.True(result.IsSuccess, result.Message);
        var decoded = Assert.Single(result.TypeSpecification!.Children);
        Assert.Equal("bit-string", decoded.MmsType);
        Assert.Equal(2, decoded.Size);
        Assert.Equal(-2, decoded.RawSizeConstraint);
        Assert.Equal(MmsTypeSizeConstraintKind.Maximum, decoded.SizeConstraintKind);
        Assert.True(decoded.IsVariableLength);
        Assert.Contains("max=2", decoded.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VariableAccessAttributes_PositiveBitStringSize_RemainsFixedTwoBits()
    {
        var reference = new MmsObjectReference("LD0", "CSWI1$CO$Pos$SBOw", "CO");
        var check = BuildComponent("Check", BerWriter.EncodeTlv(0x84, new byte[] { 0x02 }));
        var structure = BerWriter.EncodeTlv(0xA2, BerWriter.EncodeTlv(0xA1, check));
        var result = MmsVariableAccessAttributesResponseDecoder.Decode(
            BuildResponse(8, BerWriter.EncodeTlv(0xA2, structure)),
            8,
            reference);

        Assert.True(result.IsSuccess, result.Message);
        var decoded = Assert.Single(result.TypeSpecification!.Children);
        Assert.Equal(2, decoded.Size);
        Assert.Equal(2, decoded.RawSizeConstraint);
        Assert.Equal(MmsTypeSizeConstraintKind.Fixed, decoded.SizeConstraintKind);
        Assert.False(decoded.IsVariableLength);
    }

    [Theory]
    [InlineData(true, false, 0x80)]
    [InlineData(false, true, 0x40)]
    [InlineData(true, true, 0xC0)]
    public void SboWAndOperate_Check_AcceptLiveVariableMaximumTwoBitConstraint(
        bool synchro,
        bool interlock,
        byte expectedBits)
    {
        var ctlVal = Type("ctlVal", "bit-string", 2, MmsTypeSizeConstraintKind.Fixed);
        var request = new Iec61850ControlRequest
        {
            ControlValue = Iec61850ControlValue.Close(),
            SynchroCheck = synchro,
            InterlockCheck = interlock
        };
        var context = Iec61850ControlStructureBuilder.CreateContext(request, ctlVal, 3, DateTimeOffset.UtcNow);
        var command = CommandSpecificationWithVariableCheck();

        var sbow = Iec61850ControlStructureBuilder.BuildSelectWithValue(context, command, true);
        var oper = Iec61850ControlStructureBuilder.BuildOperate(context, command, true);

        Assert.Equal(new byte[] { 6, expectedBits }, sbow.Children[6].RawValue);
        Assert.Equal(new byte[] { 6, expectedBits }, oper.Children[6].RawValue);
    }

    [Fact]
    public void VariableMaximumTwoBitConstraint_RejectsThreeBitValue()
    {
        var specification = Type("Check", "bit-string", 2, MmsTypeSizeConstraintKind.Maximum);
        var error = Assert.Throws<InvalidOperationException>(() =>
            Iec61850ControlValueBinder.Validate(
                MmsDataValue.BitString(5, new byte[] { 0xA0 }),
                specification,
                "SBOw.Check"));

        Assert.Contains("maximum 2 bits", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FixedTwoBitConstraint_StillRejectsOneBitValue()
    {
        var specification = Type("Check", "bit-string", 2, MmsTypeSizeConstraintKind.Fixed);
        var error = Assert.Throws<InvalidOperationException>(() =>
            Iec61850ControlValueBinder.Validate(
                MmsDataValue.BitString(7, new byte[] { 0x80 }),
                specification,
                "SBOw.Check"));

        Assert.Contains("expected 2 bits", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MmsTypeSpecificationNode CommandSpecificationWithVariableCheck()
        => Structure(string.Empty,
            Type("ctlVal", "bit-string", 2, MmsTypeSizeConstraintKind.Fixed),
            Type("operTm", "utc-time"),
            Structure("origin", Type("orCat", "integer"), Type("orIdent", "octet-string", 64, MmsTypeSizeConstraintKind.Maximum)),
            Type("ctlNum", "unsigned"),
            Type("T", "utc-time"),
            Type("Test", "boolean"),
            Type("Check", "bit-string", 2, MmsTypeSizeConstraintKind.Maximum));

    private static MmsTypeSpecificationNode Type(
        string name,
        string type,
        int? size = null,
        MmsTypeSizeConstraintKind constraint = MmsTypeSizeConstraintKind.None)
        => new() { Name = name, MmsType = type, Size = size, SizeConstraintKind = constraint };

    private static MmsTypeSpecificationNode Structure(string name, params MmsTypeSpecificationNode[] children)
        => new() { Name = name, MmsType = "structure", Children = children };

    private static byte[] BuildResponse(int invokeId, params byte[][] serviceFields)
    {
        var service = BerWriter.EncodeTlv(0xA6, Concat(serviceFields));
        var confirmed = BerWriter.EncodeTlv(
            0xA1,
            Concat(BerWriter.EncodeTlv(0x02, new[] { (byte)invokeId }), service));
        return MmsPresentation.WrapIsoPresentationPData(confirmed);
    }

    private static byte[] BuildComponent(string name, byte[] typeSpecification)
    {
        var componentName = BerWriter.EncodeTlv(0x80, System.Text.Encoding.ASCII.GetBytes(name));
        var componentType = BerWriter.EncodeTlv(0xA1, typeSpecification);
        return BerWriter.EncodeTlv(0x30, Concat(componentName, componentType));
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }
        return result;
    }
}
''', encoding="utf-8", newline="\n")

print("G1 control correctness source patch applied")
