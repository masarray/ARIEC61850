using System.Globalization;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Binding;

public sealed class Iec61850BoundValueRow
{
    public string Name { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = "-";
    public string Quality { get; init; } = "-";
    public string Timestamp { get; init; } = "-";
    public string Status { get; init; } = "bound";
    public string SemanticKind { get; init; } = string.Empty;
    public Iec61850BindingConfidence Confidence { get; init; } = Iec61850BindingConfidence.Low;
    public IReadOnlyList<Iec61850BoundValueRow> Children { get; init; } = Array.Empty<Iec61850BoundValueRow>();
}

public sealed class Iec61850ValueBindingResult
{
    public Iec61850BoundValueRow Root { get; init; } = new();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public bool HasMismatch => Diagnostics.Count > 0;
}

public static class Iec61850ValueBindingEngine
{
    public static Iec61850ValueBindingResult Bind(Iec61850ValueSchemaNode schema, MmsDataValue? value)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var diagnostics = new List<string>();
        var root = BindNode(schema, value, diagnostics, referenceOverride: schema.Reference);
        return new Iec61850ValueBindingResult
        {
            Root = root,
            Diagnostics = diagnostics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public static Iec61850BoundValueRow ToUnboundRow(Iec61850ValueSchemaNode schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new Iec61850BoundValueRow
        {
            Name = schema.Name,
            Reference = schema.Reference,
            FunctionalConstraint = schema.FunctionalConstraint,
            Type = DisplayType(schema),
            Value = "-",
            Quality = "-",
            Timestamp = "-",
            Status = schema.Confidence == Iec61850BindingConfidence.Low ? "schema-low-confidence" : "schema",
            SemanticKind = schema.SemanticKind,
            Confidence = schema.Confidence,
            Children = schema.Children.Select(ToUnboundRow).ToArray()
        };
    }

    private static Iec61850BoundValueRow BindNode(
        Iec61850ValueSchemaNode schema,
        MmsDataValue? value,
        ICollection<string> diagnostics,
        string? referenceOverride = null)
    {
        if (value == null)
        {
            var unbound = ToUnboundRow(schema);
            return WithStatus(unbound, "not read");
        }

        var children = BindChildren(schema, value, diagnostics).ToArray();
        var scalarValue = FormatValue(schema, value);
        var quality = FormatQualityColumn(schema, value, children);
        var timestamp = FormatTimestampColumn(schema, value, children);

        return new Iec61850BoundValueRow
        {
            Name = schema.Name,
            Reference = string.IsNullOrWhiteSpace(referenceOverride) ? schema.Reference : referenceOverride!,
            FunctionalConstraint = schema.FunctionalConstraint,
            Type = DisplayType(schema, value),
            Value = scalarValue,
            Quality = quality,
            Timestamp = timestamp,
            Status = schema.Confidence == Iec61850BindingConfidence.Low ? "bound-low-confidence" : "read",
            SemanticKind = schema.SemanticKind,
            Confidence = schema.Confidence,
            Children = children
        };
    }

    private static IEnumerable<Iec61850BoundValueRow> BindChildren(Iec61850ValueSchemaNode schema, MmsDataValue value, ICollection<string> diagnostics)
    {
        if (schema.SemanticKind.Equals("Quality", StringComparison.OrdinalIgnoreCase) || schema.BType.Equals("Quality", StringComparison.OrdinalIgnoreCase) || schema.Name.Equals("q", StringComparison.OrdinalIgnoreCase))
            return DecodeQualityRows(schema, value);

        if (schema.SemanticKind.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) || schema.BType.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) || schema.Name.Equals("t", StringComparison.OrdinalIgnoreCase))
            return DecodeTimestampRows(schema, value);

        if (schema.SemanticKind.Equals("Check", StringComparison.OrdinalIgnoreCase) || schema.Name.Equals("Check", StringComparison.OrdinalIgnoreCase))
            return DecodeCheckRows(schema, value);

