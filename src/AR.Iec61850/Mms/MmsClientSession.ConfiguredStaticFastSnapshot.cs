namespace AR.Iec61850.Mms;

/// <summary>
/// Hot-path RCB state capture for configured-static activation.
///
/// Normal discovery/diagnostic probing intentionally reads many attributes. Activation must
/// not repeat that broad scan. This path attempts one complete RCB structure read first and
/// falls back only to the exact leaf evidence still required for the imminent mutation.
/// There are no sleeps and no unrelated RCB/DataSet scans.
/// </summary>
public sealed partial class MmsClientSession
{
    private async Task<MmsReportRcbSnapshot> CaptureConfiguredStaticActivationSnapshotAsync(
        MmsReportControlCandidate source,
        string stage,
        bool includeReservationEvidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var clone = CloneReportControlCandidate(source);
        clone.EnabledState = string.Empty;
        clone.ReservationState = string.Empty;
        clone.ReservationTimeSeconds = string.Empty;
        clone.Owner = string.Empty;
        clone.DataSetReference = string.Empty;
        clone.DataSetProbeState = MmsRcbDataSetProbeState.NotAttempted;
        clone.DataSetProbeMessage = string.Empty;
        clone.ProbeDiagnostics.Clear();

        var logicalReads = 0;
        var structureRead = false;
        var leafFallbacks = 0;

        try
        {
            var baseReference = MmsObjectReference.Parse(
                source.Reference,
                source.FunctionalConstraint);
            var baseRead = await ReadSingleVariableAsync(
                baseReference,
                cancellationToken).ConfigureAwait(false);
            logicalReads++;

            clone.ProbeDiagnostics.Add(
                $"Activation RCB base {baseReference.Item}: {(baseRead.IsSuccess ? "OK" : baseRead.Message)}");

            if (baseRead.IsSuccess &&
                baseRead.Value is { Kind: MmsDataKind.Structure } structure &&
                structure.Children.Count > 0)
            {
                ApplyReportControlStructure(clone, structure);
                structureRead = true;

                // DatSet is element 2 for BRCB and element 3 for URCB in the standard RCB
                // structure layouts used by ApplyReportControlStructure.
                var dataSetIndex = source.Buffered ? 2 : 3;
                if (structure.Children.Count > dataSetIndex)
                {
                    clone.DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded;
                    clone.DataSetProbeMessage = "DatSet observed in complete RCB structure read.";
                }
            }

            if (MmsRcbAvailabilityEvaluator.ParseBool(clone.EnabledState) is null)
            {
                leafFallbacks++;
                logicalReads++;
                await ProbeReportControlAttributeAsync(
                    clone,
                    "RptEna",
                    value => clone.EnabledState = NormalizeReportAttributeText(value),
                    cancellationToken).ConfigureAwait(false);
            }

            // A positive live DatSet contradiction is safety-relevant. If the complete
            // structure did not provide the field, read only this one leaf.
            if (clone.DataSetProbeState != MmsRcbDataSetProbeState.ReadSucceeded)
            {
                leafFallbacks++;
                logicalReads++;
                var beforeDiagnostics = clone.ProbeDiagnostics.Count;
                await ProbeReportControlAttributeAsync(
                    clone,
                    "DatSet",
                    value =>
                    {
                        var text = NormalizeReportAttributeText(value);
                        clone.DataSetReference = string.IsNullOrWhiteSpace(text)
                            ? string.Empty
                            : NormalizeReportedDataSetReference(clone.Domain, text);
                    },
                    cancellationToken).ConfigureAwait(false);

                var datSetDiagnostic = clone.ProbeDiagnostics
                    .Skip(beforeDiagnostics)
                    .LastOrDefault(line => line.StartsWith("DatSet", StringComparison.OrdinalIgnoreCase));
                if (datSetDiagnostic?.Contains(": OK ", StringComparison.OrdinalIgnoreCase) == true)
                {
                    clone.DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded;
                    clone.DataSetProbeMessage = datSetDiagnostic;
                }
                else
                {
                    clone.DataSetProbeState = MmsRcbDataSetProbeState.ReadFailed;
                    clone.DataSetProbeMessage = datSetDiagnostic ?? "DatSet activation leaf read did not produce exact evidence.";
                }
            }

            if (includeReservationEvidence)
            {
                if (source.Buffered)
                {
                    if (MmsRcbAvailabilityEvaluator.ParseUnsigned(clone.ReservationTimeSeconds) is null &&
                        source.Attributes.Contains("ResvTms", StringComparer.OrdinalIgnoreCase))
                    {
                        leafFallbacks++;
                        logicalReads++;
                        await ProbeReportControlAttributeAsync(
                            clone,
                            "ResvTms",
                            value => clone.ReservationTimeSeconds = NormalizeReportAttributeText(value),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (MmsRcbAvailabilityEvaluator.ParseBool(clone.ReservationState) is null &&
                         source.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase))
                {
                    leafFallbacks++;
                    logicalReads++;
                    await ProbeReportControlAttributeAsync(
                        clone,
                        "Resv",
                        value => clone.ReservationState = NormalizeReportAttributeText(value),
                        cancellationToken).ConfigureAwait(false);
                }

                // Owner is a separate optional leaf on many IEDs. Avoid paying this RTT
                // when explicit reservation evidence is already free. Probe it only when
                // reservation evidence remains ambiguous or earlier evidence was positive.
                var explicitFree = source.Buffered
                    ? MmsRcbAvailabilityEvaluator.ParseUnsigned(clone.ReservationTimeSeconds) == 0
                    : MmsRcbAvailabilityEvaluator.ParseBool(clone.ReservationState) == false;
                var earlierPositiveOwner = MmsRcbAvailabilityEvaluator.HasOwner(source.Owner);
                if (source.Attributes.Contains("Owner", StringComparer.OrdinalIgnoreCase) &&
                    (!explicitFree || earlierPositiveOwner))
                {
                    leafFallbacks++;
                    logicalReads++;
                    await ProbeOwnerReadOnlyAsync(clone, cancellationToken).ConfigureAwait(false);
                }
            }

            var hasExactEnable = MmsRcbAvailabilityEvaluator.ParseBool(clone.EnabledState).HasValue;
            var message =
                $"Configured-static JIT snapshot: exactRptEna={hasExactEnable.ToString().ToLowerInvariant()}, " +
                $"structureRead={structureRead.ToString().ToLowerInvariant()}, logicalReads={logicalReads}, " +
                $"leafFallbacks={leafFallbacks}, dataSetProbe={clone.DataSetProbeState}.";

            return MmsReportRcbSnapshot.FromCandidate(
                stage,
                clone,
                success: hasExactEnable,
                message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return MmsReportRcbSnapshot.FromCandidate(
                stage,
                clone,
                success: false,
                $"Configured-static JIT snapshot failed after logicalReads={logicalReads}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
