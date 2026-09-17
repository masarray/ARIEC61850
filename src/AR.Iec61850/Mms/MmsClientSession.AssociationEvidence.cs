using AR.Iec61850.Discovery;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Projects the exact accepted built-in association profile into canonical SCL
    /// communication evidence. This is session evidence, not an exporter default.
    /// Profiles that do not carry a qualified called AP-title remain explicitly
    /// unresolved so a safe-connection export can fail closed.
    /// </summary>
    public LiveIedCommunicationEvidence GetAcceptedCommunicationEvidence(string accessPointName = "AP1")
    {
        var accepted = LastAssociationAttempts.LastOrDefault(attempt => attempt.IsAccepted);
        var profileName = accepted?.ProfileName?.Trim() ?? string.Empty;
        var association = ResolveAcceptedRemoteAssociation(profileName);

        return new LiveIedCommunicationEvidence
        {
            Source = string.IsNullOrWhiteSpace(profileName)
                ? "AcceptedAssociationUnavailable"
                : "AcceptedAssociationProfile",
            AssociationProfileName = profileName,
            Host = _lastHost?.Trim() ?? string.Empty,
            Port = _lastPort <= 0 ? 102 : _lastPort,
            AccessPointName = string.IsNullOrWhiteSpace(accessPointName) ? "AP1" : accessPointName.Trim(),
            Association = association
        };
    }

    private static SclIsoAssociationAddress ResolveAcceptedRemoteAssociation(string profileName)
    {
        // These values describe the called/remote side encoded by the shipped runtime
        // association payloads. They are deliberately tied to the accepted profile name,
        // not to any IED identity or vendor. If a future profile changes its wire identity,
        // its evidence mapping must change with it and the round-trip regression will fail.
        if (string.Equals(profileName, "BalancedApTitle", StringComparison.Ordinal))
        {
            return new SclIsoAssociationAddress
            {
                ApTitle = "1,1,1,999,1",
                AeQualifierText = "12",
                AeQualifier = 12,
                PresentationSelector = "00000001",
                SessionSelector = "0001",
                TransportSelector = "0001"
            };
        }

        // LegacyMinimal intentionally does not provide a qualified called AP-title.
        // Exporting a guessed AP-title would create an SCL that looks valid but is not
        // evidence-backed, so leave it unresolved and let canonical export fail closed.
        return new SclIsoAssociationAddress
        {
            PresentationSelector = "00000001",
            SessionSelector = "0001",
            TransportSelector = "0001"
        };
    }
}
