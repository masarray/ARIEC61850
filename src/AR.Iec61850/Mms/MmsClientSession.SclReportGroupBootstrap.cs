using AR.Iec61850.Scl;

namespace AR.Iec61850.Mms;

public sealed class MmsSclReportGroupBootstrapItem
{
    public required SclReportControl ReportControl { get; init; }
    public MmsSclRcbFamilyResolution FamilyResolution { get; init; } = new();
    public MmsSclInitialReportBootstrapResult? Bootstrap { get; init; }
    public string Message { get; init; } = string.Empty;

    public bool IsMonitoring => Bootstrap?.IsMonitoring == true;
    public bool HasInitialValues => Bootstrap?.HasInitialValues == true;
    public MmsPersistentReportMonitorSession? Session => Bootstrap?.Bootstrap?.Session;
    public IReadOnlyList<MmsReportFrame> InitialReports => Bootstrap?.Bootstrap?.InitialReports ?? Array.Empty<MmsReportFrame>();
}

public sealed class MmsSclReportGroupBootstrapResult
{
    public IReadOnlyList<MmsSclReportGroupBootstrapItem> Items { get; init; } = Array.Empty<MmsSclReportGroupBootstrapItem>();
    public IReadOnlyList<MmsDataSetDirectoryResult> DataSetDirectories { get; init; } = Array.Empty<MmsDataSetDirectoryResult>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<MmsPersistentReportMonitorSession> Sessions => Items
        .Select(item => item.Session)
        .Where(session => session is { IsStopped: false })
        .Cast<MmsPersistentReportMonitorSession>()
        .ToArray();

    public IReadOnlyList<MmsReportFrame> InitialReports => Items
        .SelectMany(item => item.InitialReports)
        .ToArray();

    public int MonitoringCount => Sessions.Count;
    public int InitialValueFamilyCount => Items.Count(item => item.HasInitialValues);
    public bool HasAnyMonitoring => MonitoringCount > 0;

