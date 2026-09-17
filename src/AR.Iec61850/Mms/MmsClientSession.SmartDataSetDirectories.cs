namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Reads independent DataSet directories with the same association-aware bounded
    /// request window used by smart structural discovery. Each individual request is
    /// still executed through the existing observed directory reader, so P0-4 KPI
    /// accounting remains authoritative and no extra MMS request is introduced.
    /// Results are published in the original DataSet order, independent of worker
    /// completion order.
    /// </summary>
    private async Task<IReadOnlyList<MmsDataSetDirectoryResult>> GetObservedSmartDataSetDirectoriesPipelinedAsync(
        IReadOnlyList<string> dataSetReferences,
        MmsIedModelDirectory directory,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        var workerCount = MmsSmartDataSetPipelinePolicy.ResolveWorkerCount(
            dataSetReferences.Count,
            maxConcurrency);
        if (workerCount == 0)
            return Array.Empty<MmsDataSetDirectoryResult>();

        var results = new MmsDataSetDirectoryResult?[dataSetReferences.Count];
        var nextIndex = -1;
        var workers = new Task[workerCount];

        for (var worker = 0; worker < workerCount; worker++)
            workers[worker] = WorkerAsync();

        await Task.WhenAll(workers).ConfigureAwait(false);

        // Keep publication deterministic: request completion may be concurrent, but
        // consumers see the exact canonical DataSet reference order supplied by the
        // discovery inventory.
        return results
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();

        async Task WorkerAsync()
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsMmsInitiated)
                    return;

                var index = Interlocked.Increment(ref nextIndex);
                if (index >= dataSetReferences.Count)
                    return;

                var singleton = new[] { dataSetReferences[index] };
                var observed = await GetObservedSmartDataSetDirectoriesAsync(
                        singleton,
                        directory,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (observed.Count > 0)
                    results[index] = observed[0];

                if (!IsMmsInitiated)
                    return;
            }
        }
    }
}
