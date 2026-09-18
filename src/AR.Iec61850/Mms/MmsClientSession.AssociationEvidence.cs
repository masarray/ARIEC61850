using System.Globalization;
using AR.Iec61850.Acse;
using AR.Iec61850.Discovery;
using AR.Iec61850.Osi;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Projects the exact accepted built-in association request into canonical SCL
    /// communication evidence. AP-title, AE-qualifier, PSEL and SSEL are decoded from
    /// the request bytes that the IED actually accepted; TSEL comes from the exact COTP
    /// destination selector used by this built-in connection path. No exporter default
    /// or duplicated profile-name mapping is allowed here.
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
                : "AcceptedAssociationWireProfile",
            AssociationProfileName = profileName,
            Host = _lastHost?.Trim() ?? string.Empty,
            Port = _lastPort <= 0 ? 102 : _lastPort,
            AccessPointName = string.IsNullOrWhiteSpace(accessPointName) ? "AP1" : accessPointName.Trim(),
            Association = association
        };
    }

    private static SclIsoAssociationAddress ResolveAcceptedRemoteAssociation(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return new SclIsoAssociationAddress();

        var profile = AcseMmsInitiateRequest
            .BuildAssociationProfiles()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, profileName, StringComparison.Ordinal));

        if (profile is null)
            return new SclIsoAssociationAddress();

        var transportSelector = _cotp.LastConnectParameters?.DestinationTsap
            ?? Array.Empty<byte>();
        var wire = AcseAssociationRequestIdentityReader.Read(
            profile.Payload,
            transportSelector);

        return new SclIsoAssociationAddress
        {
            ApTitle = wire.CalledApTitleText,
            AeQualifierText = wire.CalledAeQualifier?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            AeQualifier = wire.CalledAeQualifier,
            PresentationSelector = ToSelectorHex(wire.CalledPresentationSelector),
            SessionSelector = ToSelectorHex(wire.CalledSessionSelector),
            TransportSelector = ToSelectorHex(wire.CalledTransportSelector)
        };
    }

    private static string ToSelectorHex(byte[] selector)
        => selector.Length == 0 ? string.Empty : Convert.ToHexString(selector);
}
