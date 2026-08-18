using AR.Iec61850.Acse;

namespace AR.Iec61850.Mms;

public enum MmsCapabilityEvidenceState
{
    Unknown,
    Advertised,
    Exposed,
    Proven,
    Unsupported
}

/// <summary>
/// Evidence-backed capability view for one RCB in the current association. Exposed means
/// the field was discovered/read, not that a write has already succeeded. The activation
/// path remains the final authority for actual write success.
/// </summary>
public sealed class MmsReportControlCapability
{
    public string Reference { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public MmsRcbOperationalAvailability Availability { get; init; }
    public MmsRcbAvailabilityConfidence Confidence { get; init; }
    public bool IsCallerOwned { get; init; }
    public bool IsExplicitlyFree { get; init; }
    public MmsCapabilityEvidenceState DataSetBindingAccess { get; init; }
    public MmsCapabilityEvidenceState ReportEnableAccess { get; init; }
    public MmsCapabilityEvidenceState TriggerOptionsAccess { get; init; }
    public MmsCapabilityEvidenceState OptionalFieldsAccess { get; init; }
    public MmsCapabilityEvidenceState GeneralInterrogationAccess { get; init; }
    public MmsCapabilityEvidenceState IntegrityAccess { get; init; }
    public MmsCapabilityEvidenceState ReservationAccess { get; init; }
    public MmsCapabilityEvidenceState OwnerAccess { get; init; }
    public bool IsStaticWriteCandidate { get; init; }
    public bool IsDynamicWriteAttemptCandidate { get; init; }
    public string Reason { get; init; } = string.Empty;

    public string Type => Buffered ? "BRCB" : "URCB";
    public string Summary =>
        $"{Type} {Reference}: availability={Availability}/{Confidence}, free={IsExplicitlyFree}, " +
        $"DatSet={DataSetBindingAccess}, RptEna={ReportEnableAccess}, TrgOps={TriggerOptionsAccess}, dynamicCandidate={IsDynamicWriteAttemptCandidate}.";
}

/// <summary>
/// Association-scoped reporting capability assembled from negotiated MMS service bits and
/// fresh RCB evidence. It intentionally distinguishes a planner limit from any unknown IED
/// implementation limit; IEC 61850/MMS does not negotiate a DataSet member-count maximum.
/// </summary>
public sealed class MmsReportAssociationCapability
{
    public DateTimeOffset EvaluatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public MmsCapabilityEvidenceState WriteService { get; init; }
    public MmsCapabilityEvidenceState DefineNamedVariableListService { get; init; }
    public MmsCapabilityEvidenceState DeleteNamedVariableListService { get; init; }
    public int? NegotiatedMaxMmsPduSize { get; init; }
    public int? NegotiatedMaxOutstandingCalling { get; init; }
    public int? NegotiatedMaxOutstandingCalled { get; init; }
    public int? NegotiatedDataStructureNestingLevel { get; init; }
    public int FreeBrcbCount { get; init; }
    public int FreeUrcbCount { get; init; }
    public int DynamicBrcbSlotCount { get; init; }
    public int DynamicUrcbSlotCount { get; init; }
    public int MaxObservedStaticDataSetMembers { get; init; }
    public int PlannerDynamicMemberLimit { get; init; }
    public bool MayAttemptStaticWrites { get; init; }
    public bool MayAttemptDynamicReports { get; init; }
    public IReadOnlyList<MmsReportControlCapability> ReportControls { get; init; } = Array.Empty<MmsReportControlCapability>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public MmsReportControlCapability? FindReportControl(string? reference)
    {
        var normalized = MmsRcbAvailabilityEvaluator.NormalizeReference(reference);
        return ReportControls.FirstOrDefault(item =>
            MmsRcbAvailabilityEvaluator.NormalizeReference(item.Reference)
                .Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public string Summary =>
        $"Association report capability: write={WriteService}, defineNVL={DefineNamedVariableListService}, deleteNVL={DeleteNamedVariableListService}, " +
        $"freeBRCB={FreeBrcbCount}, freeURCB={FreeUrcbCount}, dynamicBRCB={DynamicBrcbSlotCount}, dynamicURCB={DynamicUrcbSlotCount}, " +
        $"dynamicAllowed={MayAttemptDynamicReports}, plannerMemberLimit={PlannerDynamicMemberLimit}, observedStaticMax={MaxObservedStaticDataSetMembers}.";
}

public static class MmsReportAssociationCapabilityEvaluator
{
    public static MmsReportAssociationCapability Evaluate(
        MmsRcbAvailabilityResult availability,
        AcseMmsNegotiatedCapabilities? negotiatedCapabilities = null,
        MmsHybridReportAcquisitionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(availability);
        negotiatedCapabilities ??= AcseMmsNegotiatedCapabilities.Unknown;
        options ??= new MmsHybridReportAcquisitionOptions();

        var writeService = ServiceEvidence(negotiatedCapabilities.SupportsWrite);
        var defineService = ServiceEvidence(negotiatedCapabilities.SupportsDefineNamedVariableList);
        var deleteService = ServiceEvidence(negotiatedCapabilities.SupportsDeleteNamedVariableList);
        var serviceAllowsStaticWrite = writeService != MmsCapabilityEvidenceState.Unsupported;
        var serviceAllowsDynamicWrite = serviceAllowsStaticWrite &&
                                        defineService != MmsCapabilityEvidenceState.Unsupported &&
                                        deleteService != MmsCapabilityEvidenceState.Unsupported;

        var controls = availability.ReportControls
            .Select(snapshot => EvaluateControl(snapshot, options, serviceAllowsStaticWrite, serviceAllowsDynamicWrite))
            .OrderByDescending(control => control.IsDynamicWriteAttemptCandidate)
            .ThenByDescending(control => control.IsStaticWriteCandidate)
            .ThenBy(control => control.Buffered ? 0 : 1)
            .ThenBy(control => control.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var warnings = new List<string>();
        if (!negotiatedCapabilities.IsDecoded)
            warnings.Add("MMS InitiateResponse service support was not decoded; unknown service bits are not treated as unsupported, and live RCB/write evidence remains authoritative.");
        if (writeService == MmsCapabilityEvidenceState.Unsupported)
            warnings.Add("The server explicitly did not advertise MMS Write; automatic RCB writes are withheld for this association.");
        if (defineService == MmsCapabilityEvidenceState.Unsupported)
            warnings.Add("The server explicitly did not advertise DefineNamedVariableList; dynamic DataSet creation is withheld.");
        if (deleteService == MmsCapabilityEvidenceState.Unsupported)
            warnings.Add("The server explicitly did not advertise DeleteNamedVariableList; automatic temporary dynamic DataSets are withheld to avoid uncleanable server resources.");

        var noTriggerSlots = availability.ReportControls
            .Where(snapshot => snapshot.Availability == MmsRcbOperationalAvailability.NoDataSet)
            .Count(snapshot => Evidence(snapshot, "TrgOps", snapshot.TriggerOptions) == MmsCapabilityEvidenceState.Unknown);
        if (noTriggerSlots > 0)
            warnings.Add($"{noTriggerSlots} verified-empty RCB slot(s) do not expose readable TrgOps evidence and are not promoted to dynamic-write candidates.");

        return new MmsReportAssociationCapability
        {
            EvaluatedAtUtc = availability.CheckedAtUtc,
            WriteService = writeService,
            DefineNamedVariableListService = defineService,
            DeleteNamedVariableListService = deleteService,
            NegotiatedMaxMmsPduSize = negotiatedCapabilities.MaxMmsPduSize,
            NegotiatedMaxOutstandingCalling = negotiatedCapabilities.MaxOutstandingCalling,
            NegotiatedMaxOutstandingCalled = negotiatedCapabilities.MaxOutstandingCalled,
            NegotiatedDataStructureNestingLevel = negotiatedCapabilities.DataStructureNestingLevel,
            FreeBrcbCount = controls.Count(control => control.Buffered && control.IsExplicitlyFree),
            FreeUrcbCount = controls.Count(control => !control.Buffered && control.IsExplicitlyFree),
            DynamicBrcbSlotCount = controls.Count(control => control.Buffered && control.IsDynamicWriteAttemptCandidate),
            DynamicUrcbSlotCount = controls.Count(control => !control.Buffered && control.IsDynamicWriteAttemptCandidate),
            MaxObservedStaticDataSetMembers = availability.ReportControls
                .Where(snapshot => snapshot.DataSetDirectorySuccess)
                .Select(snapshot => snapshot.DataSetMemberCount)
                .DefaultIfEmpty(0)
                .Max(),
            PlannerDynamicMemberLimit = options.MaxDynamicMembersPerReport,
            MayAttemptStaticWrites = controls.Any(control => control.IsStaticWriteCandidate),
            MayAttemptDynamicReports = controls.Any(control => control.IsDynamicWriteAttemptCandidate),
            ReportControls = controls,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static MmsReportControlCapability EvaluateControl(
        MmsRcbAvailabilitySnapshot snapshot,
        MmsHybridReportAcquisitionOptions options,
        bool serviceAllowsStaticWrite,
        bool serviceAllowsDynamicWrite)
    {
        var dataSet = snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded
            ? MmsCapabilityEvidenceState.Exposed
            : Evidence(snapshot, "DatSet", snapshot.DataSetReference);
        var reportEnable = Evidence(snapshot, "RptEna", snapshot.EnabledState);
        var triggerOptions = Evidence(snapshot, "TrgOps", snapshot.TriggerOptions);
        var optionalFields = Evidence(snapshot, "OptFlds", snapshot.OptionalFields);
        var generalInterrogation = Evidence(snapshot, "GI", string.Empty);
        var integrity = Evidence(snapshot, "IntgPd", snapshot.IntegrityPeriodMs);
        var reservation = snapshot.Buffered
            ? Evidence(snapshot, "ResvTms", snapshot.ReservationTimeSeconds)
            : Evidence(snapshot, "Resv", snapshot.ReservationState);
        var owner = Evidence(snapshot, "Owner", snapshot.Owner);
        var free = IsExplicitlyFree(snapshot);
        var exactEnough = !options.RequireExactAvailabilityEvidence || snapshot.Confidence == MmsRcbAvailabilityConfidence.Exact;
        var modeAllowsStatic = snapshot.Buffered ? options.AllowStaticBrcb : options.AllowStaticUrcb;
        var modeAllowsDynamic = snapshot.Buffered ? options.AllowDynamicBrcb : options.AllowDynamicUrcb;

        var callerOwned = snapshot.Availability == MmsRcbOperationalAvailability.UsedByCaller;
        var staticCandidate = modeAllowsStatic && exactEnough &&
                              ((callerOwned && options.AllowCallerOwnedReports && snapshot.DataSetDirectorySuccess && snapshot.DataSetMemberCount > 0) ||
                               (serviceAllowsStaticWrite && snapshot.Availability == MmsRcbOperationalAvailability.Available &&
                                snapshot.DataSetDirectorySuccess && snapshot.DataSetMemberCount > 0 && free &&
                                reportEnable != MmsCapabilityEvidenceState.Unknown));

        var dynamicCandidate = modeAllowsDynamic && serviceAllowsDynamicWrite && exactEnough &&
                               snapshot.Availability == MmsRcbOperationalAvailability.NoDataSet &&
                               snapshot.DataSetProbeState == MmsRcbDataSetProbeState.ReadSucceeded &&
                               string.IsNullOrWhiteSpace(snapshot.DataSetReference) &&
                               free &&
                               dataSet != MmsCapabilityEvidenceState.Unknown &&
                               reportEnable != MmsCapabilityEvidenceState.Unknown &&
                               triggerOptions != MmsCapabilityEvidenceState.Unknown;

        var reason = dynamicCandidate
            ? "Verified-empty/free RCB exposes DatSet, RptEna and TrgOps; negotiated MMS services do not prohibit the create/write/delete path."
            : BuildReason(snapshot, serviceAllowsDynamicWrite, free, dataSet, reportEnable, triggerOptions);

        return new MmsReportControlCapability
        {
            Reference = snapshot.Reference,
            Buffered = snapshot.Buffered,
            Availability = snapshot.Availability,
            Confidence = snapshot.Confidence,
            IsCallerOwned = callerOwned,
            IsExplicitlyFree = free,
            DataSetBindingAccess = dataSet,
            ReportEnableAccess = reportEnable,
            TriggerOptionsAccess = triggerOptions,
            OptionalFieldsAccess = optionalFields,
            GeneralInterrogationAccess = generalInterrogation,
            IntegrityAccess = integrity,
            ReservationAccess = reservation,
            OwnerAccess = owner,
            IsStaticWriteCandidate = staticCandidate,
            IsDynamicWriteAttemptCandidate = dynamicCandidate,
            Reason = reason
        };
    }

    private static string BuildReason(
        MmsRcbAvailabilitySnapshot snapshot,
        bool serviceAllowsDynamicWrite,
        bool free,
        MmsCapabilityEvidenceState dataSet,
        MmsCapabilityEvidenceState reportEnable,
        MmsCapabilityEvidenceState triggerOptions)
    {
        var reasons = new List<string>();
        if (snapshot.Availability != MmsRcbOperationalAvailability.NoDataSet)
            reasons.Add($"availability={snapshot.Availability}");
        if (!serviceAllowsDynamicWrite)
            reasons.Add("association service bitmap explicitly blocks create/write/delete dynamic DataSet workflow");
        if (!free)
            reasons.Add("ownership/reservation is not explicitly free");
        if (dataSet == MmsCapabilityEvidenceState.Unknown)
            reasons.Add("DatSet access not exposed");
        if (reportEnable == MmsCapabilityEvidenceState.Unknown)
            reasons.Add("RptEna access not exposed");
        if (triggerOptions == MmsCapabilityEvidenceState.Unknown)
            reasons.Add("TrgOps access not exposed");
        return reasons.Count == 0 ? snapshot.Reason : string.Join("; ", reasons);
    }

    private static MmsCapabilityEvidenceState ServiceEvidence(bool? supported)
        => supported switch
        {
            true => MmsCapabilityEvidenceState.Advertised,
            false => MmsCapabilityEvidenceState.Unsupported,
            _ => MmsCapabilityEvidenceState.Unknown
        };

    private static MmsCapabilityEvidenceState Evidence(
        MmsRcbAvailabilitySnapshot snapshot,
        string attribute,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim() != "-")
            return MmsCapabilityEvidenceState.Exposed;
        if (snapshot.Attributes.Contains(attribute, StringComparer.OrdinalIgnoreCase))
            return MmsCapabilityEvidenceState.Exposed;
        if (snapshot.ProbeDiagnostics.Any(line =>
                line.StartsWith(attribute, StringComparison.OrdinalIgnoreCase) &&
                line.Contains(": OK", StringComparison.OrdinalIgnoreCase)))
            return MmsCapabilityEvidenceState.Exposed;
        return MmsCapabilityEvidenceState.Unknown;
    }

    private static bool IsExplicitlyFree(MmsRcbAvailabilitySnapshot snapshot)
    {
        if (MmsRcbAvailabilityEvaluator.ParseBool(snapshot.EnabledState) != false)
            return false;
        if (MmsRcbAvailabilityEvaluator.HasOwner(snapshot.Owner))
            return false;

        return snapshot.Buffered
            ? MmsRcbAvailabilityEvaluator.ParseUnsigned(snapshot.ReservationTimeSeconds) == 0
            : MmsRcbAvailabilityEvaluator.ParseBool(snapshot.ReservationState) == false;
    }
}
