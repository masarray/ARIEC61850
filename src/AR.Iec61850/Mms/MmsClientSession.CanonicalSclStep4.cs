using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Mms;

public sealed class CanonicalSclStep4Design
{
    public CanonicalIedModel Model { get; init; } = new();
    public SclMmsDomainInventory DomainInventory { get; init; } = new();
    public InitialFcReadPlan ReadPlan { get; init; } = new();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool IsValid => Errors.Count == 0 && DomainInventory.IsSuccess && ReadPlan.IsValid;
}

/// <summary>
/// Builds Step-4 design evidence exclusively from CanonicalIedModel. This is the active
/// P0 planning boundary for SCL-assisted initial FC reads; callers no longer need to
/// construct a second LiveIedModelDiscoveryDocument just to plan Step 4.
/// </summary>
public static class CanonicalSclStep4Planner
{
    public static CanonicalSclStep4Design Build(
        CanonicalIedModel model,
        IEnumerable<string>? allowedDomains = null,
        int maximumVariableReferencesPerRead = MmsReadBatchCodec.MaximumVariableReferencesPerRead)
    {
        ArgumentNullException.ThrowIfNull(model);

        var compacted = CanonicalModelMemoryCompactor.Compact(model);
        var errors = new List<string>();
        var warnings = new List<string>();

        if (compacted.Source.Ingress != CanonicalIngressKind.SclFile)
            errors.Add($"SCL-assisted Step-4 requires canonical SCL ingress; received {compacted.Source.Ingress}.");

        var iedName = compacted.Identity.Name.Trim();
        if (iedName.Length == 0)
            errors.Add("Canonical SCL model does not contain a selected IED identity.");

        var accessPointName = compacted.Communication.AccessPointName.IsKnown
            ? compacted.Communication.AccessPointName.Value?.Trim() ?? string.Empty
            : string.Empty;
        if (accessPointName.Length == 0)
            errors.Add("Canonical SCL model does not contain a selected AccessPoint identity.");

        var domains = compacted.LogicalDevices
            .Select(device => compacted.Strings.Resolve(device.MmsDomain).Trim())
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(domain => domain, StringComparer.Ordinal)
            .ToArray();

        if (domains.Length == 0)
            errors.Add("Canonical SCL model contains no resolvable MMS logical-device domains.");

        if (compacted.Diagnostics.Length > 0)
        {
            warnings.AddRange(compacted.Diagnostics.Select(diagnostic =>
                string.IsNullOrWhiteSpace(diagnostic.Reference)
                    ? $"{diagnostic.Code}: {diagnostic.Message}"
                    : $"{diagnostic.Code} {diagnostic.Reference}: {diagnostic.Message}"));
        }

        var inventory = new SclMmsDomainInventory
        {
            IedName = iedName,
            AccessPointName = accessPointName,
            ExpectedDomains = domains,
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };

        var effectiveDomains = allowedDomains ?? domains;
        var readPlan = errors.Count == 0
            ? CanonicalInitialFcReadPlanner.FromCanonicalModel(
                compacted,
                effectiveDomains,
                maximumVariableReferencesPerRead)
            : new InitialFcReadPlan
            {
                MaximumVariableReferencesPerRead = maximumVariableReferencesPerRead,
                MaximumOutstandingReads = 1,
                Errors = errors.ToArray(),
                Warnings = warnings.ToArray()
            };

        return new CanonicalSclStep4Design
        {
            Model = compacted,
            DomainInventory = inventory,
            ReadPlan = readPlan,
            Errors = errors.Concat(readPlan.Errors).Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = warnings.Concat(readPlan.Warnings).Distinct(StringComparer.Ordinal).ToArray()
        };
    }
}

public enum CanonicalSclStep4ExecutionStatus
{
    InvalidCanonicalModel,
    InvalidAssociationPlan,
    OnlineValidationFailed,
    DomainMismatch,
    InitialReadInvalid,
    TimedOut,
    TransportFailure,
    Partial,
    Completed
}

public sealed class CanonicalSclStep4ExecutionResult
{
    public CanonicalSclStep4ExecutionStatus Status { get; init; }
    public CanonicalSclStep4Design Design { get; init; } = new();
    public SclAssistedMmsOnlineResult? Online { get; init; }
    public InitialFcReadExecutionResult? InitialRead { get; init; }
    public bool SessionRemainsOpen { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsComplete => Status == CanonicalSclStep4ExecutionStatus.Completed;
}

public sealed partial class MmsClientSession
{
    public Task<CanonicalSclStep4ExecutionResult> ConnectAndExecuteCanonicalSclStep4Async(
        SclAssistedMmsAssociationPlan associationPlan,
        CanonicalIedModel canonicalModel,
        CancellationToken cancellationToken = default)
        => ConnectAndExecuteCanonicalSclStep4Async(
            associationPlan,
            canonicalModel,
            TimeSpan.FromSeconds(5),
            cancellationToken);

