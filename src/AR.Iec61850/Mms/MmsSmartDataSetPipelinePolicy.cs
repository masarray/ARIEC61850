namespace AR.Iec61850.Mms;

/// <summary>
/// Pure policy helpers for the smart DataSet-directory pipeline. Keeping the worker
/// budget calculation separate makes the negotiated-request-window invariant easy to
/// regression test without network traffic.
/// </summary>
public static class MmsSmartDataSetPipelinePolicy
{
    public static int ResolveWorkerCount(int dataSetCount, int maxConcurrency)
    {
        if (dataSetCount <= 0)
            return 0;

        return Math.Min(dataSetCount, Math.Max(1, maxConcurrency));
    }
}
