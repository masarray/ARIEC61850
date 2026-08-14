using AR.Iec61850.Mms;

namespace AR.Iec61850.Engineering;

public static class Iec61850EngineeringProfileBuilder
{
    public static Iec61850EngineeringProfile Build(
        MmsDiscoveryResult discovery,
        IEnumerable<MmsDataSetDirectoryResult>? dataSetDirectories = null,
        Iec61850EngineeringProfileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        options ??= new Iec61850EngineeringProfileOptions();
        var directories = (dataSetDirectories ?? discovery.DataSetDirectories).ToArray();
        var readiness = MmsReportReadinessPlanner.Build(discovery.ReportInventory);
        var diagnostics = BuildDiagnostics(discovery, directories, readiness).ToArray();
        var capabilities = BuildCapabilities(discovery, directories, readiness).ToArray();
        var fcCounts = discovery.IedDirectory.CountByFunctionalConstraint();

        return new Iec61850EngineeringProfile
        {
            Host = options.Host,
            Port = options.Port <= 0 ? 102 : options.Port,
            DiscoverySummary = discovery.Summary,
            LogicalDeviceCount = discovery.IedDirectory.LogicalDeviceCount,
            LogicalNodeCount = discovery.IedDirectory.LogicalNodeCount,
            PointCount = discovery.IedDirectory.PointCount,
            ControlAttributeCount = discovery.IedDirectory.ControlAttributeCount,
            ReportAttributeCount = discovery.IedDirectory.ReportAttributeCount,
            DataSetCount = discovery.ReportInventory.DataSets.Count,
            DataSetDirectorySuccessCount = directories.Count(x => x.IsSuccess),
            DataSetMemberCount = directories.Where(x => x.IsSuccess).Sum(x => x.Members.Count),
            ReportControlCount = discovery.ReportInventory.ReportControls.Count,
            BufferedReportControlCount = discovery.ReportInventory.BufferedCount,
            UnbufferedReportControlCount = discovery.ReportInventory.UnbufferedCount,
            SafeReportCandidateCount = readiness.SafeCandidates.Count,
            FunctionalConstraintCounts = new Dictionary<string, int>(fcCounts, StringComparer.OrdinalIgnoreCase),
            ReportReadiness = readiness,
            Capabilities = capabilities,
            Diagnostics = diagnostics
        };
    }

    private static IEnumerable<Iec61850CapabilityAssessment> BuildCapabilities(
        MmsDiscoveryResult discovery,
        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories,
        MmsReportReadinessPlan readiness)
    {
        yield return new Iec61850CapabilityAssessment
        {
            Area = "MMS model discovery",
            Status = discovery.IedDirectory.PointCount > 0 ? Iec61850CapabilityStatus.Ready : Iec61850CapabilityStatus.Blocked,
            Evidence = $"LD={discovery.IedDirectory.LogicalDeviceCount}, LN={discovery.IedDirectory.LogicalNodeCount}, points={discovery.IedDirectory.PointCount}.",
            NextAction = discovery.IedDirectory.PointCount > 0 ? "Use this directory as the canonical live model snapshot." : "Verify association, domain discovery, and GetNameList response handling."
        };

        var dataSetStatus = discovery.ReportInventory.DataSets.Count > 0
            ? dataSetDirectories.Any(x => x.IsSuccess)
                ? Iec61850CapabilityStatus.Ready
                : Iec61850CapabilityStatus.Partial
            : Iec61850CapabilityStatus.Blocked;
        yield return new Iec61850CapabilityAssessment
        {
            Area = "DataSet service",
            Status = dataSetStatus,
            Evidence = $"Discovered={discovery.ReportInventory.DataSets.Count}, directoryReadOk={dataSetDirectories.Count(x => x.IsSuccess)}, members={dataSetDirectories.Where(x => x.IsSuccess).Sum(x => x.Members.Count)}.",
            NextAction = dataSetStatus == Iec61850CapabilityStatus.Ready ? "Bind report values by DataSet member index." : "Read GetNamedVariableListAttributes for candidate DataSets before report runtime tests."
        };

        var reportStatus = readiness.SafeCandidates.Count > 0
            ? Iec61850CapabilityStatus.Ready
            : discovery.ReportInventory.ReportControls.Count > 0
                ? Iec61850CapabilityStatus.Partial
                : Iec61850CapabilityStatus.Blocked;
        yield return new Iec61850CapabilityAssessment
        {
            Area = "Report service",
            Status = reportStatus,
            Evidence = $"RCB={discovery.ReportInventory.ReportControls.Count}, safeCandidates={readiness.SafeCandidates.Count}, BRCB={readiness.BufferedSafeCandidateCount}, URCB={readiness.UnbufferedSafeCandidateCount}.",
            NextAction = reportStatus == Iec61850CapabilityStatus.Ready ? "Create a guarded report session profile and run GI/report receive tests." : "Probe RCB DatSet, RptEna, Resv/ResvTms, RptID, ConfRev, OptFlds, and TrgOps."
        };

        yield return new Iec61850CapabilityAssessment
        {
            Area = "Control service safety gate",
            Status = discovery.IedDirectory.ControlAttributeCount > 0 ? Iec61850CapabilityStatus.Partial : Iec61850CapabilityStatus.NotAssessed,
            Evidence = $"controlAttributes={discovery.IedDirectory.ControlAttributeCount}.",
            NextAction = discovery.IedDirectory.ControlAttributeCount > 0 ? "Keep write/control disabled by default; add dry-run capability discovery before Oper/SBO/SBOw." : "Add control capability discovery when CO attributes are present."
        };
    }

