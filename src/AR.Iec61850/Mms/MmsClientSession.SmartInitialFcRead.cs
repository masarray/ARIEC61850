namespace AR.Iec61850.Mms;

/// <summary>
/// Bounded execution policy for the existing FC-root initial Read plan. This changes
/// only scheduling; target selection stays in the canonical/SCL/live planners.
/// </summary>
public sealed class MmsSmartInitialFcReadOptions
{
    /// <summary>
    /// Maximum number of independent batch Reads that may be outstanding together.
    /// The effective value is additionally capped by negotiated maxOutstandingCalling.
    /// </summary>
    public int MaxOutstandingBatches { get; init; } = 4;

    /// <summary>
    /// Conservative cap when the association limit could not be decoded.
    /// </summary>
    public int UnknownPeerMaxOutstandingBatches { get; init; } = 2;

    /// <summary>
    /// Optional explicit deadline for one confirmed Read batch. Non-positive values
    /// reuse the session connection timeout.
    /// </summary>
    public TimeSpan PerBatchTimeout { get; init; }
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Executes the existing bounded FC-root Read plan with a fixed-size worker pool.
    /// Each batch remains one ordered MMS Confirmed-Read, responses remain invoke-ID
    /// correlated, and final batch publication is deterministic by plan index.
    ///
    /// A timeout or transport fault closes the association exactly once because a
    /// confirmed request may already be on the wire. Other workers then fail soft and
    /// the valid completed batches are preserved in the result.
    /// </summary>
    public async Task<InitialFcReadExecutionResult> ExecuteInitialFcReadPlanSmartAsync(
        InitialFcReadPlan plan,
        MmsSmartInitialFcReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        options ??= new MmsSmartInitialFcReadOptions();

        var effectiveTimeout = options.PerBatchTimeout > TimeSpan.Zero
            ? options.PerBatchTimeout
            : _lastTimeout > TimeSpan.Zero
                ? _lastTimeout
                : TimeSpan.FromSeconds(5);

        var validationError = ValidateSmartInitialFcReadPlan(plan);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return new InitialFcReadExecutionResult
            {
                Status = InitialFcReadExecutionStatus.InvalidPlan,
                Plan = plan,
                PerBatchTimeout = effectiveTimeout,
                Message = validationError
            };
        }

        if (!IsMmsInitiated || !IsTransportConnected)
        {
            return new InitialFcReadExecutionResult
            {
                Status = InitialFcReadExecutionStatus.SessionNotReady,
                Plan = plan,
                PerBatchTimeout = effectiveTimeout,
                Message = $"Smart initial FC-root Read requires an initiated MMS association; current state={State}."
            };
        }

        var orderedBatches = plan.Batches
            .OrderBy(batch => batch.Index)
            .ToArray();
        var executions = new InitialFcReadBatchExecution?[orderedBatches.Length];
        var outcomes = new SmartInitialReadBatchOutcome[orderedBatches.Length];
        var nextIndex = -1;
        var resetStarted = 0;
        var requestedWindow = Math.Clamp(options.MaxOutstandingBatches, 1, 16);
        var unknownWindow = Math.Clamp(options.UnknownPeerMaxOutstandingBatches, 1, 8);
        var window = MmsSmartDiscoveryPolicy.ResolveWindow(
            requestedWindow,
            unknownWindow,
            LastNegotiatedCapabilities.MaxOutstandingCalling);
        var workerCount = Math.Min(window, orderedBatches.Length);
        var workers = new Task[workerCount];