        if (value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
        {
            // Template-only children are not rendered after a scalar read, except q/t/check which are handled above.
            return Array.Empty<Iec61850BoundValueRow>();
        }

        var schemaChildren = schema.Children.ToArray();
        if (schemaChildren.Length == 0)
        {
            diagnostics.Add($"LOW_CONFIDENCE_RAW_STRUCTURE: {schema.Reference} has no schema; raw positional fields are shown as [index].");
            return value.Children.Select((child, index) => BindRawChild(schema, child, index, diagnostics)).ToArray();
        }

        var rows = new List<Iec61850BoundValueRow>();
        var childCount = Math.Min(schemaChildren.Length, value.Children.Count);
        if (schemaChildren.Length != value.Children.Count)
            diagnostics.Add($"TYPE_BINDING_MISMATCH: {schema.Reference} expected {schemaChildren.Length} child value(s), received {value.Children.Count}.");

        for (var index = 0; index < childCount; index++)
            rows.Add(BindNode(schemaChildren[index], value.Children[index], diagnostics));

        for (var index = childCount; index < value.Children.Count; index++)
            rows.Add(BindRawChild(schema, value.Children[index], index, diagnostics));

        for (var index = childCount; index < schemaChildren.Length; index++)
            rows.Add(ToUnboundRow(schemaChildren[index]));

        return rows;
    }

    private static Iec61850BoundValueRow BindRawChild(Iec61850ValueSchemaNode parent, MmsDataValue child, int index, ICollection<string> diagnostics)
    {
        var schema = new Iec61850ValueSchemaNode
        {
            Name = $"[{index.ToString(CultureInfo.InvariantCulture)}]",
            Path = parent.Path + $"[{index.ToString(CultureInfo.InvariantCulture)}]",
            Reference = parent.Reference + $"[{index.ToString(CultureInfo.InvariantCulture)}]",
            FunctionalConstraint = parent.FunctionalConstraint,
            Cdc = parent.Cdc,
            BType = child.Kind.ToString(),
            MmsType = child.Kind.ToString(),
            SemanticKind = "RawPositionalField",
            Source = "RawMmsValue",
            Confidence = Iec61850BindingConfidence.Low
        };
        diagnostics.Add($"RAW_POSITIONAL_FIELD: {schema.Reference} is displayed without IEC 61850 semantic name.");
        return BindNode(schema, child, diagnostics);
    }

    private static IEnumerable<Iec61850BoundValueRow> DecodeQualityRows(Iec61850ValueSchemaNode schema, MmsDataValue value)
    {
        var quality = Iec61850QualityDecoder.Decode(value);
        if (!quality.IsDecoded)
            return Array.Empty<Iec61850BoundValueRow>();

        return new[]
        {
            Row(schema, "Validity", "Enum", quality.Validity, quality.Validity, "decoded"),
            GroupRow(schema, "Quality Details", [
                Row(schema, "Overflow", "BOOLEAN", Bool(quality.Overflow), "-", "decoded"),
                Row(schema, "OutOfRange", "BOOLEAN", Bool(quality.OutOfRange), "-", "decoded"),
                Row(schema, "BadReference", "BOOLEAN", Bool(quality.BadReference), "-", "decoded"),
                Row(schema, "Oscillatory", "BOOLEAN", Bool(quality.Oscillatory), "-", "decoded"),
                Row(schema, "Failure", "BOOLEAN", Bool(quality.Failure), "-", "decoded"),
                Row(schema, "OldData", "BOOLEAN", Bool(quality.OldData), "-", "decoded"),
                Row(schema, "Inconsistent", "BOOLEAN", Bool(quality.Inconsistent), "-", "decoded"),
                Row(schema, "Inaccurate", "BOOLEAN", Bool(quality.Inaccurate), "-", "decoded")
            ]),
            Row(schema, "Source", "Enum", quality.Source, "-", "decoded"),
            Row(schema, "Test", "BOOLEAN", Bool(quality.Test), "-", "decoded"),
            Row(schema, "OperatorBlocked", "BOOLEAN", Bool(quality.OperatorBlocked), "-", "decoded")
        };
    }

