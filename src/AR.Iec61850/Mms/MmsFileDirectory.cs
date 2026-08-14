using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed class MmsFileDirectoryEntry
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    /// <summary>
    /// Exact single GraphicString returned by the IED for this FileDirectory entry.
    /// This is intentionally not normalized: leading separators, slash direction,
    /// and case are preserved. It is empty when the MMS FileName used multiple
    /// GraphicString components because no single raw string existed on the wire.
    /// </summary>
    public string RawName { get; init; } = string.Empty;
    public IReadOnlyList<string> RawNameComponents { get; init; } = Array.Empty<string>();
    public uint? SizeBytes { get; init; }
    public byte[] LastModifiedRaw { get; init; } = Array.Empty<byte>();
    public string LastModifiedDisplay => LastModifiedRaw.Length == 0 ? string.Empty : Convert.ToHexString(LastModifiedRaw);

    // Several protection relays expose disturbance packages with no filename extension
    // (for example FRA00019). A non-zero declared size proves such an entry is a file,
    // not a directory. Directories are accepted when the server uses a trailing separator
    // or reports an extensionless zero/unknown-size entry.
    public bool IsLikelyDirectory
    {
        get
        {
            var hasDirectoryTerminator =
                Name.EndsWith('/') || Name.EndsWith('\\') ||
                Path.EndsWith('/') || Path.EndsWith('\\');
            if (hasDirectoryTerminator)
                return true;

            return string.IsNullOrWhiteSpace(System.IO.Path.GetExtension(Name)) &&
                   (!SizeBytes.HasValue || SizeBytes.Value == 0);
        }
    }
}

public sealed class MmsFileDirectoryResult
{
    public bool IsSuccess { get; init; }
    public string DirectoryName { get; init; } = string.Empty;
    public string ContinueAfter { get; init; } = string.Empty;
    public IReadOnlyList<MmsFileDirectoryEntry> Entries { get; init; } = Array.Empty<MmsFileDirectoryEntry>();
    public bool MoreFollows { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;

    public string Summary => IsSuccess
        ? $"FileDirectory: dir='{(string.IsNullOrWhiteSpace(DirectoryName) ? "/" : DirectoryName)}' entries={Entries.Count}, moreFollows={MoreFollows}"
        : $"FileDirectory failed: dir='{(string.IsNullOrWhiteSpace(DirectoryName) ? "/" : DirectoryName)}': {Message}";
}

public static class MmsFileDirectoryRequest
{
    public static byte[] Build(int invokeId, string? directoryName = null, string? continueAfter = null)
    {
        var body = Array.Empty<byte>();
        if (!IsRootFileSpecification(directoryName))
            body = MmsPresentation.Concat(body, BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: true, 0, EncodeFileNameContent(directoryName!)));

        if (!IsRootFileSpecification(continueAfter))
            body = MmsPresentation.Concat(body, BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: true, 1, EncodeFileNameContent(continueAfter!)));

        // ConfirmedServiceRequest.fileDirectory is context-specific tag [77] in ISO 9506 MMS.
        var fileDirectory = BerWriter.EncodeTlv(BerClass.ContextSpecific, constructed: true, 77, body);
        var confirmedRequest = BerWriter.EncodeTlv(0xA0, MmsPresentation.Concat(MmsPresentation.Integer(invokeId), fileDirectory));
        return MmsPresentation.WrapIsoPresentationPData(confirmedRequest);
    }

    private static bool IsRootFileSpecification(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Trim().Replace('\\', '/');
        return normalized is "/" or "*";
    }

    private static byte[] EncodeFileNameContent(string value)
    {
        var normalized = NormalizeFileSpecification(value);

        // FileName is a SEQUENCE OF GraphicString. Real IEDs commonly expose the complete
        // case-sensitive path in one GraphicString with '/' separators, so preserve that
        // representation rather than splitting each directory into a separate element.
        return BerWriter.EncodeTlv(0x19, BerWriter.EncodeAscii(normalized));
    }

    private static string NormalizeFileSpecification(string value)
    {
        var segments = (value ?? string.Empty)
            .Trim()
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            throw new ArgumentException("MMS file specification has no usable path segment.", nameof(value));
        if (segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException("MMS file specification contains a traversal segment.", nameof(value));
        return string.Join('/', segments);
    }
}

