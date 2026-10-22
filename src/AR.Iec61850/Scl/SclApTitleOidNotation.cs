using System.Globalization;

namespace AR.Iec61850.Scl;

/// <summary>
/// SCL AP-Title is normally an ASN.1 OID arc list. Some legacy engineering
/// exports spell the first *BER subidentifier* as one decimal component
/// (40*firstArc + secondArc). Only the unambiguous single-octet 40..79
/// form is recognized here; arbitrary malformed OIDs remain invalid.
/// No source XML or other SCL association field is rewritten.
/// </summary>
internal static class SclApTitleOidNotation
{
    internal static bool TryParse(
        string text,
        out uint[] arcs,
        out bool wasPackedFirstArc,
        out string error)
    {
        arcs = Array.Empty<uint>();
        wasPackedFirstArc = false;
        error = string.Empty;
        var tokens = text.Split(
            [',', '.', ' ', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            error = $"SCL OSI-AP-Title '{text}' does not contain a valid OID.";
            return false;
        }

        var declared = new uint[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!uint.TryParse(tokens[i], NumberStyles.None, CultureInfo.InvariantCulture, out declared[i]))
            {
                error = $"SCL OSI-AP-Title '{text}' contains an invalid OID arc '{tokens[i]}'.";
                return false;
            }
        }

        if (declared[0] <= 2 && (declared[0] == 2 || declared[1] <= 39))
        {
            arcs = declared;
            return true;
        }

        // Legacy OID subidentifier: decimal 41 means ASN.1 arcs 1.1.
        // At least one remaining arc is required to distinguish this
        // representation from an incomplete engineering declaration.
        if (declared.Length >= 3 && declared[0] is >= 40 and <= 79)
        {
            arcs = new uint[declared.Length + 1];
            arcs[0] = 1;
            arcs[1] = declared[0] - 40;
            Array.Copy(declared, 1, arcs, 2, declared.Length - 1);
            wasPackedFirstArc = true;
            return true;
        }

        error = $"SCL OSI-AP-Title '{text}' violates ASN.1 object-identifier arc rules.";
        return false;
    }

    internal static string CompatibilityWarning(string declared, IReadOnlyList<uint> decoded)
        => $"SCL OSI-AP-Title '{declared}' uses legacy BER-packed first subidentifier; " +
           $"association encodes standard OID '{string.Join(",", decoded)}'. Original SCL declaration remains unchanged.";
}
