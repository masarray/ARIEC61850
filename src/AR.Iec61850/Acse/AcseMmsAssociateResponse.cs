using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Acse;

public sealed record AcseMmsAssociateResponseProfile(
    string Name,
    string Description,
    byte[] Payload,
    int MaxMmsPduSize,
    int MaxOutstandingCalling,
    int MaxOutstandingCalled,
    int DataStructureNestingLevel);

public static class AcseMmsAssociateResponse
{
    public static IReadOnlyList<AcseMmsAssociateResponseProfile> BuildResponseProfiles()
    {
        return
        [
            BuildDeterministicInitiateResponse(),
            BuildCompactInitiateResponse()
        ];
    }

    public static byte[] BuildDefaultResponsePayload()
        => BuildResponseProfiles()[0].Payload;

    public static AcseMmsAssociateResponseProfile Select(string? name)
    {
        var profiles = BuildResponseProfiles();
        if (string.IsNullOrWhiteSpace(name))
            return profiles[0];

        return profiles.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ?? profiles[0];
    }

    private static AcseMmsAssociateResponseProfile BuildDeterministicInitiateResponse()
    {
        // Deterministic ISO Session Accept + ACSE AARE + MMS InitiateResponse marker profile.
        // This profile is intentionally used as a clean-room loopback readiness payload, not as a
        // conformance claim for every vendor/client association option.
        var payload = HexDump.Parse(
            "0E 73 05 06 13 01 00 16 01 02 14 02 00 02 33 02 00 01 34 02 00 01 C1 5D " +
            "31 5B A0 03 80 01 01 A2 54 61 52 A1 07 06 05 28 CA 22 02 03 A2 03 02 01 00 " +
            "A3 05 A1 03 02 01 00 BE 3D 28 3B 06 02 51 01 02 01 03 A0 32 A9 30 80 03 00 FD E8 " +
            "81 01 0A 82 01 0A 83 01 05 A4 20 80 01 01 81 03 05 F1 00 82 18 03 EE 1C 00 " +
            "00 04 08 00 00 79 EF 00 00 04 08 00 00 79 EF");

        return new AcseMmsAssociateResponseProfile(
            "DeterministicInitiateResponse",
            "Loopback ACSE AARE profile carrying an MMS InitiateResponse marker and negotiated MMS limits.",
            payload,
            MaxMmsPduSize: 65000,
            MaxOutstandingCalling: 10,
            MaxOutstandingCalled: 10,
            DataStructureNestingLevel: 5);
    }

    private static AcseMmsAssociateResponseProfile BuildCompactInitiateResponse()
    {
        var payload = HexDump.Parse(
            "0E 41 05 06 13 01 00 16 01 02 14 02 00 02 33 02 00 01 34 02 00 01 C1 2B " +
            "31 29 A0 03 80 01 01 A2 22 61 20 A1 07 06 05 28 CA 22 02 03 A2 03 02 01 00 " +
            "BE 10 28 0E 06 02 51 01 02 01 03 A0 05 A9 03 80 01 01");

        return new AcseMmsAssociateResponseProfile(
            "CompactInitiateResponse",
            "Small ACSE AARE profile for transport-response smoke tests.",
            payload,
            MaxMmsPduSize: 65000,
            MaxOutstandingCalling: 10,
            MaxOutstandingCalled: 10,
            DataStructureNestingLevel: 5);
    }
}
