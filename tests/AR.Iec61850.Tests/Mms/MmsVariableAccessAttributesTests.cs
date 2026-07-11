using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsVariableAccessAttributesTests
{
    [Fact]
    public void Build_request_uses_get_variable_access_attributes_service_tag()
    {
        var request = MmsVariableAccessAttributesRequest.Build(
            7,
            new MmsObjectReference("LD0", "LLN0$ST$Mod$stVal", "ST"));

        var mms = MmsPresentation.StripPresentationPrefix(request);
        var offset = 0;
        Assert.True(BerReader.TryReadTlv(mms, ref offset, out var confirmed));
        var service = BerReader.ReadChildren(confirmed.Value)[1];
        var namedVariable = BerReader.ReadChildren(service.Value).Single();
        var objectName = BerReader.ReadChildren(namedVariable.Value).Single();

        Assert.Equal(0xA6, service.EncodedTag);
        Assert.Equal(0xA0, namedVariable.EncodedTag);
        Assert.Equal(0xA1, objectName.EncodedTag);
        var identifiers = BerReader.ReadChildren(objectName.Value).Select(BerReader.ReadAsciiString).ToArray();
        Assert.Equal(["LD0", "LLN0$ST$Mod$stVal"], identifiers);
    }

    [Fact]
    public void Decode_boolean_type_specification_response()
    {
        var reference = new MmsObjectReference("LD0", "LLN0$ST$Mod$stVal", "ST");
        var response = BuildResponse(
            3,
            BerWriter.EncodeTlv(0x80, new byte[] { 0x00 }),
            BerWriter.EncodeTlv(0xA2, BerWriter.EncodeTlv(0x83, ReadOnlySpan<byte>.Empty)));

        var result = MmsVariableAccessAttributesResponseDecoder.Decode(response, 3, reference);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("boolean", result.MmsType);
        Assert.Equal("BOOLEAN", result.SclBType);
        Assert.False(result.IsMmsDeletable);
    }

    [Fact]
    public void Decode_structure_type_specification_response()
    {
        var reference = new MmsObjectReference("LD0", "PTOC1$ST$Op", "ST");
        var stVal = BuildComponent("stVal", BerWriter.EncodeTlv(0x83, ReadOnlySpan<byte>.Empty));
        var q = BuildComponent("q", BerWriter.EncodeTlv(0x84, new byte[] { 13 }));
        var t = BuildComponent("t", BerWriter.EncodeTlv(0x91, ReadOnlySpan<byte>.Empty));
        var components = BerWriter.EncodeTlv(0xA1, Concat(stVal, q, t));
        var structure = BerWriter.EncodeTlv(0xA2, components);
        var response = BuildResponse(4, BerWriter.EncodeTlv(0xA2, structure));

        var result = MmsVariableAccessAttributesResponseDecoder.Decode(response, 4, reference);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("structure", result.MmsType);
        Assert.Equal("Struct", result.SclBType);
        Assert.Equal("structure(stVal:boolean,q:bit-string,t:utc-time)", result.TypeSignature);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = parts.Sum(x => x.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }

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
}
