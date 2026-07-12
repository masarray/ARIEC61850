using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850StandardEnumRegistryTests
{
    [Fact]
    public void Behaviour_Uses_Standard_On_Test_And_Off_Ordinals()
    {
        var definition = Iec61850StandardEnumRegistry.Resolve("LLN0", "Beh", "INS", "stVal");

        Assert.Equal("on", definition.Values.Single(value => value.Ord == 1).Symbol);
        Assert.Equal("test", definition.Values.Single(value => value.Ord == 3).Symbol);
        Assert.Equal("off", definition.Values.Single(value => value.Ord == 5).Symbol);
    }
}