    private static IEnumerable<Iec61850BoundValueRow> DecodeTimestampRows(Iec61850ValueSchemaNode schema, MmsDataValue value)
    {
        var timestamp = Iec61850TimestampDecoder.Decode(value);
        if (!timestamp.IsDecoded)
            return Array.Empty<Iec61850BoundValueRow>();

        return new[]
        {
            Row(schema, "LeapSecondsKnown", "BOOLEAN", Bool(timestamp.LeapSecondsKnown), "-", "decoded"),
            Row(schema, "ClockFailure", "BOOLEAN", Bool(timestamp.ClockFailure), "-", "decoded"),
            Row(schema, "ClockNotSynchronized", "BOOLEAN", Bool(timestamp.ClockNotSynchronized), "-", "decoded"),
            Row(schema, "TimeAccuracy", "Enum", timestamp.TimeAccuracy, "-", "decoded")
        };
    }

    private static IEnumerable<Iec61850BoundValueRow> DecodeCheckRows(Iec61850ValueSchemaNode schema, MmsDataValue value)
    {
        var check = Iec61850CheckDecoder.Decode(value);
        if (!check.IsDecoded)
            return Array.Empty<Iec61850BoundValueRow>();

        return new[]
        {
            Row(schema, "InterlockCheck", "BOOLEAN", Bool(check.InterlockCheck), "-", "decoded"),
            Row(schema, "Synchrocheck", "BOOLEAN", Bool(check.Synchrocheck), "-", "decoded")
        };
    }

    private static Iec61850BoundValueRow Row(Iec61850ValueSchemaNode parent, string name, string type, string value, string quality, string status)
        => new()
        {
            Name = name,
            Reference = string.IsNullOrWhiteSpace(parent.Reference) ? name : parent.Reference + "." + name,
            FunctionalConstraint = parent.FunctionalConstraint,
            Type = type,
            Value = value,
            Quality = quality,
            Timestamp = "-",
            Status = status,
            SemanticKind = type,
            Confidence = Iec61850BindingConfidence.Exact
        };

    private static Iec61850BoundValueRow GroupRow(Iec61850ValueSchemaNode parent, string name, IReadOnlyList<Iec61850BoundValueRow> children)
        => new()
        {
            Name = name,
            Reference = string.IsNullOrWhiteSpace(parent.Reference) ? name : parent.Reference + "." + name,
            FunctionalConstraint = parent.FunctionalConstraint,
            Type = "Group",
            Value = string.Empty,
            Quality = "-",
            Timestamp = "-",
            Status = "decoded",
            SemanticKind = "Group",
            Confidence = Iec61850BindingConfidence.Exact,
            Children = children
        };

    private static Iec61850BoundValueRow WithStatus(Iec61850BoundValueRow row, string status)
        => new()
        {
            Name = row.Name,
            Reference = row.Reference,
            FunctionalConstraint = row.FunctionalConstraint,
            Type = row.Type,
            Value = row.Value,
            Quality = row.Quality,
            Timestamp = row.Timestamp,
            Status = status,
            SemanticKind = row.SemanticKind,
            Confidence = row.Confidence,
            Children = row.Children
        };

    private static string FormatValue(Iec61850ValueSchemaNode schema, MmsDataValue value)
    {
        if (schema.SemanticKind.Equals("Quality", StringComparison.OrdinalIgnoreCase) || schema.BType.Equals("Quality", StringComparison.OrdinalIgnoreCase) || schema.Name.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            var q = Iec61850QualityDecoder.Decode(value);
            return q.IsDecoded ? q.Validity : MmsDataValueRenderer.ToCompactString(value, schema.Reference);
        }

        if (schema.SemanticKind.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) || schema.BType.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) || schema.Name.Equals("t", StringComparison.OrdinalIgnoreCase))
        {
            var t = Iec61850TimestampDecoder.Decode(value);
            return t.IsDecoded ? t.DisplayTime : MmsDataValueRenderer.ToCompactString(value, schema.Reference);
        }

