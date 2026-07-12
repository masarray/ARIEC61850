using AR.Iec61850.Mms;

namespace AR.Iec61850.Control;

internal sealed class Iec61850ControlSequenceContext
{
    public required Iec61850ControlRequest Request { get; init; }
    public required MmsDataValue CtlVal { get; init; }
    public required byte ControlNumber { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string Fingerprint { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

internal static class Iec61850ControlStructureBuilder
{
    private static readonly DateTimeOffset BinaryTimeEpoch = new(1984, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static Iec61850ControlSequenceContext CreateContext(
        Iec61850ControlRequest request,
        MmsTypeSpecificationNode ctlValSpecification,
        byte generatedControlNumber,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Iec61850ControlSequenceContext
        {
            Request = request,
            CtlVal = Iec61850ControlValueBinder.Bind(request.ControlValue, ctlValSpecification),
            ControlNumber = request.ControlNumber ?? generatedControlNumber,
            TimestampUtc = timestampUtc.ToUniversalTime(),
            Fingerprint = request.SequenceFingerprint
        };
    }

    public static MmsDataValue BuildOperate(
        Iec61850ControlSequenceContext context,
        MmsTypeSpecificationNode specification,
        bool requireExactNamedFields)
        => BuildServiceValue(
            context,
            specification,
            requireExactNamedFields,
            serviceName: "Oper",
            requiredFields: new[] { "ctlval", "origin", "ctlnum", "t", "test", "check" },
            optionalFields: new[] { "opertm" });

    public static MmsDataValue BuildSelectWithValue(
        Iec61850ControlSequenceContext context,
        MmsTypeSpecificationNode specification,
        bool requireExactNamedFields)
        => BuildServiceValue(
            context,
            specification,
            requireExactNamedFields,
            serviceName: "SBOw",
            requiredFields: new[] { "ctlval", "origin", "ctlnum", "t", "test", "check" },
            optionalFields: new[] { "opertm" });

    public static MmsDataValue BuildCancel(
        Iec61850ControlSequenceContext context,
        MmsTypeSpecificationNode specification,
        bool requireExactNamedFields)
        => BuildServiceValue(
            context,
            specification,
            requireExactNamedFields,
            serviceName: "Cancel",
            requiredFields: new[] { "ctlval", "origin", "ctlnum", "t", "test" },
            optionalFields: new[] { "check" });

    private static MmsDataValue BuildServiceValue(
        Iec61850ControlSequenceContext context,
        MmsTypeSpecificationNode specification,
        bool requireExactNamedFields,
        string serviceName,
        IReadOnlyCollection<string> requiredFields,
        IReadOnlyCollection<string> optionalFields)
    {
        if (!Normalize(specification.MmsType).Equals("structure", StringComparison.Ordinal))
            throw new InvalidOperationException($"{serviceName} must be an MMS structure, but live type is '{specification.MmsType}'.");

        if (specification.Children.Count == 0)
            throw new InvalidOperationException($"{serviceName} has no live component specification.");

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<MmsDataValue>(specification.Children.Count);

        foreach (var child in specification.Children)
        {
            var name = NormalizeName(child.Name);
            MmsDataValue value = name switch
            {
                "ctlval" => ValidateAndReturn(context.CtlVal, child, "ctlVal"),
                "opertm" => BuildTimestamp(context.Request.OperateAtUtc ?? DateTimeOffset.UnixEpoch, child),
                "origin" => BuildOrigin(context.Request.Origin, child),
                "ctlnum" => BuildControlNumber(context.ControlNumber, child),
                "t" => BuildTimestamp(context.TimestampUtc, child),
                "test" => BuildBoolean(context.Request.Test, child, "Test"),
                "check" => BuildCheck(context.Request.SynchroCheck, context.Request.InterlockCheck, child),
                _ when optionalFields.Contains(name, StringComparer.OrdinalIgnoreCase) => DefaultFieldValue(child),
                _ => throw new NotSupportedException($"Unsupported {serviceName} component '{child.Name}' ({child.MmsType}). Refusing to guess a vendor-specific command structure.")
            };
            found.Add(name);
            values.Add(value);
        }

        if (requireExactNamedFields)
        {
            var missing = requiredFields.Where(x => !found.Contains(x)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"{serviceName} live specification is missing required named field(s): {string.Join(", ", missing)}.");
        }

        var structure = MmsDataValue.Structure(values);
        Iec61850ControlValueBinder.Validate(structure, specification, serviceName);
        return structure;
    }

    private static MmsDataValue DefaultFieldValue(MmsTypeSpecificationNode specification)
        => Normalize(specification.MmsType) switch
        {
            "boolean" => MmsDataValue.Boolean(false),
            "bit-string" => BuildZeroBitString(specification.Size),
            "integer" or "bcd" => MmsDataValue.Integer(0),
            "unsigned" => MmsDataValue.Unsigned(0),
            "utc-time" => MmsDataValue.UtcTime(new Iec61850UtcTime(DateTimeOffset.UnixEpoch, 0)),
            "binary-time" => MmsDataValue.BinaryTime(BuildBinaryTime(DateTimeOffset.UnixEpoch, specification.Size)),
            _ => throw new NotSupportedException($"No deterministic default exists for optional control field '{specification.Name}' ({specification.MmsType}).")
        };

    private static MmsDataValue BuildZeroBitString(int? requestedBitCount)
    {
        var bitCount = requestedBitCount.GetValueOrDefault(1);
        if (bitCount is <= 0 or > 1024)
            bitCount = 1;
        var byteCount = (bitCount + 7) / 8;
        return MmsDataValue.BitString((byte)(byteCount * 8 - bitCount), new byte[byteCount]);
    }

    private static MmsDataValue ValidateAndReturn(MmsDataValue value, MmsTypeSpecificationNode specification, string path)
    {
        Iec61850ControlValueBinder.Validate(value, specification, path);
        return value;
    }

    private static MmsDataValue BuildOrigin(Iec61850Origin origin, MmsTypeSpecificationNode specification)
    {
        if (!Normalize(specification.MmsType).Equals("structure", StringComparison.Ordinal))
            throw new InvalidOperationException($"origin must be an MMS structure, but live type is '{specification.MmsType}'.");

        var values = new List<MmsDataValue>(specification.Children.Count);
        foreach (var child in specification.Children)
        {
            var value = NormalizeName(child.Name) switch
            {
                "orcat" => BuildIntegerLike((long)origin.Category, child, "origin.orCat"),
                "orident" => BuildOctetString(origin.Identifier, child, "origin.orIdent"),
                _ => throw new NotSupportedException($"Unsupported origin component '{child.Name}'.")
            };
            values.Add(value);
        }

        var result = MmsDataValue.Structure(values);
        Iec61850ControlValueBinder.Validate(result, specification, "origin");
        return result;
    }

    private static MmsDataValue BuildControlNumber(byte value, MmsTypeSpecificationNode specification)
        => BuildIntegerLike(value, specification, "ctlNum");

    private static MmsDataValue BuildIntegerLike(long value, MmsTypeSpecificationNode specification, string path)
    {
        var result = Normalize(specification.MmsType) switch
        {
            "integer" or "bcd" => MmsDataValue.Integer(value),
            "unsigned" => MmsDataValue.Unsigned(checked((ulong)value)),
            _ => throw new InvalidOperationException($"{path} must be integer/unsigned, but live type is '{specification.MmsType}'.")
        };
        Iec61850ControlValueBinder.Validate(result, specification, path);
        return result;
    }

    private static MmsDataValue BuildOctetString(byte[] value, MmsTypeSpecificationNode specification, string path)
    {
        var result = Normalize(specification.MmsType) switch
        {
            "octet-string" => MmsDataValue.OctetString(value),
            "visible-string" => MmsDataValue.VisibleString(System.Text.Encoding.ASCII.GetString(value)),
            _ => throw new InvalidOperationException($"{path} must be octet-string/visible-string, but live type is '{specification.MmsType}'.")
        };
        Iec61850ControlValueBinder.Validate(result, specification, path);
        return result;
    }

    private static MmsDataValue BuildBoolean(bool value, MmsTypeSpecificationNode specification, string path)
    {
        if (!Normalize(specification.MmsType).Equals("boolean", StringComparison.Ordinal))
            throw new InvalidOperationException($"{path} must be boolean, but live type is '{specification.MmsType}'.");
        return MmsDataValue.Boolean(value);
    }

    private static MmsDataValue BuildCheck(bool synchro, bool interlock, MmsTypeSpecificationNode specification)
    {
        var type = Normalize(specification.MmsType);
        if (type != "bit-string")
            throw new InvalidOperationException($"Check must be bit-string, but live type is '{specification.MmsType}'.");

        // IEC 61850 Check is a two-bit bit-string: bit 0 synchrocheck, bit 1 interlock-check.
        byte bits = 0;
        if (synchro) bits |= 0x80;
        if (interlock) bits |= 0x40;
        return MmsDataValue.BitString(6, new[] { bits });
    }

    private static MmsDataValue BuildTimestamp(DateTimeOffset value, MmsTypeSpecificationNode specification)
    {
        var utc = value.ToUniversalTime();
        var result = Normalize(specification.MmsType) switch
        {
            "utc-time" => MmsDataValue.UtcTime(new Iec61850UtcTime(utc, 0)),
            "binary-time" => MmsDataValue.BinaryTime(BuildBinaryTime(utc, specification.Size)),
            _ => throw new InvalidOperationException($"Timestamp field '{specification.Name}' must be utc-time/binary-time, but live type is '{specification.MmsType}'.")
        };
        Iec61850ControlValueBinder.Validate(result, specification, specification.Name);
        return result;
    }

    private static byte[] BuildBinaryTime(DateTimeOffset value, int? requestedSize)
    {
        var utc = value.ToUniversalTime();
        if (utc < BinaryTimeEpoch)
            return requestedSize == 4 ? new byte[4] : new byte[6];

        var millisecondsSinceMidnight = checked((uint)utc.TimeOfDay.TotalMilliseconds);
        if (requestedSize == 4)
            return BitConverter.GetBytes(millisecondsSinceMidnight).Reverse().ToArray();

        var daysSinceEpoch = checked((ushort)(utc.UtcDateTime.Date - BinaryTimeEpoch.UtcDateTime.Date).TotalDays);
        return new[]
        {
            (byte)(millisecondsSinceMidnight >> 24),
            (byte)(millisecondsSinceMidnight >> 16),
            (byte)(millisecondsSinceMidnight >> 8),
            (byte)millisecondsSinceMidnight,
            (byte)(daysSinceEpoch >> 8),
            (byte)daysSinceEpoch
        };
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static string NormalizeName(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
