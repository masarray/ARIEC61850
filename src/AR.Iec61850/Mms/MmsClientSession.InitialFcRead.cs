using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public enum InitialFcReadExecutionStatus
{
    InvalidPlan,
    SessionNotReady,
    Completed,
    Partial,
    TransportFailure
}

public sealed class InitialFcReadBatchExecution
{
    public int BatchIndex { get; init; }
    public IReadOnlyList<InitialFcReadTarget> Targets { get; init; } = Array.Empty<InitialFcReadTarget>();
    public MmsReadBatchResult Read { get; init; } = new();
    public IReadOnlyList<InitialFcValueProjectionResult> Projections { get; init; } = Array.Empty<InitialFcValueProjectionResult>();
}

public sealed class InitialFcReadExecutionResult
{
    public InitialFcReadExecutionStatus Status { get; init; }
    public InitialFcReadPlan Plan { get; init; } = new();
    public IReadOnlyList<InitialFcReadBatchExecution> Batches { get; init; } = Array.Empty<InitialFcReadBatchExecution>();
    public string Message { get; init; } = string.Empty;
    public int SuccessfulTargetCount => Batches.Sum(batch => batch.Read.Results.Count(result => result.IsSuccess));
    public int FailedTargetCount => Batches.Sum(batch => batch.Read.Results.Count(result => !result.IsSuccess));
    public int ProjectedLeafCount => Batches.Sum(batch => batch.Projections.Sum(projection => projection.Leaves.Count));
    public bool IsComplete => Status == InitialFcReadExecutionStatus.Completed;
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Executes an already-built initial FC-root Read plan. Batches are sent strictly
    /// sequentially: one confirmed request is registered and completed before the next
    /// batch is sent. This method never performs discovery, GVAA, writes, controls,
    /// report enable, or dynamic DataSet operations.
    /// </summary>
    public async Task<InitialFcReadExecutionResult> ExecuteInitialFcReadPlanAsync(
        InitialFcReadPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var validationError = ValidateInitialFcReadPlan(plan);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return new InitialFcReadExecutionResult
            {
                Status = InitialFcReadExecutionStatus.InvalidPlan,
                Plan = plan,
                Message = validationError
            };
        }

        if (!IsMmsInitiated || !IsTransportConnected)
        {
            return new InitialFcReadExecutionResult
            {
                Status = InitialFcReadExecutionStatus.SessionNotReady,
                Plan = plan,
                Message = $"Initial FC-root Read requires an initiated MMS association; current state={State}."
            };
        }

        var executions = new List<InitialFcReadBatchExecution>(plan.Batches.Count);
        foreach (var batch in plan.Batches.OrderBy(batch => batch.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var references = batch.References;
            var invokeId = NextInvokeId();
            var request = MmsReadBatchCodec.BuildRequest(
                invokeId,
                references,
                MmsReadPayloadProfile.PresentationDataValues);
            LastReadRequestHex = HexDump.ToCompactString(request);

            MmsReadBatchResult read;
            try
            {
                var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
                read = MmsReadBatchCodec.DecodeResponse(response, references, invokeId);
                LastReadResponseHex = read.ResponseHexPreview;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
            {
                var message = $"Initial FC-root Read batch {batch.Index} transport/session failure: {ex.GetType().Name}: {ex.Message}";
                read = new MmsReadBatchResult
                {
                    Results = references.Select(reference => new MmsReadAccessResult
                    {
                        Reference = reference,
                        IsSuccess = false,
                        Message = message
                    }).ToArray(),
                    Message = message,
                    ResponseHexPreview = LastReadResponseHex
                };

                executions.Add(new InitialFcReadBatchExecution
                {
                    BatchIndex = batch.Index,
                    Targets = batch.Targets,
                    Read = read
                });
                await MarkProtocolFaultAsync().ConfigureAwait(false);

                return new InitialFcReadExecutionResult
                {
                    Status = InitialFcReadExecutionStatus.TransportFailure,
                    Plan = plan,
                    Batches = executions,
                    Message = message
                };
            }

            var projections = new List<InitialFcValueProjectionResult>();
            for (var index = 0; index < batch.Targets.Count && index < read.Results.Count; index++)
            {
                var access = read.Results[index];
                var target = batch.Targets[index];
                if (!access.IsSuccess || access.Value is null || target.DataObjects.Count == 0)
                    continue;

                projections.Add(InitialFcValueProjector.Project(target, access.Value));
            }

            executions.Add(new InitialFcReadBatchExecution
            {
                BatchIndex = batch.Index,
                Targets = batch.Targets,
                Read = read,
                Projections = projections
            });
        }

        var failedTargets = executions.Sum(batch => batch.Read.Results.Count(result => !result.IsSuccess));
        var projectionErrors = executions.Sum(batch => batch.Projections.Sum(projection => projection.Errors.Count));
        var status = failedTargets == 0 && projectionErrors == 0
            ? InitialFcReadExecutionStatus.Completed
            : InitialFcReadExecutionStatus.Partial;

        return new InitialFcReadExecutionResult
        {
            Status = status,
            Plan = plan,
            Batches = executions,
            Message = $"Initial FC-root Read execution: batches={executions.Count}/{plan.Batches.Count}, successfulTargets={executions.Sum(batch => batch.Read.Results.Count(result => result.IsSuccess))}, failedTargets={failedTargets}, projectedLeaves={executions.Sum(batch => batch.Projections.Sum(projection => projection.Leaves.Count))}, projectionErrors={projectionErrors}."
        };
    }

    private static string ValidateInitialFcReadPlan(InitialFcReadPlan plan)
    {
        if (!plan.IsValid)
            return "Initial FC-root Read plan is invalid: " + string.Join(" | ", plan.Errors);
        if (plan.MaximumOutstandingReads != 1)
            return $"Initial FC-root Read executor requires MaximumOutstandingReads=1; received {plan.MaximumOutstandingReads}.";
        if (plan.MaximumVariableReferencesPerRead < 1 ||
            plan.MaximumVariableReferencesPerRead > MmsReadBatchCodec.MaximumVariableReferencesPerRead)
        {
            return $"Initial FC-root Read plan exceeds the bounded per-request limit of {MmsReadBatchCodec.MaximumVariableReferencesPerRead}.";
        }

        foreach (var batch in plan.Batches)
        {
            if (batch.Targets.Count == 0)
                return $"Initial FC-root Read batch {batch.Index} is empty.";
            if (batch.Targets.Count > plan.MaximumVariableReferencesPerRead ||
                batch.Targets.Count > MmsReadBatchCodec.MaximumVariableReferencesPerRead)
            {
                return $"Initial FC-root Read batch {batch.Index} contains {batch.Targets.Count} targets and exceeds the bounded request size.";
            }
        }

        return string.Empty;
    }
}
