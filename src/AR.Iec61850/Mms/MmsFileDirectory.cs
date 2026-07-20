using AR.Iec61850.Asn1;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed class MmsFileDirectoryEntry
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
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
        var normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 && !string.IsNullOrWhiteSpace(normalized))
            parts = new[] { normalized };

        var body = Array.Empty<byte>();
        foreach (var part in parts)
            body = MmsPresentation.Concat(body, BerWriter.EncodeTlv(0x19, BerWriter.EncodeAscii(part)));

        return body;
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

            return new MmsFileDirectoryResult
            {
                IsSuccess = true,
                DirectoryName = dir,
                ContinueAfter = continuation,
                Entries = entries
                    .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                MoreFollows = moreFollows,
                Message = $"MMS FileDirectory decoded {entries.Count} entr(y/ies), moreFollows={moreFollows}.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is BerFormatException or ArgumentException or InvalidOperationException)
        {
            return Fail(dir, continuation, $"FileDirectory response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static void DecodeServiceResponse(BerTlv service, string directoryName, List<MmsFileDirectoryEntry> entries, ref bool moreFollows)
    {
        foreach (var child in BerReader.ReadChildren(service.Value))
        {
            if (child.Class == BerClass.ContextSpecific && child.TagNumber == 0 && child.Constructed)
            {
                foreach (var entry in BerReader.ReadChildren(child.Value))
                {
                    var decoded = DecodeDirectoryEntry(entry, directoryName);
                    if (decoded != null)
                        entries.Add(decoded);
                }
            }
            else if (child.Class == BerClass.ContextSpecific && child.TagNumber == 1 && child.Value.Length > 0)
            {
                moreFollows = child.Value.Span[0] != 0;
            }
        }
    }

    private static MmsFileDirectoryEntry? DecodeDirectoryEntry(BerTlv entry, string directoryName)
    {
        if (!entry.Constructed)
            return null;

        string? fileName = null;
        uint? size = null;
        byte[] modified = Array.Empty<byte>();

        foreach (var field in BerReader.ReadChildren(entry.Value))
        {
            if (field.EncodedTag == 0x30 || field.Constructed)
            {
                var candidate = DecodeFileName(field);
                if (!string.IsNullOrWhiteSpace(candidate))
                    fileName ??= candidate;
            }

            if (field.Class == BerClass.ContextSpecific && field.TagNumber == 1 && field.Constructed)
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

        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var path = CombinePath(directoryName, fileName);
        return new MmsFileDirectoryEntry
        {
            Name = fileName,
            Path = path,
            SizeBytes = size,
            LastModifiedRaw = modified
        };
    }

    private static string DecodeFileName(BerTlv tlv)
    {
        if (!tlv.Constructed)
            return string.Empty;

        var parts = new List<string>();
        CollectGraphicStrings(tlv, parts, depth: 0);
        return string.Join('/', parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

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
        if (string.IsNullOrWhiteSpace(directoryName))
            return fileName;

        var dir = directoryName.Trim().TrimEnd('/', '\\');
        var name = fileName.Trim().TrimStart('/', '\\');
        return string.IsNullOrWhiteSpace(dir) ? name : $"{dir}/{name}";
    }

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