    private static IEnumerable<Iec61850DiagnosticMessage> BuildDiagnostics(
        MmsDiscoveryResult discovery,
        IReadOnlyList<MmsDataSetDirectoryResult> dataSetDirectories,
        MmsReportReadinessPlan readiness)
    {
        if (discovery.IedDirectory.PointCount == 0)
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Error,
                "MODEL_EMPTY",
                string.Empty,
                "No live FC point was resolved from the discovery snapshot.",
                "Verify MMS association, domain discovery, variable naming, and FC resolver rules before adding UI features.");
        }

        if (discovery.ReportInventory.DataSets.Count == 0)
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Warning,
                "DATASET_NONE_DISCOVERED",
                string.Empty,
                "No DataSet candidate was discovered online.",
                "Keep report runtime blocked until DataSet discovery or dynamic DataSet creation is proven.");
        }
        else if (dataSetDirectories.Count > 0 && dataSetDirectories.All(x => !x.IsSuccess))
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Warning,
                "DATASET_DIRECTORY_READ_FAILED",
                string.Empty,
                "DataSet names were discovered, but no DataSet directory read succeeded.",
                "Fix GetNamedVariableListAttributes decoding/transport before mapping reports by member index.");
        }

        if (discovery.ReportInventory.ReportControls.Count == 0)
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Warning,
                "RCB_NONE_DISCOVERED",
                string.Empty,
                "No report control block candidate was discovered online.",
                "Verify report-control naming, BR/RP functional constraints, and model discovery filters.");
        }
        else if (readiness.SafeCandidates.Count == 0)
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Warning,
                "RCB_NO_SAFE_CANDIDATE",
                string.Empty,
                "Report control blocks exist, but none is classified as safe for automatic subscription.",
                "Probe RptEna, DatSet, reservation, ConfRev, OptFlds, and TrgOps before enabling reports.");
        }
        else
        {
            foreach (var candidate in readiness.SafeCandidates.Take(5))
            {
                yield return Diagnostic(
                    Iec61850DiagnosticSeverity.Info,
                    "RCB_SAFE_CANDIDATE",
                    candidate.ReportControl.Reference,
                    $"Safe report candidate found for DataSet {candidate.ReportControl.DataSetReference}.",
                    "Use a guarded session: start receiver first, enable RptEna, trigger GI, then release on stop.");
            }
        }

        var fcCounts = discovery.IedDirectory.CountByFunctionalConstraint();
        if (!fcCounts.ContainsKey("ST") && !fcCounts.ContainsKey("MX"))
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Advisory,
                "NO_ST_OR_MX_POINTS",
                string.Empty,
                "The model snapshot does not include ST or MX monitoring points.",
                "Review FC inference and discovery filters before creating monitoring/report test plans.");
        }

        if (discovery.IedDirectory.ControlAttributeCount > 0)
        {
            yield return Diagnostic(
                Iec61850DiagnosticSeverity.Advisory,
                "CONTROL_ATTRIBUTES_PRESENT",
                string.Empty,
                $"{discovery.IedDirectory.ControlAttributeCount} control-related attribute(s) were detected.",
                "Keep control operations read-only until Direct/SBO/SBOw capability discovery and dry-run evidence are implemented.");
        }
    }

    private static Iec61850DiagnosticMessage Diagnostic(
        Iec61850DiagnosticSeverity severity,
        string code,
        string reference,
        string message,
        string recommendation)
        => new()
        {
            Severity = severity,
            Code = code,
            Reference = reference,
            Message = message,
            Recommendation = recommendation
        };
}