    public string Summary =>
        $"SCL report group bootstrap: declared={Items.Count}, monitoring={MonitoringCount}, initial-value-families={InitialValueFamilyCount}, warnings={Warnings.Count}.";
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Starts the SCL-declared static report families that can be reconciled to
    /// concrete live MMS RCB instances. The method is intended for application
    /// Connect flows that need an IEDScout-like initial state without polling:
    /// reconcile -> refresh runtime state -> resolve DataSets -> reserve/enable
    /// -> register report routing -> one-shot GI -> initial values.
    ///
    /// Each family is fail-closed independently. A family that cannot be proven
    /// safe is reported as blocked and receives no report-control write. Concrete
    /// RCB instances already claimed by an earlier family are excluded from later
    /// planning. The returned monitor sessions must be stopped before the MMS
    /// association is disposed.
    /// </summary>
    public async Task<MmsSclReportGroupBootstrapResult> StartSclPersistentReportMonitorsWithInitialGiAsync(
        IReadOnlyList<SclReportControl> reportControls,
        MmsReportInventory liveInventory,
        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories,
        TimeSpan initialReportTimeout,
        bool allowUrCbFallback = false,
        bool allowPollingFallback = false,
        bool deleteDynamicDataSetOnStop = false,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(reportControls);
        ArgumentNullException.ThrowIfNull(liveInventory);
        ArgumentNullException.ThrowIfNull(dataSetDirectories);
        if (initialReportTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(initialReportTimeout), "Initial report timeout must be positive.");

        var familyResolutions = reportControls
            .Select(reportControl => new
            {
                ReportControl = reportControl,
                Resolution = MmsSclRcbFamilyResolver.Resolve(reportControl, liveInventory.ReportControls)
            })
            .ToArray();

        // Runtime ownership/enable state is authoritative. Refresh every concrete
        // member of each relevant family before any planner is allowed to write.
        var concreteCandidates = familyResolutions
            .Where(item => item.Resolution.IsSuccess)
            .SelectMany(item => item.Resolution.Candidates)
            .DistinctBy(candidate => candidate.Reference, StringComparer.Ordinal)
            .ToArray();

        var warnings = new List<string>();
        foreach (var candidate in concreteCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ProbeReportControlAttributesAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"RCB runtime refresh failed for {candidate.Reference}: {ex.GetType().Name}: {ex.Message}");
                if (!IsMmsInitiated)
                    break;
            }
        }

        var mergedDirectories = dataSetDirectories
            .Where(result => !string.IsNullOrWhiteSpace(result.DataSetReference))
            .GroupBy(result => NormalizeGroupDataSetReference(result.DataSetReference), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        if (IsMmsInitiated)
        {
            var knownDirectoryRefs = mergedDirectories
                .Select(result => NormalizeGroupDataSetReference(result.DataSetReference))
                .ToHashSet(StringComparer.Ordinal);
            var missingDataSets = concreteCandidates
                .Select(candidate => candidate.DataSetReference)
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Distinct(StringComparer.Ordinal)
                .Where(reference => !knownDirectoryRefs.Contains(NormalizeGroupDataSetReference(reference)))
                .ToArray();

            if (missingDataSets.Length > 0)
            {
                try
                {
                    var discoveredDirectories = await GetDataSetDirectoriesAsync(
                        missingDataSets,
                        directory,
                        cancellationToken).ConfigureAwait(false);
                    foreach (var result in discoveredDirectories)
                    {
                        var key = NormalizeGroupDataSetReference(result.DataSetReference);
                        var existing = mergedDirectories.FindIndex(item =>
                            NormalizeGroupDataSetReference(item.DataSetReference).Equals(key, StringComparison.Ordinal));
                        if (existing >= 0)
                            mergedDirectories[existing] = result;
                        else
                            mergedDirectories.Add(result);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    warnings.Add($"DataSet directory refresh failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        var claimedRcbReferences = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<MmsSclReportGroupBootstrapItem>(reportControls.Count);

        foreach (var family in familyResolutions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsMmsInitiated)
            {
                items.Add(new MmsSclReportGroupBootstrapItem
                {
                    ReportControl = family.ReportControl,
                    FamilyResolution = family.Resolution,
                    Message = "MMS association is no longer initiated; remaining SCL report families were not started."
                });
                continue;
            }

            if (!family.Resolution.IsSuccess)
            {
                items.Add(new MmsSclReportGroupBootstrapItem
                {
                    ReportControl = family.ReportControl,
                    FamilyResolution = family.Resolution,
                    Message = family.Resolution.Message
                });
                continue;
            }

            try
            {
                var bootstrap = await StartSclPersistentReportMonitorWithInitialGiAsync(
                    family.ReportControl,
                    liveInventory,
                    mergedDirectories,
                    initialReportTimeout,
                    allowUrCbFallback: allowUrCbFallback,
                    allowPollingFallback: allowPollingFallback,
                    excludedRcbReferences: claimedRcbReferences,
                    deleteDynamicDataSetOnStop: deleteDynamicDataSetOnStop,
                    directory: directory,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (bootstrap.Bootstrap?.Session?.ReportControl.Reference is { Length: > 0 } selectedReference)
                    claimedRcbReferences.Add(selectedReference);

                items.Add(new MmsSclReportGroupBootstrapItem
                {
                    ReportControl = family.ReportControl,
                    FamilyResolution = family.Resolution,
                    Bootstrap = bootstrap,
                    Message = bootstrap.Message
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var message = $"SCL report family bootstrap failed for {family.ReportControl.ControlBlockReference}: {ex.GetType().Name}: {ex.Message}";
                warnings.Add(message);
                items.Add(new MmsSclReportGroupBootstrapItem
                {
                    ReportControl = family.ReportControl,
                    FamilyResolution = family.Resolution,
                    Message = message
                });
            }
        }

        return new MmsSclReportGroupBootstrapResult
        {
            Items = items,
            DataSetDirectories = mergedDirectories.ToArray(),
            Warnings = warnings
        };
    }

    private static string NormalizeGroupDataSetReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');
}
