using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed class MmsDataSetDirectoryMember
{
    public string Domain { get; init; } = string.Empty;
    public string MmsItemName { get; init; } = string.Empty;
    public string MmsReference => string.IsNullOrWhiteSpace(Domain) ? MmsItemName : $"{Domain}/{MmsItemName}";
    public string UserReference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string DataObjectPath { get; init; } = string.Empty;
    public string Source { get; init; } = "GetNamedVariableListAttributes";
    public int Confidence { get; init; } = 100;

    public override string ToString()
        => string.IsNullOrWhiteSpace(FunctionalConstraint)
            ? MmsReference
            : $"{UserReference} [{FunctionalConstraint}] mms={MmsReference}";
}

public sealed class MmsDataSetDirectoryResult
{
    public bool IsSuccess { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string DataSetMmsName { get; init; } = string.Empty;
    public bool? IsDeletable { get; init; }
    public IReadOnlyList<MmsDataSetDirectoryMember> Members { get; init; } = Array.Empty<MmsDataSetDirectoryMember>();
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;

    public string Summary
    {
        get
        {
            var deletable = IsDeletable.HasValue ? IsDeletable.Value.ToString().ToLowerInvariant() : "unknown";
            return IsSuccess
                ? $"DataSet directory: {DataSetReference} members={Members.Count}, deletable={deletable}"
                : $"DataSet directory failed: {DataSetReference}: {Message}";
        }
    }
}

public static class MmsDataSetDirectoryRequest
{
    public static byte[] Build(int invokeId, string dataSetReference)
    {
        var (domain, itemName) = ParseDataSetReference(dataSetReference);
        return Build(invokeId, domain, itemName);
    }

    public static byte[] Build(int invokeId, string domain, string dataSetMmsName)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("DataSet domain/logical device is required.", nameof(domain));
        if (string.IsNullOrWhiteSpace(dataSetMmsName))
            throw new ArgumentException("DataSet MMS item name is required.", nameof(dataSetMmsName));

        // MMS GetNamedVariableListAttributes-Request ::= ObjectName.
        // ObjectName.domain-specific [1] is encoded as a constructed context-specific
        // object with <domainID, itemID>. This is the service behind IEC 61850
        // GetDataSetDirectory.
        var objectName = EncodeDomainSpecificObjectName(domain.Trim(), dataSetMmsName.Trim());
        var getNamedVariableListAttributes = BerWriter.EncodeTlv(0xAC, objectName);
        var confirmedRequest = BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(MmsPresentation.Integer(invokeId), getNamedVariableListAttributes));
        return MmsPresentation.WrapIsoPresentationPData(confirmedRequest);
    }

    internal static (string Domain, string ItemName) ParseDataSetReference(string dataSetReference)
    {
        if (string.IsNullOrWhiteSpace(dataSetReference))
            throw new ArgumentException("DataSet reference is empty.", nameof(dataSetReference));

        var normalized = dataSetReference.Trim();
        var slash = normalized.IndexOf('/');
        if (slash <= 0 || slash >= normalized.Length - 1)
            throw new ArgumentException("DataSet reference must use IEC 61850 form LD/LN.DataSetName.", nameof(dataSetReference));

        var domain = normalized[..slash];
        var item = normalized[(slash + 1)..].Replace('.', '$');
        if (!item.Contains('$', StringComparison.Ordinal))
            item = $"LLN0${item}";

        return (domain, item);
    }

    internal static byte[] EncodeDomainSpecificObjectName(string domain, string itemName)
        => BerWriter.EncodeTlv(0xA1, MmsPresentation.Concat(MmsPresentation.VisibleString(domain), MmsPresentation.VisibleString(itemName)));
}

