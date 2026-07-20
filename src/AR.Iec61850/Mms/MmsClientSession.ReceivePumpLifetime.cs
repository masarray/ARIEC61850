namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Rebinds the confirmed-service receive pump to the MMS association lifetime instead
    /// of the cancellation token that was used only to establish the connection.
    /// Call this when a long-lived session will be reused across multiple UI operations.
    /// </summary>
    public async Task RebindReceivePumpToSessionLifetimeAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        cancellationToken.ThrowIfCancellationRequested();

        if (_receivePump.PendingOperationCount != 0)
        {
            throw new InvalidOperationException(
                "The MMS receive pump cannot be rebound while confirmed operations are pending.");
        }

        await MmsReceivePumpLifetimePolicy.RebindAsync(
            _receivePump,
            cancellationToken).ConfigureAwait(false);

        if (!IsTransportConnected || !IsMmsInitiated || !_receivePump.IsRunning)
        {
            throw new InvalidOperationException(
                "The MMS receive pump could not be rebound to the active association lifetime.");
        }

        LastReceiveRoutingSummary =
            "MMS receive pump rebound to association-owned lifetime for reusable confirmed services.";
    }
}

internal static class MmsReceivePumpLifetimePolicy
{
    public static async Task RebindAsync(
        MmsReceivePump receivePump,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receivePump);
        cancellationToken.ThrowIfCancellationRequested();

        await receivePump.StopAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        receivePump.Start();
    }
}
