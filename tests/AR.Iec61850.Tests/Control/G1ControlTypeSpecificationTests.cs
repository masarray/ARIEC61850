using AR.Iec61850.Asn1;
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
