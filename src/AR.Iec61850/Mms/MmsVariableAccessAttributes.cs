using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed class MmsTypeSpecificationNode
{
    public string Name { get; init; } = string.Empty;
    public string MmsType { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
    public int? Size { get; init; }
    public string Detail { get; init; } = string.Empty;
    public IReadOnlyList<MmsTypeSpecificationNode> Children { get; init; } = Array.Empty<MmsTypeSpecificationNode>();

    public string Signature
    {
        get
        {
            if (Children.Count == 0)
                return string.IsNullOrWhiteSpace(Name) ? MmsType : $"{Name}:{MmsType}";

            var prefix = string.IsNullOrWhiteSpace(Name) ? MmsType : $"{Name}:{MmsType}";
            return $"{prefix}({string.Join(",", Children.Select(x => x.Signature))})";
        }
    }
}

public sealed class MmsVariableAccessAttributesResult
{
    public bool IsSuccess { get; init; }
    public MmsObjectReference Reference { get; init; }
    public bool? IsMmsDeletable { get; init; }
    public MmsTypeSpecificationNode? TypeSpecification { get; init; }
    public string Source { get; init; } = "GetVariableAccessAttributes";
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;

    public string ReferenceKey => string.IsNullOrWhiteSpace(Reference.Domain) ? Reference.Item : $"{Reference.Domain}/{Reference.Item}";
    public string SclBType => TypeSpecification?.SclBType ?? string.Empty;
    public string MmsType => TypeSpecification?.MmsType ?? string.Empty;
    public string TypeSignature => TypeSpecification?.Signature ?? string.Empty;

    public string Summary => IsSuccess
        ? $"VariableAccessAttributes: {ReferenceKey} type={MmsType} sclBType={SclBType} deletable={FormatBool(IsMmsDeletable)}."
        : $"VariableAccessAttributes failed: {ReferenceKey}: {Message}";

    private static string FormatBool(bool? value)
        => value.HasValue ? value.Value.ToString().ToLowerInvariant() : "unknown";
}

public static class MmsVariableAccessAttributesRequest
{
    public static byte[] Build(int invokeId, MmsObjectReference reference)
    {
        if (string.IsNullOrWhiteSpace(reference.Domain))
            throw new ArgumentException("MMS domain is empty. Use a domain-specific IEC 61850 object reference.", nameof(reference));

        if (string.IsNullOrWhiteSpace(reference.Item))
            throw new ArgumentException("MMS item name is empty.", nameof(reference));

        var objectName = MmsDataSetDirectoryRequest.EncodeDomainSpecificObjectName(reference.Domain.Trim(), reference.Item.Trim());
        // GetVariableAccessAttributes-Request is a CHOICE. A named variable
        // is encoded as name [0] followed by ObjectName, not ObjectName alone.
        var namedVariable = BerWriter.EncodeTlv(0xA0, objectName);
        var getVariableAccessAttributes = BerWriter.EncodeTlv(0xA6, namedVariable);
        var confirmedRequest = BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(MmsPresentation.Integer(invokeId), getVariableAccessAttributes));
        return MmsPresentation.WrapIsoPresentationPData(confirmedRequest);
    }
}

public static class MmsVariableAccessAttributesResponseDecoder
{
    public static MmsVariableAccessAttributesResult Decode(
        ReadOnlyMemory<byte> presentationPayload,
        int expectedInvokeId,
        MmsObjectReference reference)
    {
        var hex = HexDump.ToCompactString(presentationPayload.Span);

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail(reference, "Empty MMS GetVariableAccessAttributes response payload.", hex);

            if (mms[0] == 0xA2)
                return Fail(reference, $"MMS Confirmed-Error PDU during GetVariableAccessAttributes: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return Fail(reference, $"MMS Reject/Abort PDU during GetVariableAccessAttributes: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] != 0xA1)
                return Fail(reference, $"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms[0]:X2}.", hex);

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
                return Fail(reference, "MMS Confirmed-Response PDU could not be decoded as BER.", hex);

            var children = BerReader.ReadChildren(outer.Value);
            if (children.Count == 0)
                return Fail(reference, "MMS Confirmed-Response PDU is empty.", hex);

            var invoke = children[0];
            if (invoke.EncodedTag != 0x02)
                return Fail(reference, $"GetVariableAccessAttributes response did not start with invokeID. First inner tag=0x{invoke.EncodedTag:X2}.", hex);

            var actualInvoke = BerReader.ReadUnsignedInteger(invoke);
            if (actualInvoke != (ulong)expectedInvokeId)
                return Fail(reference, $"GetVariableAccessAttributes invokeID mismatch. Expected {expectedInvokeId}, received {actualInvoke}.", hex);

            var service = children.Skip(1).FirstOrDefault(x => x.EncodedTag == 0xA6 || (x.Class == BerClass.ContextSpecific && x.TagNumber == 6));
            if (service.EncodedTag == 0)
                return Fail(reference, "MMS response has no GetVariableAccessAttributes service response node [6].", hex);

