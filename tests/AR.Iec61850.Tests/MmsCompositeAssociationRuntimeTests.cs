using AR.Iec61850.Mms;
using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests;

public sealed class MmsCompositeAssociationRuntimeTests
{
    [Fact]
    public void Write_FallsThroughToApplicationRuntime()
    {
        using var reporting = new StubRuntime();
        using var process = new StubRuntime
        {
            WriteHandler = (target, value) => target.EndsWith("$Oper", StringComparison.OrdinalIgnoreCase)
                ? (true, 0)
                : (false, 0)
        };
        using var composite = new MmsCompositeAssociationRuntime(reporting, process);

        var handled = composite.TryWriteRcbAttribute(
            "ARVAVR1/YLTC1$CO$TapChg$Oper",
            MmsDataValue.Structure([MmsDataValue.Integer(2)]),
            out var error);

        Assert.True(handled);
        Assert.Equal(0, error);
        Assert.Equal(1, process.WriteCount);
    }

    [Fact]
    public void Read_FirstClaimingRuntimeWins()
    {
        using var first = new StubRuntime
        {
            ReadHandler = target => target == "owned"
                ? (true, MmsDataValue.VisibleString("first"))
                : (false, MmsDataValue.Boolean(false))
        };
        using var second = new StubRuntime
        {
            ReadHandler = _ => (true, MmsDataValue.VisibleString("second"))
        };
        using var composite = new MmsCompositeAssociationRuntime(first, second);

        Assert.True(composite.TryReadRcbAttribute("owned", out var value));
        Assert.Equal("first", value.Value);
        Assert.Equal(0, second.ReadCount);
    }

    [Fact]
    public void Dispose_DisposesOwnedRuntimesExactlyOnce()
    {
        var first = new StubRuntime();
        var second = new StubRuntime();
        var composite = new MmsCompositeAssociationRuntime(first, second);

        composite.Dispose();
        composite.Dispose();

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    private sealed class StubRuntime : IMmsAssociationRuntime, IDisposable
    {
        public Func<string, (bool handled, MmsDataValue value)>? ReadHandler { get; init; }
        public Func<string, MmsDataValue, (bool handled, int error)>? WriteHandler { get; init; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int DisposeCount { get; private set; }

        public bool TryReadRcbAttribute(string iecTarget, out MmsDataValue value)
        {
            ReadCount++;
            var result = ReadHandler?.Invoke(iecTarget) ?? (false, MmsDataValue.Boolean(false));
            value = result.value;
            return result.handled;
        }

        public bool TryWriteRcbAttribute(string iecTarget, MmsDataValue value, out int dataAccessError)
        {
            WriteCount++;
            var result = WriteHandler?.Invoke(iecTarget, value) ?? (false, 0);
            dataAccessError = result.error;
            return result.handled;
        }

        public void Dispose() => DisposeCount++;
    }
}
