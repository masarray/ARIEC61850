using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;

namespace AR.Iec61850.Mms;

/// <summary>
/// Capability-aware orchestration around the stable static -> dynamic -> polling planner.
/// Static RCB coverage keeps the stable planner's original fresh-availability semantics;
/// association capability qualification is an additional guard only for dynamic mutation.
/// </summary>
public sealed class MmsCapabilityAwareHybridReportAcquisitionPlan
{
    public MmsHybridReportAcquisitionPlan AcquisitionPlan { get; init; } = new();
    public MmsReportAssociationCapability AssociationCapability { get; init; } = new();
    public bool AutomaticDynamicActivationQuarantined { get; init; }

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

        // P6.1 stability rule:
        // Do not place the P3 capability wrapper in front of the stable static planner.
        // A populated DataSet discovered with an exact fresh directory is static protocol
        // evidence and must remain visible to MmsHybridReportAcquisitionPlanner, which owns
        // the established static-usability checks.
        //
        // P6.2-B field-stability rule:
        // The single-member Define/GetAttributes/Delete probation proved that an association
        // can support the NamedVariableList service while a later full dynamic DataSet still
        // aborts that same association. Therefore advertised capability or a successful
        // one-member probation is not sufficient permission for automatic full dynamic
        // activation on the production monitoring association. Keep the dynamic primitive
        // available for explicit diagnostics, but automatic acquisition is static -> polling
        // until staged multi-member interoperability has been proven independently.
        var configuredStaticReferences = availability.ReportControls
            .Where(HasConfiguredStaticDataSetEvidence)
            .Select(snapshot => Normalize(snapshot.Reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var restrictedAvailability = new MmsRcbAvailabilityResult
        {
            CheckedAtUtc = availability.CheckedAtUtc,
            ReportControls = availability.ReportControls
                .Where(snapshot => configuredStaticReferences.Contains(Normalize(snapshot.Reference)))
                .ToArray(),
            Warnings = availability.Warnings
                .Concat(capability.Warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        var restrictedInventory = new MmsReportInventory();
        restrictedInventory.DataSets.AddRange(inventory.DataSets);
        restrictedInventory.ReportControls.AddRange(inventory.ReportControls
            .Where(candidate => configuredStaticReferences.Contains(Normalize(candidate.Reference))));

        var automaticOptions = AutomaticMonitoringOptions(options);
        var plan = MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            requestedSignals,
            restrictedInventory,
            restrictedAvailability,
            liveDirectory,
            automaticOptions);

        RestoreFreshAttributeEvidence(plan, availability);

        return new MmsCapabilityAwareHybridReportAcquisitionPlan
        {
            AcquisitionPlan = plan,
            AssociationCapability = capability,
            AutomaticDynamicActivationQuarantined =
                capability.MayAttemptDynamicReports &&
                (options.AllowDynamicBrcb || options.AllowDynamicUrcb)
        };
    }

    private static MmsHybridReportAcquisitionOptions AutomaticMonitoringOptions(
        MmsHybridReportAcquisitionOptions source)
        => new()
        {
            MaxStaticReportPlans = source.MaxStaticReportPlans,
            MaxDynamicReportPlans = source.MaxDynamicReportPlans,
            MaxDynamicMembersPerReport = source.MaxDynamicMembersPerReport,
            RequireExactAvailabilityEvidence = source.RequireExactAvailabilityEvidence,
            AllowCallerOwnedReports = source.AllowCallerOwnedReports,
            AllowStaticBrcb = source.AllowStaticBrcb,
            AllowStaticUrcb = source.AllowStaticUrcb,
            // P6.2-B: automatic full dynamic DataSet activation is quarantined after
            // physical evidence that a successful single-member NVL probation does not
            // guarantee association survival for the full member set.
            AllowDynamicBrcb = false,
            AllowDynamicUrcb = false,
            AllowPollingFallback = source.AllowPollingFallback
        };

    private static bool HasConfiguredStaticDataSetEvidence(MmsRcbAvailabilitySnapshot snapshot)
        => snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded &&
           snapshot.DataSetDirectorySuccess &&
           snapshot.DataSetMembers.Count > 0 &&
           !string.IsNullOrWhiteSpace(snapshot.DataSetReference);

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
            candidate.Status = segment.Kind is MmsHybridAcquisitionKind.StaticBrcb or MmsHybridAcquisitionKind.StaticUrcb
                ? "P6.1 baseline-static fresh availability snapshot"
                : "P6.2-B capability-qualified dynamic fresh availability snapshot";
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
