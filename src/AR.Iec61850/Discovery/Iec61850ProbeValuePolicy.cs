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