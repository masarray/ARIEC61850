using AR.Iec61850.Discovery;
using Xunit;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850ReferencePartsTests
{
    [Theory]
    [InlineData("RPRE_MMXU1", "RPRE_", "MMXU", "1")]
    [InlineData("FPRE_MMXN1", "FPRE_", "MMXN", "1")]
    [InlineData("I01ATCTR1", "I01A", "TCTR", "1")]
    [InlineData("I01ATCTR01", "I01A", "TCTR", "01")]
    [InlineData("GGIO1", "", "GGIO", "1")]
    [InlineData("CSWI1", "", "CSWI", "1")]
    [InlineData("LPHD1", "", "LPHD", "1")]
    public void ParseLogicalNodeName_AnchorsClassBeforeNumericInstance(
        string source,
        string expectedPrefix,
        string expectedClass,
        string expectedInstance)
    {
        var parsed = Iec61850ReferenceParts.ParseLogicalNodeName(source);

        Assert.Equal(source, parsed.Name);
        Assert.Equal(expectedPrefix, parsed.Prefix);
        Assert.Equal(expectedClass, parsed.LnClass);
        Assert.Equal(expectedClass, parsed.SclLnClass);
        Assert.Equal(expectedInstance, parsed.LnInst);
    }

    [Fact]
    public void ParseLogicalNodeName_PreservesLln0SpecialCase()
    {
        var parsed = Iec61850ReferenceParts.ParseLogicalNodeName("LLN0");

        Assert.Equal(string.Empty, parsed.Prefix);
        Assert.Equal("LLN0", parsed.LnClass);
        Assert.Equal(string.Empty, parsed.LnInst);
    }
}