public static class MmsDataSetDirectoryResponseDecoder
{
    public static MmsDataSetDirectoryResult Decode(
        ReadOnlyMemory<byte> presentationPayload,
        int expectedInvokeId,
        string dataSetReference,
        MmsIedModelDirectory? iedDirectory = null)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);
        var (domain, dataSetMmsName) = SafeParseDataSetReference(dataSetReference);

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail(dataSetReference, domain, dataSetMmsName, "Empty MMS GetNamedVariableListAttributes response payload.", hex);

            if (mms[0] == 0xA2)
                return Fail(dataSetReference, domain, dataSetMmsName, $"MMS Confirmed-Error PDU during DataSet directory: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return Fail(dataSetReference, domain, dataSetMmsName, $"MMS Reject/Abort PDU during DataSet directory: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] != 0xA1)
                return Fail(dataSetReference, domain, dataSetMmsName, $"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms[0]:X2}.", hex);

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
                return Fail(dataSetReference, domain, dataSetMmsName, "MMS Confirmed-Response PDU could not be decoded as BER.", hex);

            var children = BerReader.ReadChildren(outer.Value);
            if (children.Count == 0)
                return Fail(dataSetReference, domain, dataSetMmsName, "MMS Confirmed-Response PDU is empty.", hex);

            var invoke = children[0];
            if (invoke.EncodedTag != 0x02)
                return Fail(dataSetReference, domain, dataSetMmsName, $"DataSet directory response did not start with invokeID. First inner tag=0x{invoke.EncodedTag:X2}.", hex);

            var actualInvoke = BerReader.ReadUnsignedInteger(invoke);
            if (actualInvoke != (ulong)expectedInvokeId)
                return Fail(dataSetReference, domain, dataSetMmsName, $"DataSet directory invokeID mismatch. Expected {expectedInvokeId}, received {actualInvoke}.", hex);

            var service = children.Skip(1).FirstOrDefault(x => x.EncodedTag == 0xAC || (x.Class == BerClass.ContextSpecific && x.TagNumber == 12));
            if (service.EncodedTag == 0)
                return Fail(dataSetReference, domain, dataSetMmsName, "MMS response has no GetNamedVariableListAttributes service response node [12].", hex);

            bool? deletable = null;
            var rawMembers = new List<(string Domain, string Item)>();
            DecodeServiceResponse(service, rawMembers, ref deletable);

            var members = rawMembers
                .Where(x => !string.IsNullOrWhiteSpace(x.Domain) && !string.IsNullOrWhiteSpace(x.Item))
                .DistinctBy(x => $"{x.Domain}/{x.Item}", StringComparer.OrdinalIgnoreCase)
                .Select(x => NormalizeMember(x.Domain, x.Item, iedDirectory))
                .ToArray();

            return new MmsDataSetDirectoryResult
            {
                IsSuccess = true,
                DataSetReference = dataSetReference,
                Domain = domain,
                DataSetMmsName = dataSetMmsName,
                IsDeletable = deletable,
                Members = members,
                Message = $"MMS GetNamedVariableListAttributes decoded {members.Length} member(s), deletable={deletable?.ToString().ToLowerInvariant() ?? "unknown"}.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail(dataSetReference, domain, dataSetMmsName, $"DataSet directory response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static void DecodeServiceResponse(BerTlv service, List<(string Domain, string Item)> members, ref bool? deletable)
    {
        foreach (var child in BerReader.ReadChildren(service.Value))
        {
            if (child.EncodedTag == 0x80 && child.Value.Length == 1)
                deletable = child.Value.Span[0] != 0;

            CollectDomainSpecificObjectNames(child, members, depth: 0);
        }
    }

    private static void CollectDomainSpecificObjectNames(BerTlv tlv, List<(string Domain, string Item)> members, int depth)
    {
        if (depth > 32 || !tlv.Constructed)
            return;

        if (tlv.EncodedTag == 0xA1)
        {
            var ids = TryReadVisibleStringChildren(tlv.Value).ToArray();
            if (ids.Length >= 2)
            {
                var domain = ids[0];
                var item = ids[1];
                if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(item) && item.Contains('$', StringComparison.Ordinal))
                    members.Add((domain, item));
            }
        }

        foreach (var child in BerReader.ReadChildren(tlv.Value))
            CollectDomainSpecificObjectNames(child, members, depth + 1);
    }

    private static IEnumerable<string> TryReadVisibleStringChildren(ReadOnlyMemory<byte> buffer)
    {
        IReadOnlyList<BerTlv> children;
        try
        {
            children = BerReader.ReadChildren(buffer);
        }
        catch (BerFormatException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (child.EncodedTag is 0x1A or 0x16)
                yield return BerReader.ReadAsciiString(child);
        }
    }

    private static MmsDataSetDirectoryMember NormalizeMember(string domain, string itemName, MmsIedModelDirectory? iedDirectory)
    {
        var mmsReference = $"{domain}/{itemName}";
        if (iedDirectory != null && iedDirectory.TryFindByMmsReference(mmsReference, out var point))
        {
            return new MmsDataSetDirectoryMember
            {
                Domain = point.Domain,
                MmsItemName = point.MmsItemName,
                UserReference = point.UserReference,
                FunctionalConstraint = point.FunctionalConstraint,
                LogicalNode = point.LogicalNode,
                DataObjectPath = point.DataObjectPath,
                Source = "GetNamedVariableListAttributes+LiveMmsDirectory",
                Confidence = 100
            };
        }

        if (MmsIedModelDirectoryBuilder.TryParseLiveMmsVariable(domain, itemName, out var parsed))
        {
            return new MmsDataSetDirectoryMember
            {
                Domain = parsed.Domain,
                MmsItemName = parsed.MmsItemName,
                UserReference = parsed.UserReference,
                FunctionalConstraint = parsed.FunctionalConstraint,
                LogicalNode = parsed.LogicalNode,
                DataObjectPath = parsed.DataObjectPath,
                Source = "GetNamedVariableListAttributes",
                Confidence = 90
            };
        }

        return new MmsDataSetDirectoryMember
        {
            Domain = domain,
            MmsItemName = itemName,
            UserReference = $"{domain}/{itemName.Replace('$', '.')}",
            Source = "GetNamedVariableListAttributesRaw",
            Confidence = 50
        };
    }

    private static (string Domain, string DataSetMmsName) SafeParseDataSetReference(string dataSetReference)
    {
        try
        {
            return MmsDataSetDirectoryRequest.ParseDataSetReference(dataSetReference);
        }
        catch (ArgumentException)
        {
            return (string.Empty, string.Empty);
        }
    }

    private static MmsDataSetDirectoryResult Fail(string dataSetReference, string domain, string dataSetMmsName, string message, string hex)
        => new()
        {
            IsSuccess = false,
            DataSetReference = dataSetReference,
            Domain = domain,
            DataSetMmsName = dataSetMmsName,
            Message = message,
            ResponseHexPreview = hex
        };
}
