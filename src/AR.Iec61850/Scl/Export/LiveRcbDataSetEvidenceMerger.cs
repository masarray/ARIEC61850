using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Scl.Export;

/// <summary>
/// Reconciles a live discovery model with the exact DataSet directory read during
/// the passive RCB availability check. Some IEDs expose a DataSet count during the
/// initial scan but only return the FCDA member references through an explicit
/// GetDataSetDirectory request. The source model is never mutated.
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

        var evidence = availability.ReportControls.FirstOrDefault(snapshot =>
            Normalize(snapshot.Reference).Equals(selectedReference, StringComparison.OrdinalIgnoreCase));
        if (evidence is null)
            throw new InvalidOperationException(
                $"No live DataSet directory evidence is available for '{selectedReportControlReference}'. Run Check Availability before exporting this RCB.");
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

        return new LiveIedModelDiscoveryDocument
        {
            SchemaVersion = source.SchemaVersion,
            GeneratedAtUtc = source.GeneratedAtUtc,
            Source = source.Source,
            Host = source.Host,
            Port = source.Port,
            IedName = source.IedName,
            IedIdentity = source.IedIdentity,
            AccessPointName = source.AccessPointName,
            Summary = $"{source.Summary} Live DataSet directory evidence merged for selected-RCB export.",
            Coverage = source.Coverage,
            LogicalDevices = source.LogicalDevices,
            FileDirectory = source.FileDirectory,
            DataSets = mergedDataSets,
            ReportControls = source.ReportControls,
            GooseControlBlocks = source.GooseControlBlocks,
            SampledValueControlBlocks = source.SampledValueControlBlocks,
            SettingGroupControls = source.SettingGroupControls,
            LogControls = source.LogControls,
            TypeTemplates = source.TypeTemplates,
            VariableTypeDiscoveries = source.VariableTypeDiscoveries,
            Warnings = source.Warnings
        };
    }

    private static string Normalize(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
