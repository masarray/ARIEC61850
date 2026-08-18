using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// Capability-aware orchestration around the stable static -> dynamic -> polling planner.
/// The lower-level planner remains deterministic; this layer controls which RCBs are safe
/// to expose to it for the current MMS association.
/// </summary>
public sealed class MmsCapabilityAwareHybridReportAcquisitionPlan
{
    public MmsHybridReportAcquisitionPlan AcquisitionPlan { get; init; } = new();
    public MmsReportAssociationCapability AssociationCapability { get; init; } = new();

    public string Summary => $"{AcquisitionPlan.Summary} {AssociationCapability.Summary}";
    public IReadOnlyList<string> Warnings => AcquisitionPlan.Warnings
        .Concat(AssociationCapability.Warnings)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    public IReadOnlyList<string> Blockers => AcquisitionPlan.Blockers;
}

public static class MmsCapabilityAwareHybridReportAcquisitionPlanner
{
    public static MmsCapabilityAwareHybridReportAcquisitionPlan Build(
        Iec61850SignalCatalogDocument catalog,
        IEnumerable<Iec61850SignalDescriptor> requestedSignals,
        MmsReportInventory inventory,
        MmsRcbAvailabilityResult availability,
        MmsIedModelDirectory liveDirectory,
        AcseMmsNegotiatedCapabilities? negotiatedCapabilities = null,
        MmsHybridReportAcquisitionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(requestedSignals);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(liveDirectory);

        options ??= new MmsHybridReportAcquisitionOptions();
        var capability = MmsReportAssociationCapabilityEvaluator.Evaluate(
            availability,
            negotiatedCapabilities,
            options);

        var eligibleReferences = capability.ReportControls
            .Where(control => control.IsStaticWriteCandidate || control.IsDynamicWriteAttemptCandidate)
            .Select(control => Normalize(control.Reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var restrictedAvailability = new MmsRcbAvailabilityResult
        {
            CheckedAtUtc = availability.CheckedAtUtc,
            ReportControls = availability.ReportControls
                .Where(snapshot => eligibleReferences.Contains(Normalize(snapshot.Reference)))
                .ToArray(),
            Warnings = availability.Warnings
                .Concat(capability.Warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var restrictedInventory = new MmsReportInventory();
        restrictedInventory.DataSets.AddRange(inventory.DataSets);
        restrictedInventory.ReportControls.AddRange(inventory.ReportControls
            .Where(candidate => eligibleReferences.Contains(Normalize(candidate.Reference))));

        var plan = MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            requestedSignals,
            restrictedInventory,
            restrictedAvailability,
            liveDirectory,
            options);

        RestoreFreshAttributeEvidence(plan, availability);

        return new MmsCapabilityAwareHybridReportAcquisitionPlan
        {
            AcquisitionPlan = plan,
            AssociationCapability = capability
        };
    }

    private static void RestoreFreshAttributeEvidence(
        MmsHybridReportAcquisitionPlan plan,
        MmsRcbAvailabilityResult availability)
    {
        foreach (var segment in plan.Segments.Where(segment => segment.IsReportBacked && segment.ReportPlan?.ReportControl is not null))
        {
            var candidate = segment.ReportPlan!.ReportControl!;
            var snapshot = availability.ReportControls.FirstOrDefault(item =>
                Normalize(item.Reference).Equals(Normalize(candidate.Reference), StringComparison.OrdinalIgnoreCase));
            if (snapshot is null)
                continue;

            var attributes = snapshot.Attributes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            AddIfExposed(attributes, snapshot, "DatSet", snapshot.DataSetReference,
                snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded);
            AddIfExposed(attributes, snapshot, "RptEna", snapshot.EnabledState);
            AddIfExposed(attributes, snapshot, "TrgOps", snapshot.TriggerOptions);
            AddIfExposed(attributes, snapshot, "OptFlds", snapshot.OptionalFields);
            AddIfExposed(attributes, snapshot, "IntgPd", snapshot.IntegrityPeriodMs);
            AddIfExposed(attributes, snapshot, "GI", string.Empty);
            AddIfExposed(
                attributes,
                snapshot,
                snapshot.Buffered ? "ResvTms" : "Resv",
                snapshot.Buffered ? snapshot.ReservationTimeSeconds : snapshot.ReservationState);

            candidate.Attributes = attributes
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            candidate.ProbeDiagnostics.Clear();
            candidate.ProbeDiagnostics.AddRange(snapshot.ProbeDiagnostics);
            candidate.Status = "P3 capability-qualified fresh availability snapshot";
        }
    }

    private static void AddIfExposed(
        ISet<string> attributes,
        MmsRcbAvailabilitySnapshot snapshot,
        string attribute,
        string value,
        bool force = false)
    {
        if (force ||
            (!string.IsNullOrWhiteSpace(value) && value.Trim() != "-") ||
            snapshot.ProbeDiagnostics.Any(line =>
                line.StartsWith(attribute, StringComparison.OrdinalIgnoreCase) &&
                line.Contains(": OK", StringComparison.OrdinalIgnoreCase)))
        {
            attributes.Add(attribute);
        }
    }

    private static string Normalize(string? reference)
        => MmsRcbAvailabilityEvaluator.NormalizeReference(reference).Replace('\\', '/');
}
