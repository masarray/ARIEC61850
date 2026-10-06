using AR.Iec61850.Acse;
using AR.Iec61850.Osi;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    public Task<SclAssistedMmsOnlineResult> ConnectSclAssistedAsync(
        SclAssistedMmsAssociationPlan plan,
        SclMmsDomainInventory designDomains,
        CancellationToken cancellationToken = default)
        => ConnectSclAssistedAsync(
            plan,
            designDomains,
            TimeSpan.FromSeconds(5),
            cancellationToken);

    /// <summary>
    /// Opt-in SCL-assisted MMS association. The supplied Step-2 plan is used exactly for
    /// COTP and ISO Session/Presentation/ACSE/MMS association, then the live endpoint is
    /// queried only for the VMD Domain GetNameList inventory. No NamedVariable, GVAA,
    /// DataSet-directory, write, control, or report operation is performed here.
    /// </summary>
    public async Task<SclAssistedMmsOnlineResult> ConnectSclAssistedAsync(
        SclAssistedMmsAssociationPlan plan,
        SclMmsDomainInventory designDomains,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(designDomains);

        var validationError = ValidateSclAssistedInputs(plan, designDomains);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return BuildSclAssistedResult(
                SclAssistedMmsOnlineStatus.InvalidPlan,
                plan,
                associationSucceeded: false,
                domainInventorySucceeded: false,
                domains: null,
                validationError);
        }

        return await ConnectSclAssistedAsync(
                SclAssistedMmsAssociationCandidateResolver.FromExactPlan(plan),
                designDomains,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<SclAssistedMmsOnlineResult> ConnectSclAssistedAsync(
        SclAssistedMmsAssociationResolution resolution,
        SclMmsDomainInventory designDomains,
        CancellationToken cancellationToken = default)
        => ConnectSclAssistedAsync(
            resolution,
            designDomains,
            TimeSpan.FromSeconds(5),
            cancellationToken);

    /// <summary>
    /// Bounded SCL-assisted association negotiation. Candidates are engine-owned and
    /// already constrained by explicit SCL evidence. Attempts are strictly serial and
    /// every candidate starts from a fresh TCP/COTP transport. After the first accepted
    /// MMS association only VMD Domain GetNameList is used to reconcile the trusted SCL
    /// model; no NamedVariable/GVAA/DataSet discovery is introduced here.
    /// </summary>
    public async Task<SclAssistedMmsOnlineResult> ConnectSclAssistedAsync(
        SclAssistedMmsAssociationResolution resolution,
        SclMmsDomainInventory designDomains,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(designDomains);

        var validationError = ValidateSclAssistedInputs(resolution, designDomains);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return BuildSclAssistedResult(
                SclAssistedMmsOnlineStatus.InvalidPlan,
                resolution,
                associationSucceeded: false,
                domainInventorySucceeded: false,
                domains: null,
                validationError,
                selectedCandidate: null,
                associationAttemptCount: 0);
        }

        _lastHost = resolution.Host;
        _lastPort = resolution.Port;
        _lastTimeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : timeout;
        _nextInvokeId = 0;

        await ResetTransportAsync().ConfigureAwait(false);
        State = MmsAssociationState.Disconnected;
        ResetSclAssistedDiagnostics();

        var attempts = new List<AcseAssociationAttempt>();
        SclAssistedMmsAssociationCandidate? selectedCandidate = null;
        var sawTimedOutAttempt = false;
        var sawNonTimeoutFailure = false;

        foreach (var candidate in resolution.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ResetTransportAsync().ConfigureAwait(false);
            State = MmsAssociationState.Disconnected;
            LastAssociationResponseHex = string.Empty;

            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(_lastTimeout);
            var attemptToken = attemptTimeout.Token;

            try
            {
                await _tpkt.ConnectAsync(_lastHost, _lastPort, _lastTimeout, attemptToken).ConfigureAwait(false);
                State = MmsAssociationState.TcpConnected;

                await _cotp.ConnectAsync(candidate.Cotp, attemptToken).ConfigureAwait(false);
                State = MmsAssociationState.CotpConnected;
                LastHandshakeMessage =
                    $"{candidate.Name}: {_cotp.LastConnectionConfirm?.Message ?? "COTP connection confirmed."}";

                var association = await TryInitiateMmsAssociationAsync(
                        candidate.AssociationProfile,
                        attemptToken)
                    .ConfigureAwait(false);

                attempts.Add(new AcseAssociationAttempt
                {
                    ProfileName = candidate.Name,
                    IsAccepted = association.IsAccepted,
                    Message = association.Message,
                    ResponseHexPreview = association.ResponseHexPreview
                });
                LastAssociationAttempts = attempts.ToArray();

                if (!association.IsAccepted)
                {
                    sawNonTimeoutFailure = true;
                    State = MmsAssociationState.MmsInitiateFailed;
                    continue;
                }

                selectedCandidate = candidate;
                State = MmsAssociationState.MmsInitiated;
                LastHandshakeMessage = $"{candidate.Name}: {association.Message}";
                break;
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                attemptTimeout.IsCancellationRequested)
            {
                sawTimedOutAttempt = true;
                State = MmsAssociationState.MmsInitiateFailed;
                attempts.Add(new AcseAssociationAttempt
                {
                    ProfileName = candidate.Name,
                    IsAccepted = false,
                    Message = $"{candidate.Name}: association attempt timed out after {_lastTimeout.TotalMilliseconds:0} ms.",
                    ResponseHexPreview = LastAssociationResponseHex
                });
                LastAssociationAttempts = attempts.ToArray();
            }
            catch (OperationCanceledException)
            {
                await ResetTransportAsync().ConfigureAwait(false);
                State = MmsAssociationState.Disconnected;
                throw;
            }
            catch (Exception ex) when (
                ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
            {
                sawNonTimeoutFailure = true;
                State = MmsAssociationState.MmsInitiateFailed;
                attempts.Add(new AcseAssociationAttempt
                {
                    ProfileName = candidate.Name,
                    IsAccepted = false,
                    Message = $"{candidate.Name}: transport/association failure: {ex.GetType().Name}: {ex.Message}",
                    ResponseHexPreview = LastAssociationResponseHex
                });
                LastAssociationAttempts = attempts.ToArray();
            }
        }

        if (selectedCandidate is null)
        {
            await ResetTransportAsync().ConfigureAwait(false);
            State = MmsAssociationState.MmsInitiateFailed;
            LastAssociationAttempts = attempts.ToArray();
            LastHandshakeMessage = LastAssociationAttemptSummary;
            var status = sawTimedOutAttempt && !sawNonTimeoutFailure
                ? SclAssistedMmsOnlineStatus.TimedOut
                : SclAssistedMmsOnlineStatus.AssociationFailed;
            var message = string.IsNullOrWhiteSpace(LastHandshakeMessage)
                ? "All bounded SCL-assisted association candidates failed."
                : LastHandshakeMessage;

            return BuildSclAssistedResult(
                status,
                resolution,
                associationSucceeded: false,
                domainInventorySucceeded: false,
                domains: null,
                message,
                selectedCandidate: null,
                associationAttemptCount: attempts.Count);
        }

        _receivePump.Start(cancellationToken);

        using var domainTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        domainTimeout.CancelAfter(_lastTimeout);
        var domainToken = domainTimeout.Token;

        try
        {
            var domainInventory = await GetNameListPagedAsync(
                    MmsGetNameListObjectClass.Domain,
                    domainId: null,
                    cancellationToken: domainToken)
                .ConfigureAwait(false);

            if (!domainInventory.IsSuccess)
            {
                return BuildSclAssistedResult(
                    SclAssistedMmsOnlineStatus.DomainInventoryFailed,
                    resolution,
                    associationSucceeded: true,
                    domainInventorySucceeded: false,
                    domains: null,
                    domainInventory.Message,
                    selectedCandidate,
                    attempts.Count);
            }

            var reconciliation = SclMmsDomainReconciler.Reconcile(
                designDomains.ExpectedDomains,
                domainInventory.Names);
            var status = reconciliation.IsCompatible
                ? SclAssistedMmsOnlineStatus.Compatible
                : SclAssistedMmsOnlineStatus.DomainMismatch;

            LastHandshakeMessage =
                $"{selectedCandidate.Name}: association accepted. {reconciliation.Summary}";
            return BuildSclAssistedResult(
                status,
                resolution,
                associationSucceeded: true,
                domainInventorySucceeded: true,
                reconciliation,
                LastHandshakeMessage,
                selectedCandidate,
                attempts.Count);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            domainTimeout.IsCancellationRequested)
        {
            var message =
                $"{selectedCandidate.Name}: Domain/VMD inventory timed out after {_lastTimeout.TotalMilliseconds:0} ms.";
            LastHandshakeMessage = message;
            await ResetTransportAsync().ConfigureAwait(false);
            State = MmsAssociationState.MmsInitiateFailed;

            return BuildSclAssistedResult(
                SclAssistedMmsOnlineStatus.TimedOut,
                resolution,
                associationSucceeded: true,
                domainInventorySucceeded: false,
                domains: null,
                message,
                selectedCandidate,
                attempts.Count);
        }
        catch (OperationCanceledException)
        {
            await ResetTransportAsync().ConfigureAwait(false);
            State = MmsAssociationState.Disconnected;
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            var message =
                $"{selectedCandidate.Name}: Domain/VMD inventory transport failure after association: " +
                $"{ex.GetType().Name}: {ex.Message}";
            LastHandshakeMessage = message;
            await ResetTransportAsync().ConfigureAwait(false);
            State = MmsAssociationState.MmsInitiateFailed;

            return BuildSclAssistedResult(
                SclAssistedMmsOnlineStatus.DomainInventoryFailed,
                resolution,
                associationSucceeded: true,
                domainInventorySucceeded: false,
                domains: null,
                message,
                selectedCandidate,
                attempts.Count);
        }
    }

    private void ResetSclAssistedDiagnostics()
    {
        LastHandshakeMessage = string.Empty;
        LastAssociationResponseHex = string.Empty;
        LastAssociationAttempts = Array.Empty<AcseAssociationAttempt>();
        LastDiscoveryRequestHex = string.Empty;
        LastDiscoveryResponseHex = string.Empty;
        LastDiscoveryAttemptSummary = string.Empty;
        LastReadRequestHex = string.Empty;
        LastReadResponseHex = string.Empty;
        LastReadAttempts = Array.Empty<MmsReadAttempt>();
        LastReceiveRoutingSummary = string.Empty;
        _receiveRouter.Clear();
    }

    private static string ValidateSclAssistedInputs(
        SclAssistedMmsAssociationPlan plan,
        SclMmsDomainInventory designDomains)
    {
        if (!designDomains.IsSuccess)
            return "SCL MMS design-domain inventory is not valid: " + string.Join(" | ", designDomains.Errors);
        if (string.IsNullOrWhiteSpace(plan.Host))
            return "SCL-assisted association plan has no remote host.";
        if (plan.Port is < 1 or > 65535)
            return $"SCL-assisted association plan contains invalid TCP port {plan.Port}.";
        if (!string.Equals(plan.IedName, designDomains.IedName, StringComparison.Ordinal) ||
            !string.Equals(plan.AccessPointName, designDomains.AccessPointName, StringComparison.Ordinal))
        {
            return $"SCL association plan identity '{plan.IedName}/{plan.AccessPointName}' does not match design-domain identity '{designDomains.IedName}/{designDomains.AccessPointName}'.";
        }

        try
        {
            var rebuiltCotp = CotpConnectRequest.Build(plan.Cotp);
            if (!rebuiltCotp.SequenceEqual(plan.CotpConnectRequest))
                return "SCL-assisted COTP plan is internally inconsistent; exact prebuilt bytes no longer match its typed parameters.";

            var rebuiltAssociation = AcseMmsAssociationRequestBuilder.BuildSessionConnect(plan.Association);
            if (!rebuiltAssociation.SequenceEqual(plan.SessionPresentationAcseMmsRequest))
                return "SCL-assisted ACSE/MMS plan is internally inconsistent; exact prebuilt bytes no longer match its typed parameters.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"SCL-assisted association plan validation failed: {ex.GetType().Name}: {ex.Message}";
        }

        return string.Empty;
    }

    private static string ValidateSclAssistedInputs(
        SclAssistedMmsAssociationResolution resolution,
        SclMmsDomainInventory designDomains)
    {
        if (!designDomains.IsSuccess)
            return "SCL MMS design-domain inventory is not valid: " + string.Join(" | ", designDomains.Errors);
        if (!resolution.IsSuccess)
            return resolution.Errors.Count == 0
                ? "SCL-assisted association resolution contains no usable candidate."
                : string.Join(" | ", resolution.Errors);
        if (resolution.Candidates.Count > SclAssistedMmsAssociationCandidateResolver.MaximumCandidateCount)
            return $"SCL-assisted association resolution exceeds the bounded candidate limit of {SclAssistedMmsAssociationCandidateResolver.MaximumCandidateCount}.";
        if (!string.Equals(resolution.IedName, designDomains.IedName, StringComparison.Ordinal) ||
            !string.Equals(resolution.AccessPointName, designDomains.AccessPointName, StringComparison.Ordinal))
        {
            return $"SCL association identity '{resolution.IedName}/{resolution.AccessPointName}' does not match design-domain identity '{designDomains.IedName}/{designDomains.AccessPointName}'.";
        }

        foreach (var candidate in resolution.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Name))
                return "SCL-assisted association candidate has no stable name.";
            if (candidate.AssociationProfile.Payload.Length == 0)
                return $"SCL-assisted association candidate '{candidate.Name}' has an empty ACSE/MMS payload.";

            try
            {
                _ = CotpConnectRequest.Build(candidate.Cotp);
                var inspection = AcseAssociationPayloadInspector.Inspect(candidate.AssociationProfile.Payload);
                if (!inspection.LooksLikeClientAssociateRequest)
                    return $"SCL-assisted association candidate '{candidate.Name}' is not a valid client associate request: {inspection.Message}";
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return $"SCL-assisted association candidate '{candidate.Name}' validation failed: {ex.GetType().Name}: {ex.Message}";
            }
        }

        return string.Empty;
    }

    private SclAssistedMmsOnlineResult BuildSclAssistedResult(
        SclAssistedMmsOnlineStatus status,
        SclAssistedMmsAssociationPlan plan,
        bool associationSucceeded,
        bool domainInventorySucceeded,
        SclMmsDomainReconciliation? domains,
        string message)
        => new()
        {
            Status = status,
            IedName = plan.IedName,
            AccessPointName = plan.AccessPointName,
            Host = plan.Host,
            Port = plan.Port,
            AssociationSucceeded = associationSucceeded,
            DomainInventorySucceeded = domainInventorySucceeded,
            SessionRemainsOpen = IsMmsInitiated && IsTransportConnected,
            Domains = domains,
            Message = message
        };

    private SclAssistedMmsOnlineResult BuildSclAssistedResult(
        SclAssistedMmsOnlineStatus status,
        SclAssistedMmsAssociationResolution resolution,
        bool associationSucceeded,
        bool domainInventorySucceeded,
        SclMmsDomainReconciliation? domains,
        string message,
        SclAssistedMmsAssociationCandidate? selectedCandidate,
        int associationAttemptCount)
        => new()
        {
            Status = status,
            IedName = resolution.IedName,
            AccessPointName = resolution.AccessPointName,
            Host = resolution.Host,
            Port = resolution.Port,
            AssociationSucceeded = associationSucceeded,
            DomainInventorySucceeded = domainInventorySucceeded,
            SessionRemainsOpen = IsMmsInitiated && IsTransportConnected,
            SelectedAssociationCandidateName = selectedCandidate?.Name ?? string.Empty,
            SelectedAssociationCandidateSource = selectedCandidate?.Source,
            AssociationAttemptCount = associationAttemptCount,
            AssociationResolutionNotes = selectedCandidate?.ResolutionNotes ?? Array.Empty<string>(),
            Domains = domains,
            Message = message
        };
}