            IReadOnlyList<BerTlv> serviceChildren = service.Constructed ? BerReader.ReadChildren(service.Value) : Array.Empty<BerTlv>();
            bool? deletable = null;
            MmsTypeSpecificationNode? type = null;

            foreach (var child in serviceChildren)
            {
                if (child.Class == BerClass.ContextSpecific && child.TagNumber == 0 && child.Value.Length == 1)
                {
                    deletable = child.Value.Span[0] != 0;
                    continue;
                }

                if (TryDecodeExplicitTypeDescription(child, out var explicitType))
                {
                    type = explicitType;
                    break;
                }

                if (TryDecodeTypeSpecification(child, string.Empty, out var childType))
                {
                    type = childType;
                    break;
                }
            }

            if (type == null)
            {
                foreach (var child in serviceChildren)
                {
                    type = FindFirstTypeSpecification(child);
                    if (type != null)
                        break;
                }
            }

            if (type == null)
                return Fail(reference, "GetVariableAccessAttributes response did not contain a decodable TypeSpecification.", hex);

            return new MmsVariableAccessAttributesResult
            {
                IsSuccess = true,
                Reference = reference,
                IsMmsDeletable = deletable,
                TypeSpecification = type,
                Message = $"MMS GetVariableAccessAttributes decoded type={type.MmsType}, sclBType={type.SclBType}.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail(reference, $"GetVariableAccessAttributes response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static bool TryDecodeExplicitTypeDescription(BerTlv field, out MmsTypeSpecificationNode type)
    {
        type = default!;
        if (field.Class != BerClass.ContextSpecific || field.TagNumber != 2 || !field.Constructed)
            return false;

        var children = BerReader.ReadChildren(field.Value);
        if (children.Count != 1)
            return false;

        return TryDecodeTypeSpecification(children[0], string.Empty, out type);
    }

    internal static bool TryDecodeTypeSpecification(BerTlv tlv, string componentName, out MmsTypeSpecificationNode node)
    {
        node = default!;
        if (tlv.Class != BerClass.ContextSpecific)
            return false;

        switch (tlv.TagNumber)
        {
            case 1:
                if (!tlv.Constructed)
                    return false;

                node = DecodeArrayType(tlv, componentName);
                return true;
            case 2:
                if (!tlv.Constructed)
                    return false;

                node = DecodeStructureType(tlv, componentName);
                return true;
            case 3:
                node = Basic(componentName, "boolean", "BOOLEAN", tlv);
                return true;
            case 4:
                node = Basic(componentName, "bit-string", "Check", tlv);
                return true;
            case 5:
                node = Basic(componentName, "integer", "INT32", tlv);
                return true;
            case 6:
                node = Basic(componentName, "unsigned", "INT32U", tlv);
                return true;
            case 7:
                node = Basic(componentName, "floating-point", "FLOAT32", tlv);
                return true;
            case 9:
                node = Basic(componentName, "octet-string", "Octet64", tlv);
                return true;
            case 10:
                node = Basic(componentName, "visible-string", "VisString255", tlv);
                return true;
            case 12:
                node = Basic(componentName, "binary-time", "Timestamp", tlv);
                return true;
            case 13:
                node = Basic(componentName, "bcd", "INT32", tlv);
                return true;
            case 14:
                node = Basic(componentName, "boolean-array", "Check", tlv);
                return true;
            case 15:
                node = Basic(componentName, "object-id", "ObjRef", tlv);
                return true;
            case 16:
                node = Basic(componentName, "mms-string", "Unicode255", tlv);
                return true;
            case 17:
                node = Basic(componentName, "utc-time", "Timestamp", tlv);
                return true;
            default:
                return false;
        }
    }

    private static MmsTypeSpecificationNode? FindFirstTypeSpecification(BerTlv tlv)
    {
        if (TryDecodeTypeSpecification(tlv, string.Empty, out var direct))
            return direct;

        if (!tlv.Constructed)
            return null;

        foreach (var child in BerReader.ReadChildren(tlv.Value))
        {
            var found = FindFirstTypeSpecification(child);
            if (found != null)
                return found;
        }

        return null;
    }

    private static MmsTypeSpecificationNode DecodeArrayType(BerTlv tlv, string componentName)
    {
        IReadOnlyList<BerTlv> children = tlv.Constructed ? BerReader.ReadChildren(tlv.Value) : Array.Empty<BerTlv>();
        int? count = null;
        MmsTypeSpecificationNode? element = null;

        foreach (var child in children)
        {
            if (child.Class == BerClass.ContextSpecific && child.TagNumber == 1)
            {
                var parsed = BerReader.ReadUnsignedInteger(child);
                if (parsed.HasValue && parsed.Value <= int.MaxValue)
                    count = (int)parsed.Value;
            }
            else if (child.Class == BerClass.ContextSpecific && child.TagNumber == 2)
            {
                element = child.Constructed ? DecodeWrappedType(child, "element") : null;
            }
            else if (TryDecodeTypeSpecification(child, "element", out var direct))
            {
                element = direct;
            }
        }

        return new MmsTypeSpecificationNode
        {
            Name = componentName,
            MmsType = "array",
            SclBType = "Struct",
            Size = count,
            Detail = count.HasValue ? $"elements={count.Value}" : string.Empty,
            Children = element == null ? Array.Empty<MmsTypeSpecificationNode>() : new[] { element }
        };
    }

    private static MmsTypeSpecificationNode DecodeStructureType(BerTlv tlv, string componentName)
    {
        IReadOnlyList<BerTlv> children = tlv.Constructed ? BerReader.ReadChildren(tlv.Value) : Array.Empty<BerTlv>();
        var components = new List<MmsTypeSpecificationNode>();
        var anonymousIndex = 0;

        foreach (var child in children)
        {
            if (child.Class == BerClass.ContextSpecific && child.TagNumber == 1 && child.Constructed)
            {
                foreach (var componentSequence in BerReader.ReadChildren(child.Value))
                {
                    var component = DecodeStructureComponent(componentSequence, anonymousIndex++);
                    if (component != null)
                        components.Add(component);
                }
            }
            else
            if (child.EncodedTag == 0x30 || child.Class == BerClass.Universal && child.Constructed)
            {
                var component = DecodeStructureComponent(child, anonymousIndex++);
                if (component != null)
                    components.Add(component);
            }
            else if (TryDecodeTypeSpecification(child, $"[{anonymousIndex++}]", out var direct))
            {
                components.Add(direct);
            }
        }

        return new MmsTypeSpecificationNode
        {
            Name = componentName,
            MmsType = "structure",
            SclBType = "Struct",
            Size = components.Count,
            Detail = $"components={components.Count}",
            Children = components.ToArray()
        };
    }

    private static MmsTypeSpecificationNode? DecodeStructureComponent(BerTlv componentSequence, int anonymousIndex)
    {
        if (!componentSequence.Constructed)
            return null;

        var fields = BerReader.ReadChildren(componentSequence.Value);
        var name = string.Empty;
        MmsTypeSpecificationNode? type = null;

        foreach (var field in fields)
        {
            if (field.Class == BerClass.ContextSpecific && field.TagNumber == 0)
            {
                name = ReadComponentName(field);
            }
            else if (field.Class == BerClass.ContextSpecific && field.TagNumber == 1)
            {
                type = DecodeWrappedType(field, name);
            }
            else if (TryDecodeTypeSpecification(field, name, out var direct))
            {
                type = direct;
            }
        }

        if (type == null)
            return null;

        if (string.IsNullOrWhiteSpace(type.Name))
        {
            type = new MmsTypeSpecificationNode
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"[{anonymousIndex}]" : name,
                MmsType = type.MmsType,
                SclBType = type.SclBType,
                Size = type.Size,
                Detail = type.Detail,
                Children = type.Children
            };
        }

        return type;
    }

