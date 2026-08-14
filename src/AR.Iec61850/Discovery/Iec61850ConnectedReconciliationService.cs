using AR.Iec61850.Mms;

namespace AR.Iec61850.Discovery;

/// <summary>
/// High-level reconciliation facade for an already connected native MMS session.
/// Application layers provide design/live models and receive engine verdicts; probe
/// construction, MMS failure interpretation and alternate-reference behavior stay here.
/// </summary>
public sealed class Iec61850ConnectedReconciliationService
{
    private readonly MmsClientSession _session;
    private readonly IIec61850ExactReadProbe _probe;

    public Iec61850ConnectedReconciliationService(MmsClientSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _probe = new MmsClientSessionExactReadProbe(session);
    }

    public bool IsSessionReady => _session.IsMmsInitiated;
    public MmsAssociationState SessionState => _session.State;

    public Task<Iec61850DesignLiveReconciliationDocument> ReconcileAsync(
        LiveIedModelDiscoveryDocument design,
        LiveIedModelDiscoveryDocument observed,
        Iec61850DesignLiveReconciliationOptions? options = null,
        CancellationToken cancellationToken = default)
        => Iec61850DesignLiveReconciler.ReconcileAsync(
            design,
            observed,
            _probe,
            options,
            cancellationToken);
}
