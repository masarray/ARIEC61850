using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    /// <summary>
    /// One finite, strictly ordered read-only MMS Confirmed-Read of 1..10
    /// EXPLICITLY supplied and separately schema-qualified FC-group roots.
    /// This does not discover, probe arbitrary names, retry, write, enable an
    /// RCB, create a DataSet, or start cyclic polling.
    /// One result per original reference, exact invoke ID, fail-closed decode.
    /// </summary>
    public async Task<MmsReadBatchResult> ReadVerifiedVariablesBatchAsync(
        IReadOnlyList<MmsObjectReference> verifiedRoots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifiedRoots);
        if (verifiedRoots.Count is < 1 or > MmsReadBatchCodec.MaximumVariableReferencesPerRead)
            throw new ArgumentOutOfRangeException(nameof(verifiedRoots),
                "One verified FC-root Read must contain 1..10 exact variables.");
        if (verifiedRoots.Any(root => string.IsNullOrWhiteSpace(root.Domain) ||
                                      string.IsNullOrWhiteSpace(root.Item)))
            throw new ArgumentException("Verified FC roots must have exact MMS domain and item.",
                nameof(verifiedRoots));
        if (verifiedRoots.Select(root => root.Domain + "/" + root.Item)
                .Distinct(StringComparer.Ordinal).Count() != verifiedRoots.Count)
            throw new ArgumentException("Duplicate MMS variable names are not admissible.",
                nameof(verifiedRoots));
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMmsReady();

        var invokeId = NextInvokeId();
        var request = MmsReadBatchCodec.BuildRequest(
            invokeId, verifiedRoots, MmsReadPayloadProfile.PresentationDataValues);
        LastReadRequestHex = HexDump.ToCompactString(request);
        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(
                request, invokeId, cancellationToken).ConfigureAwait(false);
            var decoded = MmsReadBatchCodec.DecodeResponse(
                response, verifiedRoots, invokeId);
            LastReadResponseHex = decoded.ResponseHexPreview;
            return decoded;
        }
        catch (OperationCanceledException)
        {
            // A late Confirmed-Read reply must not leak into another flight.
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                    ObjectDisposedException or InvalidOperationException)
        {
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            var reason = "Verified FC-root batch transport/session error: " +
                         ex.GetType().Name + ": " + ex.Message;
            return new MmsReadBatchResult
            {
                Results = verifiedRoots.Select(root => new MmsReadAccessResult
                {
                    Reference = root, IsSuccess = false, Message = reason
                }).ToArray(),
                Message = reason
            };
        }
    }
}
