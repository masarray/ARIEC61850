namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    public async Task<MmsRcbAvailabilityResult> CheckReportControlAvailabilityAsync(
        MmsReportInventory inventory,
        MmsIedModelDirectory? directory = null,
        MmsRcbAvailabilityOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(inventory);
        options ??= new MmsRcbAvailabilityOptions();

        var checkedAt = DateTimeOffset.UtcNow;
        var warnings = new List<string>();
        var snapshots = new List<MmsRcbAvailabilitySnapshot>();
        var dataSetDirectories = new Dictionary<string, MmsDataSetDirectoryResult>(StringComparer.OrdinalIgnoreCase);
        var callerOwned = options.CallerOwnedRcbReferences
            .Select(MmsRcbAvailabilityEvaluator.NormalizeReference)
            .Where(reference => reference.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var max = Math.Clamp(options.MaxReportControls, 1, 4096);
        var candidates = inventory.ReportControls
            .OrderByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.DataSetReference))
            .ThenByDescending(candidate => candidate.Buffered)
            .ThenBy(candidate => candidate.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToArray();

        if (inventory.ReportControls.Count > candidates.Length)
            warnings.Add($"Availability check was bounded to {candidates.Length} of {inventory.ReportControls.Count} discovered RCBs.");

        foreach (var source in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = CloneReportControl(source);

            // Preserve discovery/SCL-derived binding only as fallback evidence. Clear the
            // candidate before the forced live probe so a successful empty DatSet read can
            // be distinguished from a failed read that merely left an old value in memory.
            var previouslyKnownDataSetReference = candidate.DataSetReference;
            candidate.DataSetReference = string.Empty;
            candidate.DataSetProbeState = MmsRcbDataSetProbeState.NotAttempted;
            candidate.DataSetProbeMessage = string.Empty;

            await ProbeReportControlAttributesAsync(candidate, cancellationToken).ConfigureAwait(false);
            CaptureDataSetProbeEvidence(candidate, previouslyKnownDataSetReference);
            await ProbeOwnerReadOnlyAsync(candidate, cancellationToken).ConfigureAwait(false);

            MmsDataSetDirectoryResult? dataSetDirectory = null;
            var dataSetReference = MmsRcbAvailabilityEvaluator.NormalizeReference(candidate.DataSetReference);
            if (options.ReadDataSetDirectories && dataSetReference.Length > 0)
            {
                if (!dataSetDirectories.TryGetValue(dataSetReference, out dataSetDirectory))
                {
                    dataSetDirectory = await GetDataSetDirectoryAsync(candidate.DataSetReference, directory, cancellationToken).ConfigureAwait(false);
                    dataSetDirectories[dataSetReference] = dataSetDirectory;
                }
            }

            snapshots.Add(MmsRcbAvailabilityEvaluator.Evaluate(
                candidate,
                dataSetDirectory,
                callerOwned.Contains(MmsRcbAvailabilityEvaluator.NormalizeReference(candidate.Reference)),
                checkedAt));
        }

        return new MmsRcbAvailabilityResult
        {
            CheckedAtUtc = checkedAt,
            ReportControls = snapshots
                .OrderByDescending(item => item.DataSetMemberCount > 0)
                .ThenBy(item => AvailabilityRank(item.Availability))
                .ThenByDescending(item => item.Buffered)
                .ThenBy(item => item.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Warnings = warnings
        };
    }

    private static void CaptureDataSetProbeEvidence(
        MmsReportControlCandidate candidate,
        string previouslyKnownDataSetReference)
    {
        var datSetDiagnostic = candidate.ProbeDiagnostics
            .LastOrDefault(line => line.StartsWith("DatSet", StringComparison.OrdinalIgnoreCase));
        var directReadSucceeded = datSetDiagnostic?.Contains(": OK ", StringComparison.OrdinalIgnoreCase) == true;
        var structureReadSucceeded = !string.IsNullOrWhiteSpace(candidate.DataSetReference) &&
                                     candidate.ProbeDiagnostics.Any(line =>
                                         line.StartsWith("RCB base ", StringComparison.OrdinalIgnoreCase) &&
                                         line.Contains(": OK", StringComparison.OrdinalIgnoreCase));

        if (directReadSucceeded)
        {
            candidate.DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded;
            candidate.DataSetProbeMessage = datSetDiagnostic ?? "Live DatSet read succeeded.";
            return;
        }

        if (structureReadSucceeded)
        {
            candidate.DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded;
            candidate.DataSetProbeMessage = "Live DatSet binding was recovered from a successful complete RCB structure read.";
            return;
        }

        if (datSetDiagnostic is not null)
        {
            candidate.DataSetProbeState = MmsRcbDataSetProbeState.ReadFailed;
            candidate.DataSetProbeMessage = datSetDiagnostic;
        }
        else
        {
            candidate.DataSetProbeState = MmsRcbDataSetProbeState.NotAttempted;
            candidate.DataSetProbeMessage = "No explicit DatSet probe evidence was captured.";
        }

        // A failed live read must never erase a previously known reference, but it also
        // must never be interpreted as positive proof that the RCB has no DataSet.
        if (string.IsNullOrWhiteSpace(candidate.DataSetReference) &&
            !string.IsNullOrWhiteSpace(previouslyKnownDataSetReference))
        {
            candidate.DataSetReference = previouslyKnownDataSetReference;
        }
    }

    private async Task ProbeOwnerReadOnlyAsync(
        MmsReportControlCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            var reference = MmsObjectReference.Parse($"{candidate.Reference}.Owner", candidate.FunctionalConstraint);
            var read = await ReadSingleVariableAsync(reference, cancellationToken).ConfigureAwait(false);
            if (read.IsSuccess)
            {
                candidate.Owner = NormalizeReportAttributeText(read.Value);
                candidate.ProbeDiagnostics.Add($"Owner item={reference.Item}: OK {MmsDataValueRenderer.ToCompactString(read.Value)}");
            }
            else
            {
                candidate.ProbeDiagnostics.Add($"Owner item={reference.Item}: {read.Message}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            candidate.ProbeDiagnostics.Add($"Owner: unsupported or unreadable ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static MmsReportControlCandidate CloneReportControl(MmsReportControlCandidate source)
        => new()
        {
            Domain = source.Domain,
            LogicalNode = source.LogicalNode,
            FunctionalConstraint = source.FunctionalConstraint,
            Name = source.Name,
            Reference = source.Reference,
            Buffered = source.Buffered,
            DataSetReference = source.DataSetReference,
            DataSetProbeState = source.DataSetProbeState,
            DataSetProbeMessage = source.DataSetProbeMessage,
            ReportId = source.ReportId,
            ConfRev = source.ConfRev,
            IntegrityPeriodMs = source.IntegrityPeriodMs,
            EnabledState = source.EnabledState,
            ReservationState = source.ReservationState,
            ReservationTimeSeconds = source.ReservationTimeSeconds,
            Owner = source.Owner,
            BufferTimeMs = source.BufferTimeMs,
            TriggerOptions = source.TriggerOptions,
            OptionalFields = source.OptionalFields,
            Status = source.Status,
            Attributes = source.Attributes.ToList()
        };

    private static int AvailabilityRank(MmsRcbOperationalAvailability availability)
        => availability switch
        {
            MmsRcbOperationalAvailability.Available => 0,
            MmsRcbOperationalAvailability.UsedByCaller => 1,
            MmsRcbOperationalAvailability.InUse => 2,
            MmsRcbOperationalAvailability.Unknown => 3,
            MmsRcbOperationalAvailability.DataSetUnreadable => 4,
            MmsRcbOperationalAvailability.DataSetEmpty => 5,
            MmsRcbOperationalAvailability.NoDataSet => 6,
            _ => 9
        };
}
