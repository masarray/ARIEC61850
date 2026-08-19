using AR.Iec61850.Mms;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AR.Iec61850.Control;

public sealed class Iec61850ControlObjectSession : IAsyncDisposable
{
    private sealed class AssociationControlState
    {
        public ConcurrentDictionary<string, SemaphoreSlim> ObjectLocks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, int> ControlNumbers { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly ConditionalWeakTable<object, AssociationControlState> AssociationStates = new();

    private readonly IIec61850ControlTransport _transport;
    private readonly Iec61850ControlServiceOptions _options;
    private readonly AssociationControlState _associationState;
    private readonly SemaphoreSlim _objectLock;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly string _lockKey;
    private Iec61850ControlSequenceContext? _activeSequence;
    private Iec61850ControlSequenceContext? _expiredSequence;
    private CancellationTokenSource? _selectionLeaseCts;
    private bool _ownsLock;
    private bool _disposed;

    internal Iec61850ControlObjectSession(
        IIec61850ControlTransport transport,
        Iec61850ControlObjectDescriptor descriptor,
        Iec61850ControlServiceOptions options)
    {
        _transport = transport;
        Descriptor = descriptor;
        _options = options;
        _associationState = AssociationStates.GetValue(transport.AssociationIdentity, _ => new AssociationControlState());
        _lockKey = descriptor.ObjectReference;
        _objectLock = _associationState.ObjectLocks.GetOrAdd(_lockKey, _ => new SemaphoreSlim(1, 1));
    }

    public Iec61850ControlObjectDescriptor Descriptor { get; }
    public bool IsSelected => _activeSequence != null && Descriptor.RequiresSelect;

    public async Task<Iec61850ControlStatusResult> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(Descriptor.StatusReference))
            {
                return new Iec61850ControlStatusResult
                {
                    IsSuccess = false,
                    State = Iec61850ControlStatusState.Unknown,
                    DisplayValue = "Unknown",
                    Message = "The live IED model did not expose a readable status value for this control object."
                };
            }

            var functionalConstraint = string.IsNullOrWhiteSpace(Descriptor.StatusFunctionalConstraint)
                ? InferStatusFunctionalConstraint(Descriptor.StatusReference)
                : Descriptor.StatusFunctionalConstraint;
            var reference = MmsObjectReference.Parse(Descriptor.StatusReference, functionalConstraint);
            var read = await _transport.ReadAsync(reference, cancellationToken).ConfigureAwait(false);
            return Iec61850ControlStatusInterpreter.Interpret(Descriptor.StatusReference, Descriptor.Cdc, read);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new Iec61850ControlStatusResult
            {
                IsSuccess = false,
                Reference = Descriptor.StatusReference,
                State = Iec61850ControlStatusState.Unknown,
                DisplayValue = "Cancelled",
                Message = "Status read cancelled."
            };
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<Iec61850ControlActionResult> SelectAsync(
        Iec61850ControlRequest request,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SelectCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<Iec61850ControlActionResult> SelectCoreAsync(
        Iec61850ControlRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (Descriptor.ControlModel != Iec61850ControlModel.SelectBeforeOperateNormal)
            return Unsupported(Iec61850ControlAction.Select, "Select read is valid only for SBO normal security.");

        _expiredSequence = null;
        await AcquireObjectAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var context = CreateContext(request);
            var read = await _transport.ReadAsync(Descriptor.References.Sbo, cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess || !IsPositiveSboSelection(read.Value, Descriptor.References))
            {
                ReleaseObject();
                var message = !read.IsSuccess
                    ? read.Message
                    : "SBO read did not return a positive selected-object reference.";
                return Rejected(Iec61850ControlAction.Select, message, stopwatch.Elapsed);
            }

            _activeSequence = context;
            StartSelectionLease(context);
            return Accepted(Iec61850ControlAction.Select, context, stopwatch.Elapsed,
                "SBO select accepted; object ownership retained until Operate, Cancel, timeout, association loss, or disposal.");
        }
        catch
        {
            ReleaseObject();
            throw;
        }
    }

