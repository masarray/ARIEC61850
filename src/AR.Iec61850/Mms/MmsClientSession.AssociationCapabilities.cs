using AR.Iec61850.Acse;
using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Decoded MMS InitiateResponse evidence for the current association. Unknown means
    /// the service bitmap was not available/decodable; it never means unsupported.
    /// </summary>
    public AcseMmsNegotiatedCapabilities LastNegotiatedCapabilities
    {
        get
        {
            var evidence = LastAssociationResponseHex;
            if (string.IsNullOrWhiteSpace(evidence))
                return AcseMmsNegotiatedCapabilities.Unknown;

            if (evidence.Contains("...", StringComparison.Ordinal))
            {
                return new AcseMmsNegotiatedCapabilities
                {
                    Diagnostic = "Stored association-response evidence was truncated; negotiated MMS services remain unknown."
                };
            }

            try
            {
                return AcseMmsNegotiatedCapabilitiesParser.Parse(HexDump.Parse(evidence));
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
            {
                return new AcseMmsNegotiatedCapabilities
                {
                    Diagnostic = $"Stored association-response evidence could not be decoded: {ex.GetType().Name}: {ex.Message}"
                };
            }
        }
    }
}
