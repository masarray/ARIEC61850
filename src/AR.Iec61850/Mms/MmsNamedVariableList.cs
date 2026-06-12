using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed class MmsDefineNamedVariableListResult
{
    public bool IsSuccess { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public sealed class MmsDeleteNamedVariableListResult
{
    public bool IsSuccess { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public uint? NumberMatched { get; init; }
    public uint? NumberDeleted { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public static class MmsDefineNamedVariableListRequest
{
    public static byte[] Build(int invokeId, string dataSetReference, IEnumerable<MmsObjectReference> members)
    {
        var (domain, itemName) = MmsDataSetDirectoryRequest.ParseDataSetReference(dataSetReference);
        return Build(invokeId, domain, itemName, members);
    }

    public static byte[] Build(int invokeId, string domain, string dataSetMmsName, IEnumerable<MmsObjectReference> members)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSetMmsName);
        ArgumentNullException.ThrowIfNull(members);

        var materialized = members.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("Dynamic DataSet requires at least one member.", nameof(members));

        var variableListName = MmsDataSetDirectoryRequest.EncodeDomainSpecificObjectName(domain.Trim(), dataSetMmsName.Trim());
        var listOfVariable = BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(materialized.Select(BuildVariableDef).ToArray()));
        var request = BerWriter.EncodeTlv(0xAB, MmsPresentation.Concat(variableListName, listOfVariable));
        var confirmedRequest = BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(MmsPresentation.Integer(invokeId), request));
        return MmsPresentation.WrapIsoPresentationPData(confirmedRequest);
    }

    private static byte[] BuildVariableDef(MmsObjectReference reference)
    {
        var objectName = MmsDataSetDirectoryRequest.EncodeDomainSpecificObjectName(reference.Domain, reference.Item);
        var variableSpecificationName = BerWriter.EncodeTlv(0xA0, objectName);
        return BerWriter.EncodeTlv(0x30, variableSpecificationName);
    }
}

public static class MmsDeleteNamedVariableListRequest
{
    public static byte[] Build(int invokeId, string dataSetReference)
    {
        var (domain, itemName) = MmsDataSetDirectoryRequest.ParseDataSetReference(dataSetReference);
        return Build(invokeId, domain, itemName);
    }

    public static byte[] Build(int invokeId, string domain, string dataSetMmsName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSetMmsName);

        var variableListName = MmsDataSetDirectoryRequest.EncodeDomainSpecificObjectName(domain.Trim(), dataSetMmsName.Trim());
        var listOfVariableListName = BerWriter.EncodeTlv(0xA1, variableListName);
        var request = BerWriter.EncodeTlv(0xAD, listOfVariableListName);
        var confirmedRequest = BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(MmsPresentation.Integer(invokeId), request));
        return MmsPresentation.WrapIsoPresentationPData(confirmedRequest);
    }
}

public static class MmsDeleteNamedVariableListResponseDecoder
{
    public static MmsDeleteNamedVariableListResult Decode(ReadOnlyMemory<byte> presentationPayload, int expectedInvokeId, string dataSetReference)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail(dataSetReference, "Empty MMS DeleteNamedVariableList response payload.", hex);

