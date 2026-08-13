namespace AR.Iec61850.Discovery;

public static class Iec61850ProbeValuePolicy
{
    public static bool IsPrimaryValueBearing(LiveIedResolvedDataSetAttributeModel? attribute)
    {
        if (attribute is null)
            return false;
        if (attribute.SemanticRole == Iec61850DataAttributeSemanticRole.PrimaryValue)
            return true;
        if (attribute.SemanticRole is Iec61850DataAttributeSemanticRole.Quality
            or Iec61850DataAttributeSemanticRole.Timestamp
            or Iec61850DataAttributeSemanticRole.FrozenValue)
            return false;

        var path = (attribute.Reference ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Trim('.')
            .ToLowerInvariant();
        if (path.Length == 0)
            return false;

        var leaf = path[(path.LastIndexOf('.') + 1)..];
        return leaf is "stval" or "general" or "posval" or "actval"
            || path.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ang.f", StringComparison.OrdinalIgnoreCase);
    }
}

public enum Iec61850TypeCompatibilityKind
{
    Unknown,
    Exact,
    Compatible,
    Conflict
}

/// <summary>
/// Conservative comparison between SCL basic types and live MMS TypeSpecification evidence.
/// MMS INTEGER and UNSIGNED are variable-width protocol families, so a decoder-side INT32
/// projection must not create a false width-specific mismatch against an SCL INT64/INT16/etc.
/// </summary>
public static class Iec61850TypeCompatibility
{
    public static Iec61850TypeCompatibilityKind Compare(
        string? designSclBType,
        string? designMmsType,
        string? observedSclBType,
        string? observedMmsType)
    {
        var design = DescribeDesign(designSclBType, designMmsType);
        var observed = DescribeObserved(observedSclBType, observedMmsType);

        if (design.Family.Length == 0 || observed.Family.Length == 0)
            return Iec61850TypeCompatibilityKind.Unknown;

        if (!design.Family.Equals(observed.Family, StringComparison.OrdinalIgnoreCase))
            return Iec61850TypeCompatibilityKind.Conflict;

        if (design.WidthBits.HasValue && observed.WidthBits.HasValue &&
            design.WidthAuthoritative && observed.WidthAuthoritative &&
            design.WidthBits.Value != observed.WidthBits.Value)
            return Iec61850TypeCompatibilityKind.Conflict;

        if (!observed.GenericMmsFamily && design.Canonical.Length > 0 && observed.Canonical.Length > 0 &&
            design.Canonical.Equals(observed.Canonical, StringComparison.OrdinalIgnoreCase))
            return Iec61850TypeCompatibilityKind.Exact;

        return Iec61850TypeCompatibilityKind.Compatible;
    }

    private static TypeDescriptor DescribeDesign(string? sclValue, string? mmsValue)
    {
        var scl = Normalize(sclValue);
        if (scl.Length > 0)
            return DescribeScl(scl, widthAuthoritative: true);

        return DescribeMms(Normalize(mmsValue), sclFallback: string.Empty);
    }

    private static TypeDescriptor DescribeObserved(string? sclValue, string? mmsValue)
    {
        var scl = Normalize(sclValue);
        var mms = Normalize(mmsValue);

        if (mms.Length > 0)
            return DescribeMms(mms, scl);

        return DescribeScl(scl, widthAuthoritative: true);
    }

    private static TypeDescriptor DescribeMms(string value, string sclFallback)
        => value switch
        {
            "BOOLEAN" => new("BOOL", "BOOLEAN", 1, true, false),
            "INTEGER" or "BCD" => new("SINT", "SINT", null, false, true),
            "UNSIGNED" => new("UINT", "UINT", null, false, true),
            "FLOATING-POINT" or "FLOATINGPOINT" => DescribeObservedFloat(sclFallback),
            "BIT-STRING" or "BITSTRING" or "BOOLEAN-ARRAY" => new("BITS", "BITS", null, false, false),
            "UTC-TIME" or "BINARY-TIME" => new("TIME", "TIME", null, false, false),
            "VISIBLE-STRING" or "MMS-STRING" => new("STRING", "STRING", null, false, false),
            "OCTET-STRING" => new("OCTETS", "OCTETS", null, false, false),
            "OBJECT-ID" => new("OBJREF", "OBJREF", null, false, false),
            "STRUCTURE" => new("STRUCT", "STRUCT", null, false, false),
            "ARRAY" => new("ARRAY", "ARRAY", null, false, false),
            _ => DescribeScl(value, widthAuthoritative: true)
        };

    private static TypeDescriptor DescribeObservedFloat(string sclFallback)
    {
        var scl = DescribeScl(sclFallback, widthAuthoritative: true);
        return scl.Family == "FLOAT"
            ? scl with { GenericMmsFamily = false }
            : new TypeDescriptor("FLOAT", "FLOAT", null, false, true);
    }

    private static TypeDescriptor DescribeScl(string value, bool widthAuthoritative)
    {
        if (value is "BOOLEAN" or "BOOL")
            return new("BOOL", "BOOLEAN", 1, true, false);

        if (value.StartsWith("INT", StringComparison.Ordinal) && value.EndsWith("U", StringComparison.Ordinal))
        {
            var width = ParseWidth(value[3..^1]);
            return new("UINT", value, width, widthAuthoritative && width.HasValue, false);
        }

        if (value.StartsWith("INT", StringComparison.Ordinal))
        {
            var width = ParseWidth(value[3..]);
            return new("SINT", value, width, widthAuthoritative && width.HasValue, false);
        }

        if (value is "ENUM" or "DBPOS" or "TCMD")
            return new("SINT", value, null, false, false);

        if (value.StartsWith("FLOAT", StringComparison.Ordinal))
        {
            var width = ParseWidth(value[5..]);
            return new("FLOAT", value, width, widthAuthoritative && width.HasValue, false);
        }

        if (value is "QUALITY" or "CHECK")
            return new("BITS", value, null, false, false);
        if (value is "TIMESTAMP" or "ENTRYTIME")
            return new("TIME", value, null, false, false);
        if (value.StartsWith("VISSTRING", StringComparison.Ordinal) || value.StartsWith("UNICODE", StringComparison.Ordinal))
            return new("STRING", value, null, false, false);
        if (value.StartsWith("OCTET", StringComparison.Ordinal))
            return new("OCTETS", value, null, false, false);
        if (value == "OBJREF")
            return new("OBJREF", value, null, false, false);
        if (value == "STRUCT")
            return new("STRUCT", value, null, false, false);

        return new(string.Empty, string.Empty, null, false, false);
    }

    private static int? ParseWidth(string value)
        => int.TryParse(value, out var width) && width > 0 ? width : null;

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().Replace("_", "-", StringComparison.Ordinal).ToUpperInvariant();

    private readonly record struct TypeDescriptor(
        string Family,
        string Canonical,
        int? WidthBits,
        bool WidthAuthoritative,
        bool GenericMmsFamily);
}
