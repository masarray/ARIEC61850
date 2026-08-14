using System.Globalization;

namespace AR.Iec61850.Mms;

public sealed class MmsRenderedField
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public MmsDataValue? Data { get; init; }
}

public sealed class MmsRenderedValue
{
    public string Compact { get; init; } = string.Empty;
    public IReadOnlyList<MmsRenderedField> Fields { get; init; } = Array.Empty<MmsRenderedField>();
    public bool IsStructured => Fields.Count > 0;
}

public static class MmsDataValueRenderer
{
    public static string ToCompactString(MmsDataValue? value)
        => Render(value).Compact;

    public static string ToCompactString(MmsDataValue? value, string? reference)
        => Render(value, reference).Compact;

    public static MmsRenderedValue Render(MmsDataValue? value, string? reference = null)
    {
        if (value == null)
            return new MmsRenderedValue { Compact = "-" };

        if (value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
            return new MmsRenderedValue { Compact = FormatScalar(value) };

        var fieldNames = GuessFieldNames(reference, value.Children.Count).ToArray();
        var fields = value.Children.Select((child, index) => new MmsRenderedField
        {
            Name = index < fieldNames.Length ? fieldNames[index] : $"[{index}]",
            Value = child.Kind is MmsDataKind.Structure or MmsDataKind.Array
                ? Render(child).Compact
                : FormatScalar(child),
            Data = child
        }).ToArray();

        var kind = value.Kind == MmsDataKind.Array ? "Array" : "Structure";
        var compactFields = string.Join(", ", fields.Take(8).Select(x => $"{x.Name}={x.Value}"));
        if (fields.Length > 8)
            compactFields += $", ... +{fields.Length - 8}";

        return new MmsRenderedValue
        {
            Compact = $"{kind}({fields.Length}) {{{compactFields}}}",
            Fields = fields
        };
    }

    public static IEnumerable<string> ToMultiline(MmsDataValue? value, string? reference = null, string indent = "")
    {
        var rendered = Render(value, reference);
        yield return rendered.Compact;

        foreach (var field in rendered.Fields)
        {
            if (field.Data?.Kind is MmsDataKind.Structure or MmsDataKind.Array)
            {
                var nested = Render(field.Data).Compact;
                yield return $"{indent}  {field.Name}: {nested}";
            }
            else
            {
                yield return $"{indent}  {field.Name}: {field.Value}";
            }
        }
    }

    private static string FormatScalar(MmsDataValue value)
    {
        return value.Kind switch
        {
            MmsDataKind.Boolean => Convert.ToString(value.Value, CultureInfo.InvariantCulture)?.ToLowerInvariant() ?? string.Empty,
            MmsDataKind.Integer => Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            MmsDataKind.Unsigned => Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            MmsDataKind.FloatingPoint => value.Value switch
            {
                float f => f.ToString("0.###", CultureInfo.InvariantCulture),
                double d => d.ToString("0.###", CultureInfo.InvariantCulture),
                _ => string.Empty
            },
            MmsDataKind.VisibleString or MmsDataKind.MmsString => Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty,
            MmsDataKind.UtcTime => value.Value is Iec61850UtcTime utc ? Iec61850UtcTimeFormatter.FormatFullPrecisionUtc(utc) : string.Empty,
            MmsDataKind.BitString => FormatBitString(value),
            MmsDataKind.BinaryTime => MmsBinaryTime.FromBytes(value.RawValue).ToDisplayString(),
            MmsDataKind.OctetString => Convert.ToHexString(value.RawValue.ToArray()),
            MmsDataKind.Unknown => $"unknown(tag={value.UnknownTagNumber}, raw={Convert.ToHexString(value.RawValue.ToArray())})",
            _ => MmsDataCodec.ToDisplayString(value)
        };
    }

    private static string FormatBitString(MmsDataValue value)
    {
        var raw = value.RawValue.ToArray();
        if (raw.Length == 0)
            return "bits()";

        var unused = raw[0];
        var data = raw.Skip(1).ToArray();
        return $"bits({Convert.ToHexString(data)}, unused={unused})";
    }

    private static IEnumerable<string> GuessFieldNames(string? reference, int count)
    {
        var leaf = ExtractLeaf(reference);
        if (leaf.Equals("q", StringComparison.OrdinalIgnoreCase) || leaf.Equals("t", StringComparison.OrdinalIgnoreCase))
            return Enumerable.Range(0, count).Select(i => $"[{i}]");

        if (count == 3)
            return ["stVal", "q", "t"];

        if (count == 2)
            return ["value", "q"];

        if (count == 6)
            return ["general", "phsA", "phsB", "phsC", "q", "t"];

        if (count == 7)
            return ["general", "phsA", "phsB", "phsC", "neut", "q", "t"];

        if (count >= 4 && (leaf.Equals("Op", StringComparison.OrdinalIgnoreCase) || leaf.Equals("Str", StringComparison.OrdinalIgnoreCase)))
        {
            var names = new[] { "general", "phsA", "phsB", "phsC", "neut", "q", "t" };
            return Enumerable.Range(0, count).Select(i => i < names.Length ? names[i] : $"[{i}]");
        }

        return Enumerable.Range(0, count).Select(i => $"[{i}]");
    }

    private static string ExtractLeaf(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var text = reference.Replace('$', '.');
        var slash = text.LastIndexOf('/');
        if (slash >= 0)
            text = text[(slash + 1)..];

        var dot = text.LastIndexOf('.');
        return dot >= 0 ? text[(dot + 1)..] : text;
    }
}