        if (schema.SemanticKind.Equals("ControlModel", StringComparison.OrdinalIgnoreCase) || schema.Name.Equals("ctlModel", StringComparison.OrdinalIgnoreCase))
            return Iec61850EnumValueDecoder.DecodeControlModel(value);

        if (schema.SemanticKind.Contains("DoublePoint", StringComparison.OrdinalIgnoreCase) || schema.BType.Equals("Dbpos", StringComparison.OrdinalIgnoreCase))
            return Iec61850EnumValueDecoder.DecodeDbpos(value);

        if (schema.SemanticKind.Equals("OriginCategory", StringComparison.OrdinalIgnoreCase) || schema.Name.Equals("orCat", StringComparison.OrdinalIgnoreCase))
            return Iec61850EnumValueDecoder.DecodeOriginCategory(value);

        if (schema.SemanticKind.Equals("Check", StringComparison.OrdinalIgnoreCase) || schema.Name.Equals("Check", StringComparison.OrdinalIgnoreCase))
        {
            var check = Iec61850CheckDecoder.Decode(value);
            return check.IsDecoded ? check.Summary : MmsDataValueRenderer.ToCompactString(value, schema.Reference);
        }

        if (value.Kind == MmsDataKind.Structure)
            return $"Struct({value.Children.Count.ToString(CultureInfo.InvariantCulture)})";
        if (value.Kind == MmsDataKind.Array)
            return $"Array({value.Children.Count.ToString(CultureInfo.InvariantCulture)})";

        return MmsDataValueRenderer.ToCompactString(value, schema.Reference);
    }

    private static string FormatQualityColumn(Iec61850ValueSchemaNode schema, MmsDataValue value, IReadOnlyList<Iec61850BoundValueRow> children)
    {
        if (schema.SemanticKind.Equals("Quality", StringComparison.OrdinalIgnoreCase) || schema.BType.Equals("Quality", StringComparison.OrdinalIgnoreCase) || schema.Name.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            var q = Iec61850QualityDecoder.Decode(value);
            return q.IsDecoded ? q.Validity : "-";
        }

        var qChild = children.FirstOrDefault(x => x.Name.Equals("q", StringComparison.OrdinalIgnoreCase));
        return qChild?.Quality is { Length: > 0 } quality && quality != "-" ? quality : "-";
    }

    private static string FormatTimestampColumn(Iec61850ValueSchemaNode schema, MmsDataValue value, IReadOnlyList<Iec61850BoundValueRow> children)
    {
        if (schema.SemanticKind.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) || schema.BType.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) || schema.Name.Equals("t", StringComparison.OrdinalIgnoreCase))
        {
            var t = Iec61850TimestampDecoder.Decode(value);
            return t.IsDecoded ? t.DisplayTime : "-";
        }

        var tChild = children.FirstOrDefault(x => x.Name.Equals("t", StringComparison.OrdinalIgnoreCase));
        return tChild?.Timestamp is { Length: > 0 } timestamp && timestamp != "-" ? timestamp : "-";
    }

    private static string DisplayType(Iec61850ValueSchemaNode schema, MmsDataValue? value = null)
    {
        if (!string.IsNullOrWhiteSpace(schema.BType) && !schema.BType.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return schema.BType;
        if (!string.IsNullOrWhiteSpace(schema.SemanticKind) && !schema.SemanticKind.Equals("Value", StringComparison.OrdinalIgnoreCase))
            return schema.SemanticKind;
        if (!string.IsNullOrWhiteSpace(schema.MmsType) && !schema.MmsType.Equals("NotRead", StringComparison.OrdinalIgnoreCase))
            return schema.MmsType;
        return value?.Kind.ToString() ?? "Unknown";
    }

    private static string Bool(bool value) => value ? "true" : "false";
}

public sealed record Iec61850DecodedQuality(
    bool IsDecoded,
    string Validity,
    bool Overflow,
    bool OutOfRange,
    bool BadReference,
    bool Oscillatory,
    bool Failure,
    bool OldData,
    bool Inconsistent,
    bool Inaccurate,
    string Source,
    bool Test,
    bool OperatorBlocked);

