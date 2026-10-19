using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

/// <summary>
/// Synthetic BER failure and exact zero-value fixtures; no device/customer capture.
/// </summary>
public sealed class P0TypedMmsNonFabricationRegressionTests
{
    [Theory]
    [InlineData("8300", 3)] // boolean: empty payload cannot mean false
    [InlineData("8500", 5)] // signed integer: empty payload cannot mean zero
    [InlineData("8600", 6)] // unsigned integer: empty payload cannot mean zero
    [InlineData("8509000000000000000000", 5)] // signed integer wider than Int64
    [InlineData("8609000000000000000000", 6)] // unsigned integer wider than UInt64
    public void InvalidTypedPayload_MustNotPublishFabricatedScalar(string hex, int expectedTag)
    {
        var value = Assert.Single(MmsDataCodec.DecodeAllData(Convert.FromHexString(hex)));
        Assert.Equal(MmsDataKind.Unknown, value.Kind);
        Assert.Equal(expectedTag, value.UnknownTagNumber);
        Assert.Null(value.Value);
    }

    [Theory]
    [InlineData("830100", MmsDataKind.Boolean)]
    [InlineData("850100", MmsDataKind.Integer)]
    [InlineData("860100", MmsDataKind.Unsigned)]
    public void RealWireFalseOrZero_RemainsObserved(string hex, MmsDataKind expected)
    {
        var value = Assert.Single(MmsDataCodec.DecodeAllData(Convert.FromHexString(hex)));
        Assert.Equal(expected, value.Kind);
        Assert.NotNull(value.Value);
        Assert.Equal("0", Convert.ToInt64(value.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