            if (mms[0] == 0xA2)
                return Fail(dataSetReference, $"MMS Confirmed-Error PDU during DeleteNamedVariableList: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return Fail(dataSetReference, $"MMS Reject/Abort PDU during DeleteNamedVariableList: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] != 0xA1)
                return Fail(dataSetReference, $"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms[0]:X2}.", hex);

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
                return Fail(dataSetReference, "MMS DeleteNamedVariableList response PDU could not be decoded as BER.", hex);

            var children = BerReader.ReadChildren(outer.Value);
            if (children.Count == 0 || children[0].EncodedTag != 0x02)
                return Fail(dataSetReference, "MMS DeleteNamedVariableList response did not start with invokeID.", hex);

            var actualInvoke = BerReader.ReadUnsignedInteger(children[0]);
            if (actualInvoke != (ulong)expectedInvokeId)
                return Fail(dataSetReference, $"MMS DeleteNamedVariableList invokeID mismatch. Expected {expectedInvokeId}, received {actualInvoke}.", hex);

            var service = children.Skip(1).FirstOrDefault(x => x.EncodedTag == 0xAD || (x.Class == BerClass.ContextSpecific && x.TagNumber == 13));
            if (service.EncodedTag == 0)
                return Fail(dataSetReference, "MMS response has no DeleteNamedVariableList service response node [13].", hex);

            uint? matched = null;
            uint? deleted = null;
            foreach (var child in BerReader.ReadChildren(service.Value))
            {
                if (child.Class != BerClass.ContextSpecific)
                    continue;

                if (child.TagNumber == 0)
                    matched = BerReader.ReadUInt32(child);
                else if (child.TagNumber == 1)
                    deleted = BerReader.ReadUInt32(child);
            }

            var success = deleted.GetValueOrDefault() > 0;
            return new MmsDeleteNamedVariableListResult
            {
                IsSuccess = success,
                DataSetReference = dataSetReference,
                NumberMatched = matched,
                NumberDeleted = deleted,
                Message = success
                    ? $"MMS DeleteNamedVariableList deleted {deleted} of {matched ?? deleted} matched list(s) for {dataSetReference}."
                    : $"MMS DeleteNamedVariableList completed but deleted {deleted?.ToString() ?? "unknown"} of {matched?.ToString() ?? "unknown"} matched list(s) for {dataSetReference}.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail(dataSetReference, $"MMS DeleteNamedVariableList response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static MmsDeleteNamedVariableListResult Fail(string dataSetReference, string message, string hex)
        => new()
        {
            IsSuccess = false,
            DataSetReference = dataSetReference,
            Message = message,
            ResponseHexPreview = hex
        };
}

public static class MmsDefineNamedVariableListResponseDecoder
{
    public static MmsDefineNamedVariableListResult Decode(ReadOnlyMemory<byte> presentationPayload, int expectedInvokeId, string dataSetReference)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail(dataSetReference, "Empty MMS DefineNamedVariableList response payload.", hex);

            if (mms[0] == 0xA2)
                return Fail(dataSetReference, $"MMS Confirmed-Error PDU during DefineNamedVariableList: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return Fail(dataSetReference, $"MMS Reject/Abort PDU during DefineNamedVariableList: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] != 0xA1)
                return Fail(dataSetReference, $"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms[0]:X2}.", hex);

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
                return Fail(dataSetReference, "MMS DefineNamedVariableList response PDU could not be decoded as BER.", hex);

            var children = BerReader.ReadChildren(outer.Value);
            if (children.Count == 0 || children[0].EncodedTag != 0x02)
                return Fail(dataSetReference, "MMS DefineNamedVariableList response did not start with invokeID.", hex);

            var actualInvoke = BerReader.ReadUnsignedInteger(children[0]);
            if (actualInvoke != (ulong)expectedInvokeId)
                return Fail(dataSetReference, $"MMS DefineNamedVariableList invokeID mismatch. Expected {expectedInvokeId}, received {actualInvoke}.", hex);

            var service = children.Skip(1).FirstOrDefault(x => x.EncodedTag == 0x8B || x.EncodedTag == 0xAB || (x.Class == BerClass.ContextSpecific && x.TagNumber == 11));
            if (service.EncodedTag == 0)
                return Fail(dataSetReference, "MMS response has no DefineNamedVariableList service response node [11].", hex);

            if (service.Value.Length == 0)
            {
                return new MmsDefineNamedVariableListResult
                {
                    IsSuccess = true,
                    DataSetReference = dataSetReference,
                    Message = $"MMS DefineNamedVariableList succeeded for {dataSetReference}.",
                    ResponseHexPreview = hex
                };
            }

            return Fail(dataSetReference, $"MMS DefineNamedVariableList response node was present but not empty NULL: {HexDump.ToCompactString(service.Value.Span)}", hex);
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail(dataSetReference, $"MMS DefineNamedVariableList response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static MmsDefineNamedVariableListResult Fail(string dataSetReference, string message, string hex)
        => new()
        {
            IsSuccess = false,
            DataSetReference = dataSetReference,
            Message = message,
            ResponseHexPreview = hex
        };
}
