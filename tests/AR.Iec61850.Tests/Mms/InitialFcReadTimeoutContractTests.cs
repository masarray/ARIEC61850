using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class InitialFcReadTimeoutContractTests
{
    [Fact]
    public async Task Executor_InvalidPlan_Preserves_Explicit_Batch_Timeout_Without_Network()
    {
        await using var session = new MmsClientSession();
        var timeout = TimeSpan.FromMilliseconds(750);

        var result = await session.ExecuteInitialFcReadPlanAsync(
            new InitialFcReadPlan(),
            timeout);

        Assert.Equal(InitialFcReadExecutionStatus.InvalidPlan, result.Status);
        Assert.Equal(timeout, result.PerBatchTimeout);
        Assert.False(session.IsTcpConnected);
    }
}
