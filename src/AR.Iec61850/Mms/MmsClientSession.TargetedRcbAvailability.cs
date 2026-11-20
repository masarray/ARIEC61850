namespace AR.Iec61850.Mms;

/// <summary>
/// Minimal live-state probe used only after an exact RCB target has already been chosen.
///
/// The broad diagnostic probe reads many presentation/configuration attributes. The runtime
/// hot path instead tries one complete RCB structure read, then fills only missing DatSet,
/// RptEna, reservation and Owner evidence required for safe availability decisions.
/// </summary>
public sealed partial class MmsClientSession
{
    private async Task<int> ProbeTargetedReportControlAvailabilityAsync(
        MmsReportControlCandidate reportControl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reportControl);

        var logicalReads = 0;

        // Prefer one complete RCB structure read. On interoperable peers this provides
        // DatSet + RptEna + reservation state in one confirmed service round trip.
        await TryReadReportControlStructureAsync(reportControl, cancellationToken).ConfigureAwait(false);
        logicalReads++;

        if (MmsRcbAvailabilityEvaluator.ParseBool(reportControl.EnabledState) is null)
        {
            await ProbeReportControlAttributeAsync(
                reportControl,
                "RptEna",
                value => reportControl.EnabledState = NormalizeReportAttributeText(value),
                cancellationToken).ConfigureAwait(false);
            logicalReads++;
        }

        if (string.IsNullOrWhiteSpace(reportControl.DataSetReference))
        {
            await ProbeReportControlAttributeAsync(
                reportControl,
                "DatSet",
                value =>
                {
                    var text = NormalizeReportAttributeText(value);
                    reportControl.DataSetReference = string.IsNullOrWhiteSpace(text)
                        ? string.Empty
                        : NormalizeReportedDataSetReference(reportControl.Domain, text);
                },
                cancellationToken).ConfigureAwait(false);
            logicalReads++;
        }

        if (reportControl.Buffered)
        {
            if (MmsRcbAvailabilityEvaluator.ParseUnsigned(reportControl.ReservationTimeSeconds) is null &&
                reportControl.Attributes.Contains("ResvTms", StringComparer.OrdinalIgnoreCase))
            {
                await ProbeReportControlAttributeAsync(
                    reportControl,
                    "ResvTms",
                    value => reportControl.ReservationTimeSeconds = NormalizeReportAttributeText(value),
                    cancellationToken).ConfigureAwait(false);
                logicalReads++;
            }
        }
        else if (MmsRcbAvailabilityEvaluator.ParseBool(reportControl.ReservationState) is null &&
                 reportControl.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase))
        {
            await ProbeReportControlAttributeAsync(
                reportControl,
                "Resv",
                value => reportControl.ReservationState = NormalizeReportAttributeText(value),
                cancellationToken).ConfigureAwait(false);
            logicalReads++;
        }

        // Owner is outside the base structure on many devices. If exposed, one exact
        // read is cheaper and safer than inferring ownership from missing metadata.
        if (reportControl.Attributes.Contains("Owner", StringComparer.OrdinalIgnoreCase))
        {
            await ProbeOwnerReadOnlyAsync(reportControl, cancellationToken).ConfigureAwait(false);
            logicalReads++;
        }

        reportControl.Status = "Targeted-availability-probed";
        return logicalReads;
    }
}