public static class MmsFileDirectoryResponseDecoder
{
    public static MmsFileDirectoryResult Decode(
        ReadOnlyMemory<byte> presentationPayload,
        int expectedInvokeId,
        string? directoryName = null,
        string? continueAfter = null)
    {
        var dir = directoryName?.Trim() ?? string.Empty;
        var continuation = continueAfter?.Trim() ?? string.Empty;
        var hex = HexDump.ToCompactString(presentationPayload.Span);

        try
        {
            var mms = MmsPresentation.StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail(dir, continuation, "Empty MMS FileDirectory response payload.", hex);

            if (mms[0] == 0xA2)
                return Fail(dir, continuation, $"MMS Confirmed-Error PDU during FileDirectory: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return Fail(dir, continuation, $"MMS Reject/Abort PDU during FileDirectory: {HexDump.ToCompactString(mms)}", hex);

            if (mms[0] != 0xA1)
                return Fail(dir, continuation, $"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms[0]:X2}.", hex);

            var offset = 0;
            if (!BerReader.TryReadTlv(mms, ref offset, out var outer))
                return Fail(dir, continuation, "MMS Confirmed-Response PDU could not be decoded as BER.", hex);

            var children = BerReader.ReadChildren(outer.Value);
            if (children.Count == 0)
                return Fail(dir, continuation, "MMS Confirmed-Response PDU is empty.", hex);

            var invoke = children[0];
            if (invoke.EncodedTag != 0x02)
                return Fail(dir, continuation, $"FileDirectory response did not start with invokeID. First inner tag=0x{invoke.EncodedTag:X2}.", hex);

            var actualInvoke = BerReader.ReadUnsignedInteger(invoke);
            if (actualInvoke != (ulong)expectedInvokeId)
                return Fail(dir, continuation, $"FileDirectory invokeID mismatch. Expected {expectedInvokeId}, received {actualInvoke}.", hex);

            var service = children.Skip(1).FirstOrDefault(x => x.Class == BerClass.ContextSpecific && x.TagNumber == 77);
            if (service.EncodedTag == 0)
                return Fail(dir, continuation, "MMS response has no FileDirectory service response node [77].", hex);

            var entries = new List<MmsFileDirectoryEntry>();
            var moreFollows = false;
            DecodeServiceResponse(service, dir, entries, ref moreFollows);
            var distinctEntries = entries
                .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new MmsFileDirectoryResult
            {
                IsSuccess = true,
                DirectoryName = dir,
                ContinueAfter = continuation,
                Entries = distinctEntries,
                MoreFollows = moreFollows,
                Message = $"MMS FileDirectory decoded {distinctEntries.Length} entr(y/ies), moreFollows={moreFollows}.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail(dir, continuation, $"FileDirectory response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static void DecodeServiceResponse(
        BerTlv service,
        string directoryName,
        List<MmsFileDirectoryEntry> entries,
        ref bool moreFollows)
    {
        foreach (var child in BerReader.ReadChildren(service.Value))
        {
            if (child.Class == BerClass.ContextSpecific && child.TagNumber == 0 && child.Constructed)
            {
                CollectDirectoryEntries(child, directoryName, entries, depth: 0);
            }
            else if (child.Class == BerClass.ContextSpecific && child.TagNumber == 1 && child.Value.Length > 0)
            {
                moreFollows = BerReader.ReadBoolean(child) ?? child.Value.Span[0] != 0;
            }
        }
    }

    private static void CollectDirectoryEntries(
        BerTlv container,
        string directoryName,
        List<MmsFileDirectoryEntry> entries,
        int depth)
    {
        if (!container.Constructed || depth > 6)
            return;

        foreach (var child in BerReader.ReadChildren(container.Value))
        {
            if (LooksLikeDirectoryEntry(child))
            {
                var decoded = DecodeDirectoryEntry(child, directoryName);
                if (decoded != null)
                    entries.Add(decoded);
            }
            else if (child.Constructed)
            {
                // Some stacks encode listOfDirectoryEntry as [0] -> SEQUENCE ->
                // DirectoryEntry SEQUENCE(s), while others place the DirectoryEntry
                // SEQUENCE(s) directly under [0]. Accept both without collapsing all
                // filenames into one synthetic entry.
                CollectDirectoryEntries(child, directoryName, entries, depth + 1);
            }
        }
    }

    private static bool LooksLikeDirectoryEntry(BerTlv candidate)
    {
        if (!candidate.Constructed)
            return false;

        var fields = BerReader.ReadChildren(candidate.Value);
        return fields.Any(field =>
            field.Class == BerClass.ContextSpecific &&
            field.TagNumber == 0 &&
            field.Constructed);
    }

    private static MmsFileDirectoryEntry? DecodeDirectoryEntry(BerTlv entry, string directoryName)
    {
        if (!entry.Constructed)
            return null;

        IReadOnlyList<string> fileNameComponents = Array.Empty<string>();
        uint? size = null;
        byte[] modified = Array.Empty<byte>();

        foreach (var field in BerReader.ReadChildren(entry.Value))
        {
            if (field.Class == BerClass.ContextSpecific && field.TagNumber == 0 && field.Constructed)
            {
                fileNameComponents = DecodeFileNameComponents(field);
            }
            else if (field.Class == BerClass.ContextSpecific && field.TagNumber == 1 && field.Constructed)
            {
                foreach (var attr in BerReader.ReadChildren(field.Value))
                {
                    if (attr.Class == BerClass.ContextSpecific && attr.TagNumber == 0)
                        size = BerReader.ReadUInt32(attr);
                    else if (attr.Class == BerClass.ContextSpecific && attr.TagNumber == 1)
                        modified = attr.Value.ToArray();
                }
            }
        }

        var normalizedName = NormalizeReturnedPath(NormalizeFileNameComponents(fileNameComponents));
        if (string.IsNullOrWhiteSpace(normalizedName))
            return null;

        var rawName = fileNameComponents.Count == 1
            ? fileNameComponents[0]
            : string.Empty;
        var path = CombinePath(directoryName, normalizedName);
        return new MmsFileDirectoryEntry
        {
            Name = normalizedName,
            Path = path,
            RawName = rawName,
            RawNameComponents = fileNameComponents.ToArray(),
            SizeBytes = size,
            LastModifiedRaw = modified
        };
    }

    private static IReadOnlyList<string> DecodeFileNameComponents(BerTlv tlv)
    {
        if (!tlv.Constructed)
            return Array.Empty<string>();

        var parts = new List<string>();
        CollectGraphicStrings(tlv, parts, depth: 0);
        return parts.ToArray();
    }

    private static string NormalizeFileNameComponents(IReadOnlyList<string> parts)
        => string.Join('/', parts
            .Select(part => part.Trim().Replace('\\', '/'))
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    private static void CollectGraphicStrings(BerTlv tlv, List<string> parts, int depth)
    {
        if (depth > 8 || !tlv.Constructed)
            return;

        foreach (var child in BerReader.ReadChildren(tlv.Value))
        {
            if (child.EncodedTag is 0x19 or 0x1A or 0x16)
                parts.Add(BerReader.ReadAsciiString(child));
            else if (child.Constructed)
                CollectGraphicStrings(child, parts, depth + 1);
        }
    }

    private static string CombinePath(string directoryName, string fileName)
    {
        var dir = NormalizeReturnedPath(directoryName);
        var name = NormalizeReturnedPath(fileName);
        if (string.IsNullOrWhiteSpace(dir))
            return name;
        if (string.IsNullOrWhiteSpace(name))
            return dir;

        if (name.Equals(dir, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return $"{dir}/{name}";
    }

    private static string NormalizeReturnedPath(string? value)
        => string.Join('/', (value ?? string.Empty)
            .Trim()
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static MmsFileDirectoryResult Fail(string directoryName, string continueAfter, string message, string hex)
        => new()
        {
            IsSuccess = false,
            DirectoryName = directoryName,
            ContinueAfter = continueAfter,
            Entries = Array.Empty<MmsFileDirectoryEntry>(),
            MoreFollows = false,
            Message = message,
            ResponseHexPreview = hex
        };
}