    private static MmsTypeSpecificationNode? DecodeWrappedType(BerTlv wrapper, string componentName)
    {
        if (!wrapper.Constructed)
            return null;

        foreach (var child in BerReader.ReadChildren(wrapper.Value))
        {
            if (TryDecodeTypeSpecification(child, componentName, out var type))
                return type;
        }

        return null;
    }

    private static string ReadComponentName(BerTlv field)
    {
        if (!field.Constructed)
        {
            if (field.Value.Length == 0)
                return string.Empty;

            return System.Text.Encoding.ASCII.GetString(field.Value.Span);
        }

        foreach (var child in BerReader.ReadChildren(field.Value))
        {
            if (child.EncodedTag is 0x1A or 0x16 or 0x80)
                return BerReader.ReadAsciiString(child);
        }

        return string.Empty;
    }

    private static MmsTypeSpecificationNode Basic(string componentName, string mmsType, string sclBType, BerTlv tlv)
        => new()
        {
            Name = componentName,
            MmsType = mmsType,
            SclBType = sclBType,
            Size = TryReadSize(tlv),
            Detail = FormatDetail(tlv)
        };

    private static int? TryReadSize(BerTlv tlv)
    {
        if (tlv.Value.IsEmpty || tlv.Value.Length > 4)
            return null;

        var parsed = BerReader.ReadUnsignedInteger(tlv);
        return parsed.HasValue && parsed.Value <= int.MaxValue ? (int)parsed.Value : null;
    }

    private static string FormatDetail(BerTlv tlv)
    {
        var size = TryReadSize(tlv);
        return size.HasValue ? $"size={size.Value}" : string.Empty;
    }

    private static MmsVariableAccessAttributesResult Fail(MmsObjectReference reference, string message, string hex)
        => new()
        {
            IsSuccess = false,
            Reference = reference,
            Message = message,
            ResponseHexPreview = hex
        };
}
