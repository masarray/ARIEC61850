using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class P0CTypedReadAccessResultTests
{
    [Fact]
    public void Ordered_Confirmed_Read_Maintains_Int32_Zero_And_Individual_AccessFailures()
    {
        var references = Enumerable.Range(0, 4)
            .Select(i => new MmsObjectReference("LD0", $"LLN0$SG$Value{i}$setVal", "SG"))
            .ToArray();
        var response = Response(42,
            MmsDataCodec.Encode(MmsDataValue.Integer(0)),
            BerWriter.EncodeTlv(0x80, new byte[] { 3 }),
            BerWriter.EncodeTlv(0x80, new byte[] { 6 }),
            BerWriter.EncodeTlv(0x80, new byte[] { 7 }));
        var batch = MmsReadBatchCodec.DecodeResponse(response, references, expectedInvokeId: 42);
        Assert.Equal(4, batch.Results.Count);
        Assert.Equal(0L, batch.Results[0].Value!.Value);
        Assert.True(batch.Results[0].IsSuccess);
        Assert.Equal(MmsReadEvidenceFailureKind.None, batch.Results[0].FailureKind);
        Assert.Equal(MmsReadEvidenceFailureKind.AccessDenied, batch.Results[1].FailureKind);
        Assert.Equal(3, batch.Results[1].FailureCode);
        Assert.Equal(MmsReadEvidenceFailureKind.NotSupported, batch.Results[2].FailureKind);
        Assert.Equal(MmsReadEvidenceFailureKind.TypeMismatch, batch.Results[3].FailureKind);
        Assert.All(batch.Results.Skip(1), x => Assert.Null(x.Value));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Missing_Or_Extra_AccessResults_Fail_All_Without_Ordinal_Ambiguity(int resultCount)
    {
        var references = new[]
        {
            new MmsObjectReference("LD0", "LLN0$SG$PTOCEna$setVal", "SG"),
            new MmsObjectReference("LD0", "LLN0$SG$InMagTyp$setVal", "SG")
        };
        var response = Response(5, Enumerable.Range(0, resultCount)
            .Select(_ => MmsDataCodec.Encode(MmsDataValue.Boolean(false))).ToArray());
        var result = MmsReadBatchCodec.DecodeResponse(response, references, expectedInvokeId: 5);
        Assert.False(result.HasAnySuccess);
        Assert.All(result.Results, x =>
        {
            Assert.False(x.IsSuccess);
            Assert.Null(x.Value);
            Assert.Equal(MmsReadEvidenceFailureKind.InvalidResponse, x.FailureKind);
        });
        Assert.Contains("count mismatch", result.Message);
    }

    [Fact]
    public void Stale_Invoke_Cannot_Publish_Even_When_Data_Is_Decodable()
    {
        var refs = new[] {new MmsObjectReference("LD0", "LLN0$SG$PTOCEna$setVal", "SG")};
        var result = MmsReadBatchCodec.DecodeResponse(
            Response(21, MmsDataCodec.Encode(MmsDataValue.Boolean(false))), refs,
            expectedInvokeId: 22);
        Assert.False(result.HasAnySuccess);
        Assert.Equal(MmsReadEvidenceFailureKind.InvalidResponse,
            Assert.Single(result.Results).FailureKind);
    }

    [Fact]
    public void Invalid_Ber_Integer_Leaves_Unknown_Not_Observed_Zero()
    {
        var refs = new[] {new MmsObjectReference("LD0", "LLN0$SG$InMagTyp$setVal", "SG")};
        var result = MmsReadBatchCodec.DecodeResponse(
            Response(42, Convert.FromHexString("8500")), refs, expectedInvokeId: 42);
        var item = Assert.Single(result.Results);
        Assert.False(item.IsSuccess);
        Assert.Null(item.Value);
        Assert.Equal(MmsReadEvidenceFailureKind.InvalidResponse, item.FailureKind);
    }

    private static byte[] Response(int invokeId, params byte[][] accessResults)
    {
        var results = accessResults.SelectMany(item => item).ToArray();
        var service = BerWriter.EncodeTlv(0xA4,
            BerWriter.EncodeTlv(0xA1, results));
        var invoke = BerWriter.EncodeTlv(0x02, BerWriter.EncodeUnsignedInteger((ulong)invokeId));
        return BerWriter.EncodeTlv(0xA1, invoke.Concat(service).ToArray());
    }
}
