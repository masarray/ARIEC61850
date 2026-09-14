using System.Globalization;
using AR.Iec61850.Acse;
using AR.Iec61850.Osi;

namespace AR.Iec61850.Scl;

public sealed class MmsLocalAssociationProfile
{
    public string Name { get; init; } = string.Empty;
    public byte[] TransportSelector { get; init; } = Array.Empty<byte>();
    public byte[] SessionSelector { get; init; } = Array.Empty<byte>();
    public byte[] PresentationSelector { get; init; } = Array.Empty<byte>();
    public uint[] ApTitle { get; init; } = Array.Empty<uint>();
    public int AeQualifier { get; init; }
    public byte TpduSizeExponent { get; init; } = 0x0A;
    public MmsInitiateRequestParameters Initiate { get; init; } = new();

    public static MmsLocalAssociationProfile ExistingRuntimeDefault { get; } = new()
    {
        Name = "ExistingRuntimeDefault",
        TransportSelector = [0x00, 0x01],
        SessionSelector = [0x00, 0x01],
        PresentationSelector = [0x00, 0x00, 0x00, 0x01],
        ApTitle = [1, 1, 1, 999],
        AeQualifier = 12,
        TpduSizeExponent = 0x0A,
        Initiate = new MmsInitiateRequestParameters
        {
            LocalDetailCalling = 65000,
            MaxOutstandingCalling = 10,
            MaxOutstandingCalled = 10,
            NestingLevel = 5
        }
    };
}

public sealed class SclAssistedMmsAssociationPlan
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 102;
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public string LocalProfileName { get; init; } = string.Empty;
    public CotpConnectParameters Cotp { get; init; } = new();
    public AcseMmsAssociationRequestParameters Association { get; init; } = new();
    public byte[] CotpConnectRequest { get; init; } = Array.Empty<byte>();
    public byte[] SessionPresentationAcseMmsRequest { get; init; } = Array.Empty<byte>();
}

public sealed class SclAssistedMmsAssociationPlanResult
{
    public SclAssistedMmsAssociationPlan? Plan { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool IsSuccess => Plan is not null && Errors.Count == 0;
}

/// <summary>
/// Converts typed SCL addressing into a deterministic, side-effect-free association plan.
/// Remote/called identity comes only from SCL. Local/calling identity comes only from the
/// explicit client profile. Missing, ambiguous or out-of-range remote identity fails closed.
/// </summary>
public static class SclAssistedMmsAssociationPlanBuilder
{
    public static SclAssistedMmsAssociationPlanResult BuildExact(
        SclMmsAccessPoint remote,
        MmsLocalAssociationProfile local)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(local);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(remote.Endpoint.IpAddress))
            errors.Add("SCL ConnectedAP does not declare an IP endpoint.");

        var remoteTsel = ParseSelector(remote.Association.TransportSelector, "OSI-TSEL", errors);
        var remoteSsel = ParseSelector(remote.Association.SessionSelector, "OSI-SSEL", errors);
        var remotePsel = ParseSelector(remote.Association.PresentationSelector, "OSI-PSEL", errors);
        var remoteApTitle = ParseApTitle(remote.Association.ApTitle, errors);

        if (!remote.Association.AeQualifier.HasValue ||
            remote.Association.AeQualifier.Value is < 0 or > 65535)
        {
            errors.Add("SCL ConnectedAP does not declare a valid OSI-AE-Qualifier in the range 0..65535.");
        }

        ValidateLocal(local, errors);

        if (errors.Count > 0)
        {
            return new SclAssistedMmsAssociationPlanResult
            {
                Errors = errors,
                Warnings = warnings
            };
        }

        var cotp = new CotpConnectParameters
        {
            SourceTsap = local.TransportSelector.ToArray(),
            DestinationTsap = remoteTsel!,
            TpduSizeExponent = local.TpduSizeExponent
        };

        var association = new AcseMmsAssociationRequestParameters
        {
            Calling = new AcseEndpointIdentity
            {
                PresentationSelector = local.PresentationSelector.ToArray(),
                SessionSelector = local.SessionSelector.ToArray(),
                ApTitle = local.ApTitle.ToArray(),
                AeQualifier = local.AeQualifier
            },
            Called = new AcseEndpointIdentity
            {
                PresentationSelector = remotePsel!,
                SessionSelector = remoteSsel!,
                ApTitle = remoteApTitle!,
                AeQualifier = remote.Association.AeQualifier!.Value
            },
            Initiate = new MmsInitiateRequestParameters
            {
                LocalDetailCalling = local.Initiate.LocalDetailCalling,
                MaxOutstandingCalling = local.Initiate.MaxOutstandingCalling,
                MaxOutstandingCalled = local.Initiate.MaxOutstandingCalled,
                NestingLevel = local.Initiate.NestingLevel
            }
        };

        return new SclAssistedMmsAssociationPlanResult
        {
            Plan = new SclAssistedMmsAssociationPlan
            {
                Host = remote.Endpoint.IpAddress,
                Port = 102,
                IedName = remote.IedName,
                AccessPointName = remote.AccessPointName,
                LocalProfileName = local.Name,
                Cotp = cotp,
                Association = association,
                CotpConnectRequest = CotpConnectRequest.Build(cotp),
                SessionPresentationAcseMmsRequest = AcseMmsAssociationRequestBuilder.BuildSessionConnect(association)
            },
            Errors = errors,
            Warnings = warnings
        };
    }

    private static void ValidateLocal(MmsLocalAssociationProfile local, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(local.Name))
            errors.Add("Local association profile must have a stable name.");
        if (local.TransportSelector.Length is < 1 or > 16)
            errors.Add("Local transport selector must contain 1 to 16 byte(s).");
        if (local.SessionSelector.Length is < 1 or > 16)
            errors.Add("Local session selector must contain 1 to 16 byte(s).");
        if (local.PresentationSelector.Length is < 1 or > 16)
            errors.Add("Local presentation selector must contain 1 to 16 byte(s).");
        if (local.ApTitle.Length < 2)
            errors.Add("Local AP-title must contain at least two OID arcs.");
        if (local.AeQualifier is < 0 or > 65535)
            errors.Add("Local AE qualifier must be in the range 0..65535.");
    }

    private static byte[]? ParseSelector(string text, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add($"SCL ConnectedAP does not declare {name}.");
            return null;
        }

        var compact = text.Trim();
        if (compact.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            compact = compact[2..];
        compact = compact
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal);

        if (compact.Length == 0 || (compact.Length & 1) != 0 || compact.Length > 32)
        {
            errors.Add($"SCL {name} '{text}' is not a 1-to-16-byte hexadecimal selector.");
            return null;
        }

        var bytes = new byte[compact.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(compact.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                errors.Add($"SCL {name} '{text}' contains non-hexadecimal data.");
                return null;
            }
        }

        return bytes;
    }

    private static uint[]? ParseApTitle(string text, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add("SCL ConnectedAP does not declare OSI-AP-Title.");
            return null;
        }

        var tokens = text.Split([',', '.', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            errors.Add($"SCL OSI-AP-Title '{text}' does not contain a valid OID.");
            return null;
        }

        var arcs = new uint[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!uint.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out arcs[i]))
            {
                errors.Add($"SCL OSI-AP-Title '{text}' contains an invalid OID arc '{tokens[i]}'.");
                return null;
            }
        }

        if (arcs[0] > 2 || (arcs[0] < 2 && arcs[1] > 39))
        {
            errors.Add($"SCL OSI-AP-Title '{text}' violates ASN.1 object-identifier arc rules.");
            return null;
        }

        return arcs;
    }
}
