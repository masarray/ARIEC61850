using System.Globalization;

namespace AR.Iec61850.Comtrade;

public static class ComtradeChannelMapper
{
    private static readonly string[] VoltageKeys = ["Va", "Vb", "Vc", "Vn"];
    private static readonly string[] CurrentKeys = ["Ia", "Ib", "Ic", "In"];

    public static IReadOnlyDictionary<string, int> CreateDefaultMap(IReadOnlyList<ComtradeAnalogChannel> channels)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            var key = ResolveKey(channel);
            if (key is null || map.ContainsKey(key))
                continue;

            map[key] = i;
        }

        return map;
    }

    private static string? ResolveKey(ComtradeAnalogChannel channel)
    {
        var text = Normalize($"{channel.Name} {channel.Phase} {channel.CircuitComponent}");
        var unit = Normalize(channel.Unit);
        var isVoltage = unit.Contains('V') || text.StartsWith('V') || text.Contains(" VOLT");
        var isCurrent = unit.Contains('A') || text.StartsWith('I') || text.Contains(" AMP") || text.Contains(" CURR");

        if (isVoltage)
            return ResolvePhaseKey(text, channel.Phase, VoltageKeys);

        if (isCurrent)
            return ResolvePhaseKey(text, channel.Phase, CurrentKeys);

        return null;
    }

    private static string? ResolvePhaseKey(string normalizedText, string phase, IReadOnlyList<string> keys)
    {
        var normalizedPhase = Normalize(phase);
        if (HasNeutral(normalizedText) || HasNeutral(normalizedPhase))
            return keys[3];

        if (HasPhaseA(normalizedText) || HasPhaseA(normalizedPhase))
            return keys[0];
        if (HasPhaseB(normalizedText) || HasPhaseB(normalizedPhase))
            return keys[1];
        if (HasPhaseC(normalizedText) || HasPhaseC(normalizedPhase))
            return keys[2];

        return null;
    }

    private static bool HasPhaseA(string text)
        => ContainsToken(text, "A") || ContainsToken(text, "R") || ContainsToken(text, "L1") || text.Contains("PHASEA") || text.Contains("PHASE A");

    private static bool HasPhaseB(string text)
        => ContainsToken(text, "B") || ContainsToken(text, "S") || ContainsToken(text, "L2") || text.Contains("PHASEB") || text.Contains("PHASE B");

    private static bool HasPhaseC(string text)
        => ContainsToken(text, "C") || ContainsToken(text, "T") || ContainsToken(text, "L3") || text.Contains("PHASEC") || text.Contains("PHASE C");

    private static bool HasNeutral(string text)
        => ContainsToken(text, "N") || ContainsToken(text, "NEUTRAL") || ContainsToken(text, "GND") || ContainsToken(text, "E");

    private static bool ContainsToken(string text, string token)
    {
        var padded = $" {text.Replace('_', ' ').Replace('-', ' ')} ";
        return padded.Contains($" {token} ", StringComparison.OrdinalIgnoreCase) ||
               text.EndsWith(token, StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith(token, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.ToUpper(CultureInfo.InvariantCulture)
                .Replace("/", " ")
                .Replace("\\", " ")
                .Replace("-", " ")
                .Replace("_", " ")
                .Trim();
}