public static class Iec61850QualityDecoder
{
    public static Iec61850DecodedQuality Decode(MmsDataValue? value)
    {
        value = FindFirst(value, MmsDataKind.BitString);
        if (value?.Kind != MmsDataKind.BitString || !HasQualityBitLength(value))
            return new Iec61850DecodedQuality(false, "-", false, false, false, false, false, false, false, false, "-", false, false);

        var bit = BitReader(value);
        var validityCode = (bit(0) ? 2 : 0) + (bit(1) ? 1 : 0);
        var validity = validityCode switch
        {
            0 => "good",
            1 => "invalid",
            2 => "reserved",
            3 => "questionable",
            _ => "unknown"
        };

        return new Iec61850DecodedQuality(
            true,
            validity,
            bit(2),
            bit(3),
            bit(4),
            bit(5),
            bit(6),
            bit(7),
            bit(8),
            bit(9),
            bit(10) ? "substituted" : "process",
            bit(11),
            bit(12));
    }

    private static bool HasQualityBitLength(MmsDataValue value)
    {
        if (value.RawValue.Count < 3)
            return false;

        var unusedBits = value.RawValue[0];
        if (unusedBits > 7)
            return false;

        var bitCount = (value.RawValue.Count - 1) * 8 - unusedBits;
        return bitCount >= 13;
    }

    private static MmsDataValue? FindFirst(MmsDataValue? value, MmsDataKind kind)
    {
        if (value == null)
            return null;
        if (value.Kind == kind)
            return value;
        if (value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
            return null;

        foreach (var child in value.Children)
        {
            var match = FindFirst(child, kind);
            if (match != null)
                return match;
        }

        return null;
    }

    internal static Func<int, bool> BitReader(MmsDataValue value)
    {
        var raw = value.RawValue.ToArray();
        var data = raw.Length <= 1 ? Array.Empty<byte>() : raw.Skip(1).ToArray();
        return index =>
        {
            if (index < 0)
                return false;
            var byteIndex = index / 8;
            var bitIndex = index % 8;
            if (byteIndex >= data.Length)
                return false;
            return (data[byteIndex] & (0x80 >> bitIndex)) != 0;
        };
    }
}

public sealed record Iec61850DecodedTimestamp(
    bool IsDecoded,
    string DisplayTime,
    bool LeapSecondsKnown,
    bool ClockFailure,
    bool ClockNotSynchronized,
    string TimeAccuracy);

public static class Iec61850TimestampDecoder
{
    private static MmsDataValue? FindFirst(MmsDataValue? value, MmsDataKind kind)
    {
        if (value == null)
            return null;
        if (value.Kind == kind)
            return value;
        if (value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
            return null;
        foreach (var child in value.Children)
        {
            var match = FindFirst(child, kind);
            if (match != null)
                return match;
        }
        return null;
    }

    public static Iec61850DecodedTimestamp Decode(MmsDataValue? value)
    {
        value = FindFirst(value, MmsDataKind.UtcTime);
        if (value?.Kind != MmsDataKind.UtcTime || value.Value is not Iec61850UtcTime utc)
            return new Iec61850DecodedTimestamp(false, "-", false, false, false, "-");

        var quality = utc.Quality;
        var leapSecondsKnown = (quality & 0x80) != 0;
        var clockFailure = (quality & 0x40) != 0;
        var clockNotSynchronized = (quality & 0x20) != 0;
        var accuracyCode = quality & 0x1F;
        var accuracy = accuracyCode == 31 ? "unspecified" : $"2^-{accuracyCode.ToString(CultureInfo.InvariantCulture)} s";
        return new Iec61850DecodedTimestamp(
            true,
            utc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            leapSecondsKnown,
            clockFailure,
            clockNotSynchronized,
            accuracy);
    }
}

public sealed record Iec61850DecodedCheck(bool IsDecoded, bool InterlockCheck, bool Synchrocheck, string Summary);

public static class Iec61850CheckDecoder
{
    private static MmsDataValue? FindFirst(MmsDataValue? value, MmsDataKind kind)
    {
        if (value == null)
            return null;
        if (value.Kind == kind)
            return value;
        if (value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
            return null;
        foreach (var child in value.Children)
        {
            var match = FindFirst(child, kind);
            if (match != null)
                return match;
        }
        return null;
    }

    public static Iec61850DecodedCheck Decode(MmsDataValue? value)
    {
        value = FindFirst(value, MmsDataKind.BitString);
        if (value?.Kind != MmsDataKind.BitString)
            return new Iec61850DecodedCheck(false, false, false, "-");

        var bit = Iec61850QualityDecoder.BitReader(value);
        var interlock = bit(0);
        var synchro = bit(1);
        var enabled = new List<string>();
        if (interlock)
            enabled.Add("InterlockCheck");
        if (synchro)
            enabled.Add("Synchrocheck");
        return new Iec61850DecodedCheck(true, interlock, synchro, enabled.Count == 0 ? "none" : string.Join(", ", enabled));
    }
}

public static class Iec61850EnumValueDecoder
{
    public static string DecodeControlModel(MmsDataValue? value)
    {
        var number = ToInt64(value);
        return number switch
        {
            0 => "status-only",
            1 => "direct-with-normal-security",
            2 => "sbo-with-normal-security",
            3 => "direct-with-enhanced-security",
            4 => "sbo-with-enhanced-security",
            null => MmsDataValueRenderer.ToCompactString(value),
            _ => $"{number.Value.ToString(CultureInfo.InvariantCulture)} / unknown"
        };
    }

