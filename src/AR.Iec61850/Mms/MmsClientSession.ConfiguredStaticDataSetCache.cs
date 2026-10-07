using System.Collections.Concurrent;

namespace AR.Iec61850.Mms;

/// <summary>
/// Association-scoped cache for immutable configured DataSet directories.
///
/// Only successfully read non-deletable DataSets are cached. Dynamic/deletable or
/// unknown-mutability DataSets are never cached because their membership can change.
/// The cache is cleared on every successful MMS association.
/// </summary>
public sealed partial class MmsClientSession
{
    private readonly ConcurrentDictionary<string, MmsDataSetDirectoryResult> _configuredStaticDataSetDirectoryEvidence =
        new(StringComparer.OrdinalIgnoreCase);

    private void ResetConfiguredStaticDataSetDirectoryEvidence()
        => _configuredStaticDataSetDirectoryEvidence.Clear();

    private async Task<(MmsDataSetDirectoryResult Result, bool CacheHit)> GetAvailabilityDataSetDirectoryAsync(
        string dataSetReference,
        MmsIedModelDirectory? directory,
        CancellationToken cancellationToken)
    {
        var key = MmsRcbAvailabilityEvaluator.NormalizeReference(dataSetReference);
        if (key.Length > 0 &&
            _configuredStaticDataSetDirectoryEvidence.TryGetValue(key, out var cached))
        {
            return (cached, true);
        }

        var result = await GetDataSetDirectoryAsync(
            dataSetReference,
            directory,
            cancellationToken).ConfigureAwait(false);

        if (key.Length > 0 &&
            result.IsSuccess &&
            result.IsDeletable == false)
        {
            _configuredStaticDataSetDirectoryEvidence[key] = result;
        }

        return (result, false);
    }
}
