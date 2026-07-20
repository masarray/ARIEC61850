using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Scl.Export;

/// <summary>
/// Reconciles a live discovery model with exact read-only evidence collected during
/// the RCB availability check. The source model is never mutated.
/// </summary>
public static class LiveRcbDataSetEvidenceMerger
{
    public static LiveIedModelDiscoveryDocument MergeSelectedDataSetDirectory(
        LiveIedModelDiscoveryDocument source,
        string selectedReportControlReference,
        MmsRcbAvailabilityResult availability)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedReportControlReference);
        ArgumentNullException.ThrowIfNull(availability);

        var selectedReference = Normalize(selectedReportControlReference);
        var selected = source.ReportControls.FirstOrDefault(control =>
            Normalize(control.Reference).Equals(selectedReference, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
            throw new InvalidOperationException($"ReportControl '{selectedReportControlReference}' was not found in the live model.");

        var evidence = FindEvidence(availability, selectedReportControlReference);
        if (!evidence.DataSetDirectorySuccess)
            throw new InvalidOperationException(
                $"The DataSet directory for '{selectedReportControlReference}' was not read successfully. Run Check Availability again before export.");
        if (evidence.DataSetMembers.Count == 0)
            throw new InvalidOperationException(
                $"The live DataSet directory for '{selectedReportControlReference}' contains no exportable members.");

        var dataSetReference = FirstNonEmpty(evidence.DataSetReference, selected.DataSetReference);
        var normalizedDataSet = Normalize(dataSetReference);
        var sourceDataSet = source.DataSets.FirstOrDefault(dataSet =>
            Normalize(dataSet.Reference).Equals(normalizedDataSet, StringComparison.OrdinalIgnoreCase));
        if (sourceDataSet is null)
            throw new InvalidOperationException(
                $"The live model does not contain DataSet '{dataSetReference}' referenced by '{selectedReportControlReference}'.");

        var members = evidence.DataSetMembers
            .Select((member, index) => new LiveIedDataSetMemberModel
            {
                Index = index,
                Reference = FirstNonEmpty(member.UserReference, member.MmsReference),
                FunctionalConstraint = member.FunctionalConstraint,
                MmsReference = member.MmsReference,
                Confidence = member.Confidence >= 90
                    ? LiveIedDiscoveryConfidenceLevel.Exact
                    : LiveIedDiscoveryConfidenceLevel.High
            })
            .Where(member => !string.IsNullOrWhiteSpace(member.Reference))
            .GroupBy(
                member => $"{Normalize(member.Reference)}|{member.FunctionalConstraint}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select((member, index) => new LiveIedDataSetMemberModel
            {
                Index = index,
                Reference = member.Reference,
                FunctionalConstraint = member.FunctionalConstraint,
                MmsReference = member.MmsReference,
                Confidence = member.Confidence
            })
            .ToArray();
        if (members.Length == 0)
            throw new InvalidOperationException(
                $"The live DataSet directory for '{selectedReportControlReference}' did not contain usable member references.");

        var mergedDataSet = new LiveIedDataSetModel
        {
            Reference = sourceDataSet.Reference,
            Domain = sourceDataSet.Domain,
            LogicalNode = sourceDataSet.LogicalNode,
            Name = sourceDataSet.Name,
            IsDeletable = evidence.DataSetIsDeletable ?? sourceDataSet.IsDeletable,
            MemberCount = members.Length,
            Members = members,
            UsedByReportControls = sourceDataSet.UsedByReportControls,
            UsedByGooseControls = sourceDataSet.UsedByGooseControls,
            UsedBySampledValueControls = sourceDataSet.UsedBySampledValueControls
        };

        var mergedDataSets = source.DataSets
            .Select(dataSet => Normalize(dataSet.Reference).Equals(normalizedDataSet, StringComparison.OrdinalIgnoreCase)
                ? mergedDataSet
                : dataSet)
            .ToArray();

        return CloneDocument(
            source,
            dataSets: mergedDataSets,
            reportControls: source.ReportControls,
            summarySuffix: "Live DataSet directory evidence merged for selected-RCB export.");
    }

    public static LiveIedModelDiscoveryDocument MergeSelectedReportControlEvidence(
        LiveIedModelDiscoveryDocument source,
        string selectedReportControlReference,
        MmsRcbAvailabilityResult availability)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedReportControlReference);
        ArgumentNullException.ThrowIfNull(availability);

        var selectedReference = Normalize(selectedReportControlReference);
        var sourceControl = source.ReportControls.FirstOrDefault(control =>
            Normalize(control.Reference).Equals(selectedReference, StringComparison.OrdinalIgnoreCase));
        if (sourceControl is null)
            throw new InvalidOperationException($"ReportControl '{selectedReportControlReference}' was not found in the live model.");

        var evidence = FindEvidence(availability, selectedReportControlReference);
        var mergedControl = new LiveIedReportControlModel
        {
            Reference = sourceControl.Reference,
            Domain = sourceControl.Domain,
            LogicalNode = sourceControl.LogicalNode,
            Name = sourceControl.Name,
            Buffered = sourceControl.Buffered,
            DataSetReference = FirstNonEmpty(evidence.DataSetReference, sourceControl.DataSetReference),
            ReportId = FirstNonEmpty(evidence.ReportId, sourceControl.ReportId),
            ConfRev = FirstNonEmpty(evidence.ConfRev, sourceControl.ConfRev),
            TriggerOptions = FirstNonEmpty(evidence.TriggerOptions, sourceControl.TriggerOptions),
            OptionalFields = FirstNonEmpty(evidence.OptionalFields, sourceControl.OptionalFields),
            BufferTimeMs = FirstNonEmpty(evidence.BufferTimeMs, sourceControl.BufferTimeMs),
            IntegrityPeriodMs = FirstNonEmpty(evidence.IntegrityPeriodMs, sourceControl.IntegrityPeriodMs),
            EnabledState = FirstNonEmpty(evidence.EnabledState, sourceControl.EnabledState),
            ReservationState = FirstNonEmpty(evidence.ReservationState, sourceControl.ReservationState),
            ReservationTimeSeconds = FirstNonEmpty(evidence.ReservationTimeSeconds, sourceControl.ReservationTimeSeconds),
            Status = sourceControl.Status
        };

        var reportControls = source.ReportControls
            .Select(control => Normalize(control.Reference).Equals(selectedReference, StringComparison.OrdinalIgnoreCase)
                ? mergedControl
                : control)
            .ToArray();

        return CloneDocument(
            source,
            source.DataSets,
            reportControls,
            "Exact live RCB configuration evidence merged for selected-RCB export.");
    }

    private static MmsRcbAvailabilitySnapshot FindEvidence(
        MmsRcbAvailabilityResult availability,
        string selectedReportControlReference)
    {
        var selectedReference = Normalize(selectedReportControlReference);
        return availability.ReportControls.FirstOrDefault(snapshot =>
                   Normalize(snapshot.Reference).Equals(selectedReference, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"No live RCB evidence is available for '{selectedReportControlReference}'. Run Check Availability before exporting this RCB.");
    }

    private static LiveIedModelDiscoveryDocument CloneDocument(
        LiveIedModelDiscoveryDocument source,
        IReadOnlyList<LiveIedDataSetModel> dataSets,
        IReadOnlyList<LiveIedReportControlModel> reportControls,
        string summarySuffix)
        => new()
        {
            SchemaVersion = source.SchemaVersion,
            GeneratedAtUtc = source.GeneratedAtUtc,
            Source = source.Source,
            Host = source.Host,
            Port = source.Port,
            IedName = source.IedName,
            IedIdentity = source.IedIdentity,
            AccessPointName = source.AccessPointName,
            Summary = $"{source.Summary} {summarySuffix}".Trim(),
            Coverage = source.Coverage,
            LogicalDevices = source.LogicalDevices,
            FileDirectory = source.FileDirectory,
            DataSets = dataSets,
            ReportControls = reportControls,
            GooseControlBlocks = source.GooseControlBlocks,
            SampledValueControlBlocks = source.SampledValueControlBlocks,
            SettingGroupControls = source.SettingGroupControls,
            LogControls = source.LogControls,
            TypeTemplates = source.TypeTemplates,
            VariableTypeDiscoveries = source.VariableTypeDiscoveries,
            Warnings = source.Warnings
        };

    private static string Normalize(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}