    public async Task<Iec61850ControlActionResult> SelectWithValueAsync(
        Iec61850ControlRequest request,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SelectWithValueCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<Iec61850ControlActionResult> SelectWithValueCoreAsync(
        Iec61850ControlRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (Descriptor.ControlModel != Iec61850ControlModel.SelectBeforeOperateEnhanced || Descriptor.SelectWithValueSpecification == null)
            return Unsupported(Iec61850ControlAction.SelectWithValue, "SelectWithValue is valid only for SBO enhanced security.");

        _expiredSequence = null;
        await AcquireObjectAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var context = CreateContext(request);
            await using var subscription = _transport.SubscribeInformationReports(out var reports, 16);
            var value = Iec61850ControlStructureBuilder.BuildSelectWithValue(
                context,
                Descriptor.SelectWithValueSpecification,
                _options.RequireExactNamedControlFields);
            var write = await _transport.WriteControlAsync(Descriptor.References.SboWithValue, value, cancellationToken).ConfigureAwait(false);
            if (!write.IsSuccess)
            {
                var appError = await WaitForApplicationErrorAsync(_options.ApplicationErrorGracePeriod, cancellationToken, reports).ConfigureAwait(false);
                ReleaseObject();
                return FromWriteFailure(Iec61850ControlAction.SelectWithValue, write, appError, context, stopwatch.Elapsed);
            }

            var postAcceptanceError = await WaitForApplicationErrorAsync(
                _options.ApplicationErrorGracePeriod,
                cancellationToken,
                reports).ConfigureAwait(false);
            if (postAcceptanceError is { Positive: false })
            {
                ReleaseObject();
                return FromApplicationRejection(
                    Iec61850ControlAction.SelectWithValue,
                    postAcceptanceError,
                    context,
                    stopwatch.Elapsed,
                    "SBOw received an asynchronous LastApplError after MMS service acceptance.");
            }

            _activeSequence = context;
            StartSelectionLease(context);
            return Accepted(Iec61850ControlAction.SelectWithValue, context, stopwatch.Elapsed,
                "SBOw accepted; exact ctlVal/origin/ctlNum/T/Test/Check values are retained for Operate.");
        }
        catch
        {
            ReleaseObject();
            throw;
        }
    }