    public static string DecodeDbpos(MmsDataValue? value)
    {
        var number = ToInt64(value);
        return number switch
        {
            0 => "intermediate-state",
            1 => "off",
            2 => "on",
            3 => "bad-state",
            null => MmsDataValueRenderer.ToCompactString(value),
            _ => $"{number.Value.ToString(CultureInfo.InvariantCulture)} / unknown"
        };
    }

    public static string DecodeOriginCategory(MmsDataValue? value)
    {
        var number = ToInt64(value);
        return number switch
        {
            0 => "not-supported",
            1 => "bay-control",
            2 => "station-control",
            3 => "remote-control",
            4 => "automatic-bay",
            5 => "automatic-station",
            6 => "automatic-remote",
            7 => "maintenance",
            8 => "process",
            null => MmsDataValueRenderer.ToCompactString(value),
            _ => $"{number.Value.ToString(CultureInfo.InvariantCulture)} / unknown"
        };
    }

    private static long? ToInt64(MmsDataValue? value)
    {
        if (value == null)
            return null;
        if (value.Kind == MmsDataKind.Integer)
            return Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
        if (value.Kind == MmsDataKind.Unsigned)
        {
            var unsigned = Convert.ToUInt64(value.Value, CultureInfo.InvariantCulture);
            return unsigned > long.MaxValue ? null : (long)unsigned;
        }
        if (value.Kind == MmsDataKind.Boolean)
            return value.Value is bool b ? b ? 1 : 0 : null;
        if (value.Kind == MmsDataKind.BitString)
        {
            var encoded = value.RawValue.ToArray();
            if (encoded.Length < 2 || encoded[0] > 7)
                return null;

            var bitCount = checked((encoded.Length - 1) * 8 - encoded[0]);
            if (bitCount is <= 0 or > 63)
                return null;

            ulong numeric = 0;
            for (var encodedBit = 0; encodedBit < bitCount; encodedBit++)
            {
                var byteIndex = 1 + encodedBit / 8;
                var bitInByte = encodedBit % 8;
                if ((encoded[byteIndex] & (0x80 >> bitInByte)) == 0)
                    continue;

                var numericBit = bitCount - 1 - encodedBit;
                numeric |= 1UL << numericBit;
            }

            return numeric <= long.MaxValue ? (long)numeric : null;
        }
        return null;
    }
}
