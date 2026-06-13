using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    public async Task<MmsVariableAccessAttributesResult> GetVariableAccessAttributesAsync(
        MmsObjectReference reference,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();

        var invokeId = NextInvokeId();
        var request = MmsVariableAccessAttributesRequest.Build(invokeId, reference);
        LastDiscoveryRequestHex = HexDump.ToCompactString(request);

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsVariableAccessAttributesResponseDecoder.Decode(response, invokeId, reference);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Summary;
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            var result = new MmsVariableAccessAttributesResult
            {
                IsSuccess = false,
                Reference = reference,
                Message = $"GetVariableAccessAttributes transport fault: {ex.GetType().Name}: {ex.Message}",
                ResponseHexPreview = LastDiscoveryResponseHex
            };
            LastDiscoveryAttemptSummary = result.Summary;
            return result;
        }
    }

    public async Task<IReadOnlyList<MmsVariableAccessAttributesResult>> GetVariableAccessAttributesBatchAsync(
        IEnumerable<MmsObjectReference> references,
        int maxCount = 256,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(references);

        var results = new List<MmsVariableAccessAttributesResult>();
        var limit = maxCount <= 0 ? int.MaxValue : maxCount;
        var distinct = references
            .Where(x => !string.IsNullOrWhiteSpace(x.Domain) && !string.IsNullOrWhiteSpace(x.Item))
            .DistinctBy(x => $"{x.Domain}/{x.Item}", StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

        foreach (var reference in distinct)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await GetVariableAccessAttributesAsync(reference, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (!IsMmsInitiated)
                break;
        }

        return results;
    }
}