    /// <summary>
    /// Active SCL-assisted Step-4 orchestration. The canonical model is the only semantic
    /// input: association/domain validation is performed first, then the existing bounded
    /// sequential FC-root executor consumes a plan rebuilt from exact matched online
    /// domains. No legacy SCL-to-live-model Step-4 projection is used by this path.
    /// </summary>
    public async Task<CanonicalSclStep4ExecutionResult> ConnectAndExecuteCanonicalSclStep4Async(
        SclAssistedMmsAssociationPlan associationPlan,
        CanonicalIedModel canonicalModel,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(associationPlan);
        ArgumentNullException.ThrowIfNull(canonicalModel);

        var design = CanonicalSclStep4Planner.Build(canonicalModel);
        if (!design.IsValid)
        {
            return BuildCanonicalStep4Result(
                CanonicalSclStep4ExecutionStatus.InvalidCanonicalModel,
                design,
                null,
                null,
                "Canonical SCL Step-4 design is invalid: " + string.Join(" | ", design.Errors));
        }

        if (!string.Equals(associationPlan.IedName, design.DomainInventory.IedName, StringComparison.Ordinal) ||
            !string.Equals(associationPlan.AccessPointName, design.DomainInventory.AccessPointName, StringComparison.Ordinal))
        {
            return BuildCanonicalStep4Result(
                CanonicalSclStep4ExecutionStatus.InvalidAssociationPlan,
                design,
                null,
                null,
                $"Association identity '{associationPlan.IedName}/{associationPlan.AccessPointName}' does not match canonical SCL identity '{design.DomainInventory.IedName}/{design.DomainInventory.AccessPointName}'.");
        }

        var online = await ConnectSclAssistedAsync(
                associationPlan,
                design.DomainInventory,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

        if (online.Status == SclAssistedMmsOnlineStatus.TimedOut)
        {
            return BuildCanonicalStep4Result(
                CanonicalSclStep4ExecutionStatus.TimedOut,
                design,
                online,
                null,
                online.Message);
        }

        if (online.Status == SclAssistedMmsOnlineStatus.DomainMismatch)
        {
            return BuildCanonicalStep4Result(
                CanonicalSclStep4ExecutionStatus.DomainMismatch,
                design,
                online,
                null,
                online.Message);
        }

        if (!online.IsCompatible || online.Domains is null)
        {
            return BuildCanonicalStep4Result(
                CanonicalSclStep4ExecutionStatus.OnlineValidationFailed,
                design,
                online,
                null,
                online.Message);
        }

        // Rebuild from exact live-matched identifiers. This prevents stale or merely
        // similar SCL domain names from leaking into the actual Step-4 Read requests.
        var onlineDesign = CanonicalSclStep4Planner.Build(
            design.Model,
            online.Domains.MatchedDomains,
            design.ReadPlan.MaximumVariableReferencesPerRead);

        if (!onlineDesign.IsValid)
        {
            return BuildCanonicalStep4Result(
                CanonicalSclStep4ExecutionStatus.InitialReadInvalid,
                onlineDesign,
                online,
                null,
                "Canonical Step-4 plan became invalid after exact online-domain reconciliation: " +
                string.Join(" | ", onlineDesign.Errors));
        }

        var initialRead = await ExecuteInitialFcReadPlanAsync(
                onlineDesign.ReadPlan,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

        var status = initialRead.Status switch
        {
            InitialFcReadExecutionStatus.Completed => CanonicalSclStep4ExecutionStatus.Completed,
            InitialFcReadExecutionStatus.Partial => CanonicalSclStep4ExecutionStatus.Partial,
            InitialFcReadExecutionStatus.TimedOut => CanonicalSclStep4ExecutionStatus.TimedOut,
            InitialFcReadExecutionStatus.TransportFailure => CanonicalSclStep4ExecutionStatus.TransportFailure,
            _ => CanonicalSclStep4ExecutionStatus.InitialReadInvalid
        };

        return BuildCanonicalStep4Result(
            status,
            onlineDesign,
            online,
            initialRead,
            $"Canonical SCL Step-4: online={online.Status}, initialRead={initialRead.Status}. {initialRead.Message}");
    }

    private CanonicalSclStep4ExecutionResult BuildCanonicalStep4Result(
        CanonicalSclStep4ExecutionStatus status,
        CanonicalSclStep4Design design,
        SclAssistedMmsOnlineResult? online,
        InitialFcReadExecutionResult? initialRead,
        string message)
        => new()
        {
            Status = status,
            Design = design,
            Online = online,
            InitialRead = initialRead,
            SessionRemainsOpen = IsMmsInitiated && IsTransportConnected,
            Message = message
        };
}