    public async Task<Iec61850ControlActionResult> OperateAsync(
        Iec61850ControlRequest request,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await OperateCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<Iec61850ControlActionResult> OperateCoreAsync(
        Iec61850ControlRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (!Descriptor.IsOperationallyReady)
            return Unsupported(Iec61850ControlAction.Operate, "Control descriptor is not operationally ready.");
        if (request.OperateAtUtc.HasValue && !Descriptor.SupportsTimeActivatedOperate)
            return Unsupported(Iec61850ControlAction.Operate, "The live Oper type does not expose operTm; time-activated operation is unsupported.");

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<Iec61850ControlWireStep> precedingWireSteps = Array.Empty<Iec61850ControlWireStep>();
        if (Descriptor.RequiresSelect && _activeSequence == null)
        {
            var expiredContext = _expiredSequence;
            _expiredSequence = null;
            if (expiredContext != null && SequenceMatches(request, expiredContext))
                return SelectionTimedOut(expiredContext, stopwatch.Elapsed);

            if (!request.AutoSelect)
                return Rejected(Iec61850ControlAction.Operate, "Object is not selected and AutoSelect=false.", stopwatch.Elapsed);

            var select = Descriptor.ControlModel == Iec61850ControlModel.SelectBeforeOperateEnhanced
                ? await SelectWithValueCoreAsync(request, cancellationToken).ConfigureAwait(false)
                : await SelectCoreAsync(request, cancellationToken).ConfigureAwait(false);
            if (!select.IsSuccess)
                return select;
            precedingWireSteps = select.WireSteps;
        }
        else if (!Descriptor.RequiresSelect)
        {
            await AcquireObjectAsync(cancellationToken).ConfigureAwait(false);
        }

        var operateRequestAccepted = false;
        try
        {
            var context = _activeSequence ?? CreateContext(request);
            if (_activeSequence != null)
            {
                StopSelectionLease();
                if (SelectionExpired(context))
                {
                    await BestEffortCancelAsync(CancellationToken.None).ConfigureAwait(false);
                    return SelectionTimedOut(context, stopwatch.Elapsed);
                }

                if (!SequenceMatches(request, context))
                {
                    await BestEffortCancelAsync(CancellationToken.None).ConfigureAwait(false);
                    return Rejected(Iec61850ControlAction.Operate,
                        "Operate request differs from the selected ctlVal/origin/ctlNum/T/Test/Check sequence. The stale selection was cancelled; re-select with one immutable sequence.",
                        stopwatch.Elapsed);
                }
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = EffectiveOperateTimeout(request);
            var deadline = DateTimeOffset.UtcNow + timeout;
            linked.CancelAfter(timeout);
            await using var subscription = _transport.SubscribeInformationReports(out var reports, 64);

            var operValue = Iec61850ControlStructureBuilder.BuildOperate(
                context,
                Descriptor.OperSpecification,
                _options.RequireExactNamedControlFields);
            var write = await _transport.WriteControlAsync(Descriptor.References.Oper, operValue, linked.Token).ConfigureAwait(false);
            if (!write.IsSuccess)
            {
                var applicationError = await WaitForApplicationErrorAsync(_options.ApplicationErrorGracePeriod, cancellationToken, reports).ConfigureAwait(false);
                return FromWriteFailure(
                    Iec61850ControlAction.Operate,
                    write,
                    applicationError,
                    context,
                    stopwatch.Elapsed,
                    precedingWireSteps);
            }

            operateRequestAccepted = true;
            if (!Descriptor.IsEnhanced)
                return Accepted(
                    Iec61850ControlAction.Operate,
                    context,
                    stopwatch.Elapsed,
                    "Operate service accepted (normal-security completion boundary).",
                    precedingWireSteps);

            var remaining = deadline - DateTimeOffset.UtcNow;
            var termination = remaining > TimeSpan.Zero
                ? await WaitForTerminationAsync(reports, remaining, cancellationToken).ConfigureAwait(false)
                : null;
            if (termination == null)
            {
                return new Iec61850ControlActionResult
                {
                    Action = Iec61850ControlAction.Operate,
                    CompletionState = _transport.IsAssociated ? Iec61850ControlCompletionState.TimedOut : Iec61850ControlCompletionState.AssociationLost,
                    RequestAccepted = true,
                    ClientError = _transport.IsAssociated
                        ? $"Operate was accepted, but no CommandTermination arrived within {timeout}."
                        : "MMS association was lost while waiting for CommandTermination.",
                    RequestHex = _transport.LastRequestHex,
                    ResponseHex = _transport.LastResponseHex,
                    ControlNumber = context.ControlNumber,
                    SequenceTimestamp = context.TimestampUtc,
                    Elapsed = stopwatch.Elapsed,
                    WireSteps = AppendWireSteps(
                        precedingWireSteps,
                        BuildWireStep(Iec61850ControlAction.Operate, requestAccepted: true, "Operate accepted; CommandTermination timed out."))
                };
            }

            return new Iec61850ControlActionResult
            {
                Action = Iec61850ControlAction.Operate,
                CompletionState = termination.Positive
                    ? Iec61850ControlCompletionState.PositiveTermination
                    : Iec61850ControlCompletionState.NegativeTermination,
                RequestAccepted = true,
                CommandTerminationReceived = true,
                PositiveTermination = termination.Positive,
                ControlError = termination.ControlError,
                AddCause = termination.AddCause,
                LastApplErrorText = termination.LastApplErrorText,
                RequestHex = _transport.LastRequestHex,
                ResponseHex = termination.ResponseHex,
                ControlNumber = context.ControlNumber,
                SequenceTimestamp = context.TimestampUtc,
                Elapsed = stopwatch.Elapsed,
                WireSteps = AppendWireSteps(
                    precedingWireSteps,
                    BuildWireStep(
                        Iec61850ControlAction.Operate,
                        requestAccepted: true,
                        termination.Positive ? "Operate accepted; positive CommandTermination received." : "Operate accepted; negative CommandTermination received."))
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (Descriptor.RequiresSelect && _activeSequence != null && !operateRequestAccepted)
                await BestEffortCancelAsync(CancellationToken.None).ConfigureAwait(false);

            return new Iec61850ControlActionResult
            {
                Action = Iec61850ControlAction.Operate,
                CompletionState = Iec61850ControlCompletionState.Cancelled,
                RequestAccepted = operateRequestAccepted,
                ClientError = operateRequestAccepted
                    ? "Control operation was accepted, but the client stopped waiting for completion."
                    : "Control operation cancelled by caller.",
                Elapsed = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            if (Descriptor.RequiresSelect && _activeSequence != null && !operateRequestAccepted)
                await BestEffortCancelAsync(CancellationToken.None).ConfigureAwait(false);

            return new Iec61850ControlActionResult
            {
                Action = Iec61850ControlAction.Operate,
                CompletionState = _transport.IsAssociated
                    ? Iec61850ControlCompletionState.TimedOut
                    : Iec61850ControlCompletionState.AssociationLost,
                RequestAccepted = operateRequestAccepted,
                ClientError = _transport.IsAssociated
                    ? "Control service timed out before the MMS write/response sequence completed."
                    : "MMS association was lost during the control sequence.",
                RequestHex = _transport.LastRequestHex,
                ResponseHex = _transport.LastResponseHex,
                Elapsed = stopwatch.Elapsed
            };
        }
        finally
        {
            _activeSequence = null;
            ReleaseObject();
        }
    }

    public async Task<Iec61850ControlActionResult> CancelAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CancelCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<Iec61850ControlActionResult> CancelCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var stopwatch = Stopwatch.StartNew();
        if (_activeSequence == null)
            return Rejected(Iec61850ControlAction.Cancel, "No active selection exists for this control session.", stopwatch.Elapsed);

        var context = _activeSequence;
        StopSelectionLease();
        try
        {
            if (Descriptor.CancelSpecification == null)
                return Unsupported(Iec61850ControlAction.Cancel, "IED did not expose a decodable Cancel type specification.");

            var value = Iec61850ControlStructureBuilder.BuildCancel(context, Descriptor.CancelSpecification, _options.RequireExactNamedControlFields);
            var write = await _transport.WriteControlAsync(Descriptor.References.Cancel, value, cancellationToken).ConfigureAwait(false);
            return write.IsSuccess
                ? Accepted(Iec61850ControlAction.Cancel, context, stopwatch.Elapsed, "Cancel accepted; local SBO ownership released.")
                : FromWriteFailure(Iec61850ControlAction.Cancel, write, null, context, stopwatch.Elapsed);
        }
        finally
        {
            _activeSequence = null;
            ReleaseObject();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            StopSelectionLease();
            if (_activeSequence != null && _transport.IsAssociated)
                await BestEffortCancelAsync(CancellationToken.None).ConfigureAwait(false);
            _activeSequence = null;
            _expiredSequence = null;
            ReleaseObject();
        }
        finally
        {
            _operationGate.Release();
        }
    }


    private void StartSelectionLease(Iec61850ControlSequenceContext context)
    {
        StopSelectionLease();
        var timeout = Descriptor.SboTimeout ?? _options.DefaultSboTimeout;
        if (timeout <= TimeSpan.Zero)
            timeout = _options.DefaultSboTimeout;

        var cts = new CancellationTokenSource();
        _selectionLeaseCts = cts;
        _ = ExpireSelectionLeaseAsync(context, timeout, cts.Token);
    }

    private void StopSelectionLease()
    {
        var cts = _selectionLeaseCts;
        _selectionLeaseCts = null;
        if (cts == null)
            return;

        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task ExpireSelectionLeaseAsync(
        Iec61850ControlSequenceContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(timeout, cancellationToken).ConfigureAwait(false);
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(_activeSequence, context))
                    return;

                // Preserve the expired sequence long enough for the next matching
                // Operate call to report the real outcome instead of silently
                // auto-selecting again and misclassifying the result as Rejected.
                _expiredSequence = context;

                // The server should expire the SBO lease itself. Send Cancel as a
                // best-effort cleanup and always release local ownership so a stale
                // UI/session cannot block later commands.
                await BestEffortCancelAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal path when Operate, Cancel, disposal, or a replacement select
            // consumes the active lease before the timeout.
        }
        catch
        {
            // Lease cleanup is intentionally fail-safe. BestEffortCancelAsync and
            // ReleaseObject prevent a background cleanup fault from leaking ownership.
            _activeSequence = null;
            ReleaseObject();
        }
    }



    private static string InferStatusFunctionalConstraint(string reference)
        => reference.Contains(".mag.", StringComparison.OrdinalIgnoreCase) ? "MX" : "ST";

    private static bool IsPositiveSboSelection(
        MmsDataValue? value,
        Iec61850ControlObjectReferences references)
    {
        if (value == null)
            return false;

        return value.Kind switch
        {
            MmsDataKind.VisibleString or MmsDataKind.MmsString => IsMatchingSboReference(
                Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture),
                references),
            MmsDataKind.Boolean => value.Value is true,
            _ => false
        };
    }

    private static bool IsMatchingSboReference(string? selectedReference, Iec61850ControlObjectReferences references)
    {
        if (string.IsNullOrWhiteSpace(selectedReference))
            return false;

        var normalized = selectedReference.Trim().Replace('$', '.').Replace(".CO.", ".", StringComparison.OrdinalIgnoreCase);
        var objectReference = references.ObjectReference.Replace('$', '.');
        var relativeReference = $"{references.LogicalNode}.{references.DataObjectPath}";

        if (normalized.Equals(objectReference, StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(relativeReference, StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith($"/{relativeReference}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Several IEDs return a short selection token rather than the full object
        // reference.  Accept opaque tokens only when they do not look like a
        // different IEC 61850 object reference.
        return !normalized.Contains('/') && !normalized.Contains('.') && !normalized.Contains('$');
    }

    private Iec61850ControlSequenceContext CreateContext(Iec61850ControlRequest request)
    {
        var next = (byte)(_associationState.ControlNumbers.AddOrUpdate(_lockKey, 1, (_, current) => current >= 255 ? 1 : current + 1));
        return Iec61850ControlStructureBuilder.CreateContext(request, Descriptor.CtlValSpecification, next, DateTimeOffset.UtcNow);
    }

    private bool SelectionExpired(Iec61850ControlSequenceContext context)
        => DateTimeOffset.UtcNow - context.CreatedUtc > (Descriptor.SboTimeout ?? _options.DefaultSboTimeout);

    private TimeSpan EffectiveOperateTimeout(Iec61850ControlRequest request)
    {
        var timeout = request.CommandTerminationTimeout ?? Descriptor.OperTimeout ?? _options.DefaultOperateTimeout;
        if (!request.OperateAtUtc.HasValue)
            return timeout;

        var activationDelay = request.OperateAtUtc.Value.ToUniversalTime() - DateTimeOffset.UtcNow;
        return activationDelay > TimeSpan.Zero ? activationDelay + timeout : timeout;
    }

    private static bool SequenceMatches(Iec61850ControlRequest request, Iec61850ControlSequenceContext context)
    {
        if (request.ControlNumber.HasValue && request.ControlNumber.Value != context.ControlNumber)
            return false;
        return request.ControlValue.Fingerprint == context.Request.ControlValue.Fingerprint &&
               request.Origin.Fingerprint == context.Request.Origin.Fingerprint &&
               request.Test == context.Request.Test &&
               request.InterlockCheck == context.Request.InterlockCheck &&
               request.SynchroCheck == context.Request.SynchroCheck &&
               request.OperateAtUtc?.ToUniversalTime() == context.Request.OperateAtUtc?.ToUniversalTime();
    }

    private async Task AcquireObjectAsync(CancellationToken cancellationToken)
    {
        if (_ownsLock)
            return;
        await _objectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        _ownsLock = true;
    }

    private void ReleaseObject()
    {
        if (!_ownsLock)
            return;
        _ownsLock = false;
        _objectLock.Release();
    }

    private async Task<Iec61850CommandTermination?> WaitForTerminationAsync(
        ChannelReader<MmsPduEnvelope> reports,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            await foreach (var envelope in reports.ReadAllAsync(linked.Token).ConfigureAwait(false))
            {
                var termination = Iec61850CommandTerminationDecoder.Decode(envelope, Descriptor.References);
                if (termination.IsForControlObject && termination.IsTermination)
                    return termination;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
        catch (IOException) when (!_transport.IsAssociated)
        {
            return null;
        }
        return null;
    }

    private async Task<Iec61850CommandTermination?> WaitForApplicationErrorAsync(
        TimeSpan gracePeriod,
        CancellationToken cancellationToken,
        ChannelReader<MmsPduEnvelope>? existingReader = null)
    {
        if (gracePeriod <= TimeSpan.Zero)
            return null;

        if (existingReader != null)
            return await WaitForTerminationAsync(existingReader, gracePeriod, cancellationToken).ConfigureAwait(false);

        await using var subscription = _transport.SubscribeInformationReports(out var reports, 16);
        return await WaitForTerminationAsync(reports, gracePeriod, cancellationToken).ConfigureAwait(false);
    }

    private async Task BestEffortCancelAsync(CancellationToken cancellationToken)
    {
        StopSelectionLease();
        var context = _activeSequence;
        if (context == null)
            return;

        try
        {
            if (Descriptor.CancelSpecification != null && _transport.IsAssociated)
            {
                var value = Iec61850ControlStructureBuilder.BuildCancel(
                    context,
                    Descriptor.CancelSpecification,
                    _options.RequireExactNamedControlFields);
                await _transport.WriteControlAsync(Descriptor.References.Cancel, value, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Association-loss and disposal cleanup must never mask the original fault.
        }
        finally
        {
            _activeSequence = null;
            ReleaseObject();
        }
    }

    private Iec61850ControlActionResult Accepted(
        Iec61850ControlAction action,
        Iec61850ControlSequenceContext context,
        TimeSpan elapsed,
        string diagnostic,
        IReadOnlyList<Iec61850ControlWireStep>? precedingWireSteps = null)
        => new()
        {
            Action = action,
            CompletionState = Iec61850ControlCompletionState.Accepted,
            RequestAccepted = true,
            RequestHex = _transport.LastRequestHex,
            ResponseHex = _transport.LastResponseHex,
            ControlNumber = context.ControlNumber,
            SequenceTimestamp = context.TimestampUtc,
            Elapsed = elapsed,
            Diagnostics = new[] { diagnostic },
            WireSteps = AppendWireSteps(
                precedingWireSteps,
                BuildWireStep(action, requestAccepted: true, diagnostic))
        };

    private Iec61850ControlActionResult FromWriteFailure(
        Iec61850ControlAction action,
        MmsWriteResult write,
        Iec61850CommandTermination? appError,
        Iec61850ControlSequenceContext context,
        TimeSpan elapsed,
        IReadOnlyList<Iec61850ControlWireStep>? precedingWireSteps = null)
        => new()
        {
            Action = action,
            CompletionState = Iec61850ControlCompletionState.Rejected,
            RequestAccepted = false,
            ClientError = write.Message,
            ControlError = appError?.ControlError ?? string.Empty,
            AddCause = appError?.AddCause ?? string.Empty,
            LastApplErrorText = appError?.LastApplErrorText ?? string.Empty,
            RequestHex = _transport.LastRequestHex,
            ResponseHex = appError?.ResponseHex ?? write.ResponseHexPreview,
            ControlNumber = context.ControlNumber,
            SequenceTimestamp = context.TimestampUtc,
            Elapsed = elapsed,
            WireSteps = AppendWireSteps(
                precedingWireSteps,
                BuildWireStep(action, requestAccepted: false, write.Message))
        };

    private Iec61850ControlActionResult FromApplicationRejection(
        Iec61850ControlAction action,
        Iec61850CommandTermination appError,
        Iec61850ControlSequenceContext context,
        TimeSpan elapsed,
        string clientMessage)
        => new()
        {
            Action = action,
            CompletionState = Iec61850ControlCompletionState.Rejected,
            RequestAccepted = true,
            // LastApplError during SelectWithValue is an application-service
            // rejection, not an Oper command-termination indication.
            CommandTerminationReceived = false,
            PositiveTermination = false,
            ClientError = clientMessage,
            ControlError = appError.ControlError,
            AddCause = appError.AddCause,
            LastApplErrorText = appError.LastApplErrorText,
            RequestHex = _transport.LastRequestHex,
            ResponseHex = appError.ResponseHex,
            ControlNumber = context.ControlNumber,
            SequenceTimestamp = context.TimestampUtc,
            Elapsed = elapsed,
            WireSteps = AppendWireSteps(
                null,
                BuildWireStep(action, requestAccepted: true, clientMessage))
        };

    private Iec61850ControlWireStep BuildWireStep(
        Iec61850ControlAction action,
        bool requestAccepted,
        string detail)
    {
        var reference = action switch
        {
            Iec61850ControlAction.Select => Descriptor.References.Sbo,
            Iec61850ControlAction.SelectWithValue => Descriptor.References.SboWithValue,
            Iec61850ControlAction.Operate => Descriptor.References.Oper,
            Iec61850ControlAction.Cancel => Descriptor.References.Cancel,
            _ => Descriptor.References.Oper
        };

        return new Iec61850ControlWireStep
        {
            Action = action,
            Reference = $"{reference.Domain}/{reference.Item}",
            RequestAccepted = requestAccepted,
            RequestHex = _transport.LastRequestHex,
            ResponseHex = _transport.LastResponseHex,
            Detail = detail
        };
    }

    private static IReadOnlyList<Iec61850ControlWireStep> AppendWireSteps(
        IReadOnlyList<Iec61850ControlWireStep>? preceding,
        Iec61850ControlWireStep current)
    {
        if (preceding == null || preceding.Count == 0)
            return new[] { current };
        return preceding.Concat(new[] { current }).ToArray();
    }

    private static Iec61850ControlActionResult SelectionTimedOut(
        Iec61850ControlSequenceContext context,
        TimeSpan elapsed)
        => new()
        {
            Action = Iec61850ControlAction.Operate,
            CompletionState = Iec61850ControlCompletionState.TimedOut,
            ClientError = "SBO selection timeout expired before Operate.",
            ControlNumber = context.ControlNumber,
            SequenceTimestamp = context.TimestampUtc,
            Elapsed = elapsed
        };

    private static Iec61850ControlActionResult Rejected(Iec61850ControlAction action, string message, TimeSpan elapsed)
        => new()
        {
            Action = action,
            CompletionState = Iec61850ControlCompletionState.Rejected,
            ClientError = message,
            Elapsed = elapsed
        };

    private static Iec61850ControlActionResult Unsupported(Iec61850ControlAction action, string message)
        => new()
        {
            Action = action,
            CompletionState = Iec61850ControlCompletionState.Unsupported,
            ClientError = message
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_transport.IsAssociated)
            throw new InvalidOperationException("MMS association is not active.");
    }
}
