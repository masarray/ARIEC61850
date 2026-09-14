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

        _lastHost = plan.Host;
        _lastPort = plan.Port;
        _lastTimeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : timeout;
        _nextInvokeId = 0;

        await ResetTransportAsync().ConfigureAwait(false);
        State = MmsAssociationState.Disconnected;
        ResetSclAssistedDiagnostics();

        var associationSucceeded = false;
        var profileName = $"SCL:{plan.IedName}/{plan.AccessPointName}";

        try
        {
            await _tpkt.ConnectAsync(_lastHost, _lastPort, _lastTimeout, cancellationToken).ConfigureAwait(false);
            State = MmsAssociationState.TcpConnected;

            await _cotp.ConnectAsync(plan.Cotp, cancellationToken).ConfigureAwait(false);
            State = MmsAssociationState.CotpConnected;
            LastHandshakeMessage = $"{profileName}: {_cotp.LastConnectionConfirm?.Message ?? "COTP connection confirmed."}";

            var associationProfile = new AcseAssociationProfile(
                profileName,
                "Exact SCL-assisted MMS association plan.",
                plan.SessionPresentationAcseMmsRequest.ToArray());

            var association = await TryInitiateMmsAssociationAsync(associationProfile, cancellationToken).ConfigureAwait(false);
            LastAssociationAttempts =
            [
                new AcseAssociationAttempt
                {
                    ProfileName = profileName,
                    IsAccepted = association.IsAccepted,
                    Message = association.Message,
                    ResponseHexPreview = association.ResponseHexPreview
                }
            ];

            if (!association.IsAccepted)
            {
                State = MmsAssociationState.MmsInitiateFailed;
                LastHandshakeMessage = association.Message;
                await ResetTransportAsync().ConfigureAwait(false);
                State = MmsAssociationState.MmsInitiateFailed;

                return BuildSclAssistedResult(
                    SclAssistedMmsOnlineStatus.AssociationFailed,
                    plan,
                    associationSucceeded: false,
                    domainInventorySucceeded: false,
                    domains: null,
                    association.Message);
            }

            associationSucceeded = true;
            State = MmsAssociationState.MmsInitiated;
            LastHandshakeMessage = association.Message;
            _receivePump.Start(cancellationToken);

            var domainInventory = await GetNameListPagedAsync(
                    MmsGetNameListObjectClass.Domain,
                    domainId: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!domainInventory.IsSuccess)
            {
                return BuildSclAssistedResult(
                    SclAssistedMmsOnlineStatus.DomainInventoryFailed,
                    plan,
                    associationSucceeded: true,
                    domainInventorySucceeded: false,
                    domains: null,
                    domainInventory.Message);
            }

            var reconciliation = SclMmsDomainReconciler.Reconcile(
                designDomains.ExpectedDomains,
                domainInventory.Names);
            var status = reconciliation.IsCompatible
                ? SclAssistedMmsOnlineStatus.Compatible
                : SclAssistedMmsOnlineStatus.DomainMismatch;

            LastHandshakeMessage = $"{profileName}: association accepted. {reconciliation.Summary}";
            return BuildSclAssistedResult(
                status,
                plan,
                associationSucceeded: true,
                domainInventorySucceeded: true,
                reconciliation,
                LastHandshakeMessage);
        }
        catch (OperationCanceledException)
        {
            await ResetTransportAsync().ConfigureAwait(false);
            State = MmsAssociationState.Disconnected;
            throw;
        }
        catch (Exception ex)
        {
            var message = associationSucceeded
                ? $"{profileName}: Domain/VMD inventory transport failure after association: {ex.GetType().Name}: {ex.Message}"
                : $"{profileName}: SCL-assisted transport/association failure: {ex.GetType().Name}: {ex.Message}";

            LastAssociationAttempts = associationSucceeded
                ? LastAssociationAttempts
                :
                [
                    new AcseAssociationAttempt
                    {
                        ProfileName = profileName,
                        IsAccepted = false,
                        Message = message,
                        ResponseHexPreview = LastAssociationResponseHex
                    }
                ];
            LastHandshakeMessage = message;
            await ResetTransportAsync().ConfigureAwait(false);
            State = MmsAssociationState.MmsInitiateFailed;

            return BuildSclAssistedResult(
                associationSucceeded
                    ? SclAssistedMmsOnlineStatus.DomainInventoryFailed
                    : SclAssistedMmsOnlineStatus.AssociationFailed,
                plan,
                associationSucceeded,
                domainInventorySucceeded: false,
                domains: null,
                message);
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
}
