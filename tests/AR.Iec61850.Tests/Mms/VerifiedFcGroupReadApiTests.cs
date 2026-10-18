using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class VerifiedFcGroupReadApiTests
{
    [Fact]
    public async Task GroupReadRequiresInitiatedAssociationAndNeverImplicitlyConnects()
    {
        await using var session = new MmsClientSession();
        var roots = new[]
        {
            new MmsObjectReference("LD", "SGgndPTOC1$CF0", ""),
            new MmsObjectReference("LD", "SGgndPTOC1$SG0", "")
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.ReadVerifiedVariablesBatchAsync(roots));
        Assert.Equal(MmsAssociationState.Disconnected, session.State);
    }

    [Fact]
    public async Task GroupReadRejectsDuplicateAndOversizedRootListsBeforeWire()
    {
        await using var session = new MmsClientSession();
        var one = new MmsObjectReference("LD", "SGgndPTOC1$SG0", "");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            session.ReadVerifiedVariablesBatchAsync([one, one]));
        var large = Enumerable.Range(1, 11)
            .Select(n => new MmsObjectReference("LD", "LN" + n + "$SG0", ""))
            .ToArray();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            session.ReadVerifiedVariablesBatchAsync(large));
    }
}
