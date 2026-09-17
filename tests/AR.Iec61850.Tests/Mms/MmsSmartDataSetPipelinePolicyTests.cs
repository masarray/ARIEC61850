using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsSmartDataSetPipelinePolicyTests
{
    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(1, 8, 1)]
    [InlineData(2, 8, 2)]
    [InlineData(8, 4, 4)]
    [InlineData(8, 0, 1)]
    [InlineData(32, 10, 10)]
    public void ResolveWorkerCount_RespectsAssociationWindowAndAvailableWork(
        int dataSetCount,
        int maxConcurrency,
        int expected)
    {
        Assert.Equal(
            expected,
            MmsSmartDataSetPipelinePolicy.ResolveWorkerCount(dataSetCount, maxConcurrency));
    }
}
