using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed class MmsReadAccessResult
{
    public MmsObjectReference Reference { get; init; }
    public bool IsSuccess { get; init; }
    public MmsDataValue? Value { get; init; }
    public int? FailureCode { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class MmsReadBatchResult
{
    public IReadOnlyList<MmsReadAccessResult> Results { get; init; } = Array.Empty<MmsReadAccessResult>();
    public int ExtraAccessResultCount { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
    public bool IsSuccess => Results.Count > 0 && Results.All(result => result.IsSuccess) && ExtraAccessResultCount == 0;
    public bool HasAnySuccess => Results.Any(result => result.IsSuccess);
}

/// <summary>
/// Bounded MMS Confirmed-Read codec for an explicitly ordered variable list.
/// The Step-4 initial-value workflow deliberately caps one request at ten variables.
/// </summary>
public static class MmsReadBatchCodec
{
    public const int MaximumVariableReferencesPerRead = 10;

    public static byte[] BuildRequest(
        int invokeId,
        IReadOnlyList<MmsObjectReference> references,
        MmsReadPayloadProfile payloadProfile = MmsReadPayloadProfile.PresentationDataValues)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count == 0)
            throw new ArgumentException("MMS batch Read requires at least one variable reference.", nameof(references));
        if (references.Count > MaximumVariableReferencesPerRead)
            throw new ArgumentOutOfRangeException(nameof(references), $"MMS batch Read is bounded to {MaximumVariableReferencesPerRead} variable references.");

        var variableSpecifications = new byte[references.Count][];
        for (var index = 0; index < references.Count; index++)
        {
            var reference = references[index];
            if (string.IsNullOrWhiteSpace(reference.Domain))
                throw new ArgumentException($"MMS batch Read reference {index} has an empty domain.", nameof(references));
            if (string.IsNullOrWhiteSpace(reference.Item))
                throw new ArgumentException($"MMS batch Read reference {index} has an empty item.", nameof(references));

            var domainSpecificObjectName = BerWriter.EncodeTlv(
                0xA1,
                MmsPresentation.Concat(
                    MmsPresentation.VisibleString(reference.Domain),
                    MmsPresentation.VisibleString(reference.Item)));
            var variableSpecificationName = BerWriter.EncodeTlv(0xA0, domainSpecificObjectName);
            variableSpecifications[index] = BerWriter.EncodeTlv(0x30, variableSpecificationName);
        }

        var listOfVariable = BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(variableSpecifications));
        var variableAccessSpecification = BerWriter.EncodeTlv(0xA1, listOfVariable);
        var includeSpecificationWithResult = payloadProfile == MmsReadPayloadProfile.PresentationDataValuesWithSpecificationResult;
        var readRequestBody = includeSpecificationWithResult
            ? MmsPresentation.Concat([0x80, 0x01, 0xFF], variableAccessSpecification)
            : variableAccessSpecification;
        var readRequest = BerWriter.EncodeTlv(0xA4, readRequestBody);
        var mmsPdu = BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(MmsPresentation.Integer(invokeId), readRequest));

        return payloadProfile switch
        {
            MmsReadPayloadProfile.PresentationDataValues or MmsReadPayloadProfile.PresentationDataValuesWithSpecificationResult
                => MmsPresentation.WrapIsoPresentationPData(mmsPdu),
            MmsReadPayloadProfile.SessionDataOnly => MmsPresentation.Concat([0x01, 0x00], mmsPdu),
            MmsReadPayloadProfile.RawMmsPdu => mmsPdu,
            _ => MmsPresentation.WrapIsoPresentationPData(mmsPdu)
        };
    }

    public static MmsReadBatchResult DecodeResponse(
        ReadOnlyMemory<byte> presentationPayload,
        IReadOnlyList<MmsObjectReference> references,
        int? expectedInvokeId = null)
    {
        ArgumentNullException.ThrowIfNull(references);
        var hex = HexDump.ToCompactString(presentationPayload.Span);

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return FailAll(references, "Empty MMS response payload.", hex);
            if (mms[0] == 0xA2)
                return FailAll(references, $"MMS Confirmed-Error PDU received: {HexDump.ToCompactString(mms)}", hex);
            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return FailAll(references, $"MMS Reject/Abort PDU received: {HexDump.ToCompactString(mms)}", hex);

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer) || outer.EncodedTag != 0xA1)
                return FailAll(references, "Expected MMS Confirmed-Response PDU [1].", hex);

            var confirmedChildren = BerReader.ReadChildren(outer.Value);
            if (confirmedChildren.Count == 0 || confirmedChildren[0].EncodedTag != 0x02)
                return FailAll(references, "MMS Confirmed-Response did not start with invokeID.", hex);

            if (expectedInvokeId.HasValue)
            {
                var actual = BerReader.ReadUnsignedInteger(confirmedChildren[0]);
                if (actual != (ulong)expectedInvokeId.Value)
                    return FailAll(references, $"MMS invokeID mismatch. Expected {expectedInvokeId.Value}, received {actual}.", hex);
            }

            var readService = confirmedChildren
                .Skip(1)
                .FirstOrDefault(node => node.Class == BerClass.ContextSpecific && node.TagNumber == 4);
            if (readService.EncodedTag == 0 || !readService.Constructed)
                return FailAll(references, "MMS Confirmed-Response has no Read response service.", hex);

            var serviceChildren = BerReader.ReadChildren(readService.Value);
            var accessList = serviceChildren.LastOrDefault(node =>
                node.Class == BerClass.ContextSpecific && node.TagNumber == 1 && node.Constructed);
            if (accessList.EncodedTag == 0)
                return FailAll(references, "MMS Read response has no listOfAccessResult.", hex);

            var accessResults = BerReader.ReadChildren(accessList.Value);
            var results = new List<MmsReadAccessResult>(references.Count);
            for (var index = 0; index < references.Count; index++)
            {
                if (index >= accessResults.Count)
                {
                    results.Add(new MmsReadAccessResult
                    {
                        Reference = references[index],
                        IsSuccess = false,
                        Message = $"MMS Read response ended before access result {index}."
                    });
                    continue;
                }

                results.Add(DecodeAccessResult(references[index], accessResults[index]));
            }

            var extras = Math.Max(0, accessResults.Count - references.Count);
            return new MmsReadBatchResult
            {
                Results = results,
                ExtraAccessResultCount = extras,
                Message = $"MMS batch Read decoded {results.Count(result => result.IsSuccess)}/{references.Count} requested value(s); extraAccessResults={extras}.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return FailAll(references, $"MMS batch Read response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static MmsReadAccessResult DecodeAccessResult(MmsObjectReference reference, BerTlv accessResult)
    {
        if (accessResult.Class == BerClass.ContextSpecific && accessResult.TagNumber == 0 && !accessResult.Constructed)
        {
            var code = BerReader.ReadUnsignedInteger(accessResult);
            return new MmsReadAccessResult
            {
                Reference = reference,
                IsSuccess = false,
                FailureCode = code.HasValue ? (int)code.Value : null,
                Message = code.HasValue
                    ? $"MMS Read AccessResult.failure code {code.Value}."
                    : "MMS Read AccessResult.failure code is undecodable."
            };
        }

        if (IsMmsData(accessResult))
        {
            var value = MmsDataCodec.Decode(accessResult);
            return new MmsReadAccessResult
            {
                Reference = reference,
                IsSuccess = value.Kind != MmsDataKind.Unknown,
                Value = value.Kind == MmsDataKind.Unknown ? null : value,
                Message = value.Kind == MmsDataKind.Unknown ? "MMS Data value is unknown." : "MMS Data value decoded."
            };
        }

        if (accessResult.Class == BerClass.ContextSpecific && accessResult.TagNumber == 0 && accessResult.Constructed)
        {
            var child = BerReader.ReadChildren(accessResult.Value).FirstOrDefault(IsMmsData);
            if (child.EncodedTag != 0)
            {
                var value = MmsDataCodec.Decode(child);
                return new MmsReadAccessResult
                {
                    Reference = reference,
                    IsSuccess = value.Kind != MmsDataKind.Unknown,
                    Value = value.Kind == MmsDataKind.Unknown ? null : value,
                    Message = value.Kind == MmsDataKind.Unknown ? "Wrapped MMS Data value is unknown." : "Wrapped MMS Data value decoded."
                };
            }
        }

        return new MmsReadAccessResult
        {
            Reference = reference,
            IsSuccess = false,
            Message = $"Unsupported MMS AccessResult tag 0x{accessResult.EncodedTag:X2}."
        };
    }

    private static bool IsMmsData(BerTlv node)
    {
        if (node.Class != BerClass.ContextSpecific)
            return false;

        return node.TagNumber switch
        {
            1 or 2 => node.Constructed,
            >= 3 and <= 17 => !node.Constructed,
            _ => false
        };
    }

    private static MmsReadBatchResult FailAll(
        IReadOnlyList<MmsObjectReference> references,
        string message,
        string hex)
        => new()
        {
            Results = references.Select(reference => new MmsReadAccessResult
            {
                Reference = reference,
                IsSuccess = false,
                Message = message
            }).ToArray(),
            Message = message,
            ResponseHexPreview = hex
        };
}
