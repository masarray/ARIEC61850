using AR.Iec61850.Ethernet;

namespace AR.Iec61850.Tests.Ethernet;

public sealed class MacAddressValueSemanticsTests
{
    [Fact]
    public void EquivalentParsedAddressesCompareByOctetValue()
    {
        var colon = MacAddress.Parse("01:0C:CD:04:00:02");
        var hyphen = MacAddress.Parse("01-0c-cd-04-00-02");

        Assert.Equal(colon, hyphen);
        Assert.True(colon == hyphen);
        Assert.Equal(colon.GetHashCode(), hyphen.GetHashCode());
    }

    [Fact]
    public void DifferentAddressesDoNotCompareEqual()
    {
        var first = MacAddress.Parse("01:0C:CD:04:00:01");
        var second = MacAddress.Parse("01:0C:CD:04:00:02");

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }
}