        for (var worker = 0; worker < workerCount; worker++)
            workers[worker] = WorkerAsync();

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ResetAssociationOnceAsync().ConfigureAwait(false);
            throw;
        }

        var materialized = new List<InitialFcReadBatchExecution>(orderedBatches.Length);
        for (var index = 0; index < orderedBatches.Length; index++)
        {
            if (executions[index] is not null)
            {
                materialized.Add(executions[index]!);
                continue;
            }

            var batch = orderedBatches[index];
            var message = "Batch was not started because the MMS association became unavailable.";
            materialized.Add(new InitialFcReadBatchExecution
            {
                BatchIndex = batch.Index,
                Targets = batch.Targets,
                Read = BuildFailedInitialFcBatch(batch.References, message)
            });
            outcomes[index] = SmartInitialReadBatchOutcome.Skipped;
        }

        var failedTargets = materialized.Sum(batch => batch.Read.Results.Count(result => !result.IsSuccess));
        var projectionErrors = materialized.Sum(batch => batch.Projections.Sum(projection => projection.Errors.Count));
        var timedOut = outcomes.Any(outcome => outcome == SmartInitialReadBatchOutcome.TimedOut);
        var transportFailure = outcomes.Any(outcome => outcome == SmartInitialReadBatchOutcome.TransportFailure);
        var skipped = outcomes.Count(outcome => outcome == SmartInitialReadBatchOutcome.Skipped);
        var status = timedOut
            ? InitialFcReadExecutionStatus.TimedOut
            : transportFailure
                ? InitialFcReadExecutionStatus.TransportFailure
                : failedTargets == 0 && projectionErrors == 0 && skipped == 0
                    ? InitialFcReadExecutionStatus.Completed
                    : InitialFcReadExecutionStatus.Partial;

        return new InitialFcReadExecutionResult
        {
            Status = status,
            Plan = plan,
            PerBatchTimeout = effectiveTimeout,
            Batches = materialized,
            Message =
                $"Smart initial FC-root Read: batches={materialized.Count}/{orderedBatches.Length}, " +
                $"window={window}, negotiatedCalling={LastNegotiatedCapabilities.MaxOutstandingCalling?.ToString() ?? "unknown"}, " +
                $"successfulTargets={materialized.Sum(batch => batch.Read.Results.Count(result => result.IsSuccess))}, " +
                $"failedTargets={failedTargets}, projectedLeaves={materialized.Sum(batch => batch.Projections.Sum(projection => projection.Leaves.Count))}, " +
                $"projectionErrors={projectionErrors}, skippedBatches={skipped}."
        };

        async Task WorkerAsync()
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsMmsInitiated || Volatile.Read(ref resetStarted) != 0)
                    return;

                var index = Interlocked.Increment(ref nextIndex);
                if (index >= orderedBatches.Length)
                    return;

                var batch = orderedBatches[index];
                var execution = await ExecuteSmartInitialReadBatchAsync(
                        batch,
                        effectiveTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                executions[index] = execution.Execution;
                outcomes[index] = execution.Outcome;

                if (execution.RequiresAssociationReset)
                {
                    await ResetAssociationOnceAsync().ConfigureAwait(false);
                    return;
                }
            }
        }

        async Task ResetAssociationOnceAsync()
        {
            if (Interlocked.CompareExchange(ref resetStarted, 1, 0) != 0)
                return;

            await MarkProtocolFaultAsync().ConfigureAwait(false);
        }
    }

    private async Task<SmartInitialReadBatchResult> ExecuteSmartInitialReadBatchAsync(
        InitialFcReadBatch batch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var references = batch.References;
        var invokeId = NextInvokeId();
        var request = MmsReadBatchCodec.BuildRequest(
            invokeId,
            references,
            MmsReadPayloadProfile.PresentationDataValues);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        MmsReadBatchResult read;
        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(
                    request,
                    invokeId,
                    deadline.Token)
                .ConfigureAwait(false);
            read = MmsReadBatchCodec.DecodeResponse(response, references, invokeId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            var message = $"Smart initial FC-root Read batch {batch.Index} timed out after {timeout.TotalMilliseconds:0} ms.";
            return new SmartInitialReadBatchResult(
                BuildSmartInitialReadExecution(batch, BuildFailedInitialFcBatch(references, message)),
                SmartInitialReadBatchOutcome.TimedOut,
                RequiresAssociationReset: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var message = $"Smart initial FC-root Read batch {batch.Index} stopped because the receive pump/association closed.";
            return new SmartInitialReadBatchResult(
                BuildSmartInitialReadExecution(batch, BuildFailedInitialFcBatch(references, message)),
                SmartInitialReadBatchOutcome.Skipped,
                RequiresAssociationReset: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            var message = $"Smart initial FC-root Read batch {batch.Index} transport/session failure: {ex.GetType().Name}: {ex.Message}";
            return new SmartInitialReadBatchResult(
                BuildSmartInitialReadExecution(batch, BuildFailedInitialFcBatch(references, message)),
                SmartInitialReadBatchOutcome.TransportFailure,
                RequiresAssociationReset: true);
        }

        return new SmartInitialReadBatchResult(
            BuildSmartInitialReadExecution(batch, read),
            read.IsSuccess
                ? SmartInitialReadBatchOutcome.Completed
                : SmartInitialReadBatchOutcome.Partial,
            RequiresAssociationReset: false);
    }

    private static InitialFcReadBatchExecution BuildSmartInitialReadExecution(
        InitialFcReadBatch batch,
        MmsReadBatchResult read)
    {
        var projections = new List<InitialFcValueProjectionResult>();
        for (var index = 0; index < batch.Targets.Count && index < read.Results.Count; index++)
        {
            var access = read.Results[index];
            var target = batch.Targets[index];
            if (!access.IsSuccess || access.Value is null || target.DataObjects.Count == 0)
                continue;

            projections.Add(InitialFcValueProjector.Project(target, access.Value));
        }

        return new InitialFcReadBatchExecution
        {
            BatchIndex = batch.Index,
            Targets = batch.Targets,
            Read = read,
            Projections = projections
        };
    }

    private static string ValidateSmartInitialFcReadPlan(InitialFcReadPlan plan)
    {
        if (!plan.IsValid)
            return "Initial FC-root Read plan is invalid: " + string.Join(" | ", plan.Errors);
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

    private enum SmartInitialReadBatchOutcome
    {
        None,
        Completed,
        Partial,
        TimedOut,
        TransportFailure,
        Skipped
    }

    private sealed record SmartInitialReadBatchResult(
        InitialFcReadBatchExecution Execution,
        SmartInitialReadBatchOutcome Outcome,
        bool RequiresAssociationReset);
}
