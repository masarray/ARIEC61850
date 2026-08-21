using System.Net;

namespace AR.Iec61850.Mms;

/// <summary>
/// Decodes an IEC 61850 RCB Owner OCTET STRING when the server exposes it as an
/// IPv4/IPv6 client address and compares it with the exact local TCP endpoint of
/// the active MMS association. Unknown encodings fail closed.
/// </summary>
public static class MmsRcbOwnerIdentity
{
    public static bool TryDecodeIpAddress(string? ownerText, out IPAddress? address)
    {
        address = null;
        var compact = CompactHex(ownerText);
        if (compact.Length is not (8 or 32) || compact.Any(character => !Uri.IsHexDigit(character)))
            return false;

        try
        {
            var bytes = Convert.FromHexString(compact);
            address = new IPAddress(bytes);
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool MatchesLocalTcpAddress(
        string? ownerText,
        string? localTcpAddress,
        out string reason)
    {
        if (!TryDecodeIpAddress(ownerText, out var ownerAddress) || ownerAddress is null)
        {
            reason = $"Owner '{TextOrDash(ownerText)}' is not a supported 4-byte IPv4 or 16-byte IPv6 address OCTET STRING.";
            return false;
        }

        if (!IPAddress.TryParse((localTcpAddress ?? string.Empty).Trim(), out var localAddress))
        {
            reason = $"The active MMS association local TCP address is unavailable or invalid: {TextOrDash(localTcpAddress)}.";
            return false;
        }

        if (localAddress.IsIPv4MappedToIPv6)
            localAddress = localAddress.MapToIPv4();

        if (!ownerAddress.Equals(localAddress))
        {
            reason = $"Owner address {ownerAddress} does not match the active MMS association local TCP address {localAddress}.";
            return false;
        }

        reason = $"Owner address {ownerAddress} matches the active MMS association local TCP address exactly.";
        return true;
    }

    private static string CompactHex(string? value)
        => (value ?? string.Empty).Trim()
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
