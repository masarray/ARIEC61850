using AR.Iec61850.Asn1;
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
    private static readonly byte[] BerTransferSyntaxName = [0x51, 0x01];

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

    public static AcseMmsAssociateResponseProfile SelectForRequest(string? name, ReadOnlySpan<byte> clientAssociateRequest)
    {
        var profile = Select(name);
        if (!TryBuildSessionMirroredPayload(clientAssociateRequest, out var payload))
            return profile;

        return profile with
        {
            Name = $"{profile.Name}+SessionMirror",
            Description = $"{profile.Description} Session Accept parameters are mirrored from the client CN SPDU.",
            Payload = payload
        };
    }

    private static AcseMmsAssociateResponseProfile BuildDeterministicInitiateResponse()
    {
        var payload = BuildSessionAccept(
            BuildDefaultSessionParameters(),
            BuildPresentationAarePayload(BuildDetailedMmsInitiateResponse(), BuildDefaultPresentationNegotiation()));

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

    private static byte[] BuildDetailedMmsInitiateResponse()
    {
        var detail = Concat(
            BerWriter.EncodeTlv(0x80, [0x01]),
            BerWriter.EncodeTlv(0x81, [0x05, 0xF1, 0x00]),
            BerWriter.EncodeTlv(0x82, [0x03, 0xEE, 0x1C, 0x00, 0x00, 0x04, 0x08, 0x00, 0x00, 0x79, 0xEF, 0x18]));

        return BerWriter.EncodeTlv(0xA9, Concat(
            BerWriter.EncodeTlv(0x80, [0x00, 0xFD, 0xE8]),
            BerWriter.EncodeTlv(0x81, [0x0A]),
            BerWriter.EncodeTlv(0x82, [0x0A]),
            BerWriter.EncodeTlv(0x83, [0x05]),
            BerWriter.EncodeTlv(0xA4, detail)));
    }

    private static byte[] BuildPresentationAarePayload(byte[] mmsInitiateResponse, PresentationNegotiation negotiation)
    {
        var external = BerWriter.EncodeTlv(0x28, Concat(
            BerWriter.EncodeTlv(0x06, [0x51, 0x01]),
            BerWriter.EncodeTlv(0x02, [0x03]),
            BerWriter.EncodeTlv(0xA0, mmsInitiateResponse)));

        var acseAare = BerWriter.EncodeTlv(0x61, Concat(
            BerWriter.EncodeTlv(0xA1, BerWriter.EncodeTlv(0x06, [0x28, 0xCA, 0x22, 0x02, 0x03])),
            BerWriter.EncodeTlv(0xA2, BerWriter.EncodeTlv(0x02, [0x00])),
            BerWriter.EncodeTlv(0xA3, BerWriter.EncodeTlv(0xA1, BerWriter.EncodeTlv(0x02, [0x00]))),
            BerWriter.EncodeTlv(0xBE, external)));

        var fullyEncodedData = BerWriter.EncodeTlv(0x61, BerWriter.EncodeTlv(0x30, Concat(
            EncodeInteger(negotiation.AcsePresentationContextId),
            BerWriter.EncodeTlv(0xA0, acseAare))));

        return BerWriter.EncodeTlv(0x31, Concat(
            BerWriter.EncodeTlv(0xA0, BerWriter.EncodeTlv(0x80, [0x01])),
            BerWriter.EncodeTlv(0xA2, Concat(
                BuildPresentationContextResultList(negotiation.Contexts),
                fullyEncodedData))));
    }

    private static byte[] BuildDefaultSessionParameters()
        => [0x05, 0x06, 0x13, 0x01, 0x00, 0x16, 0x01, 0x02, 0x14, 0x02, 0x00, 0x02, 0x33, 0x02, 0x00, 0x01, 0x34, 0x02, 0x00, 0x01];

    private static byte[] BuildSessionAccept(ReadOnlySpan<byte> sessionParameters, ReadOnlySpan<byte> presentationPayload)
    {
        var sessionAcceptLength = checked(sessionParameters.Length + 2 + presentationPayload.Length);
        if (sessionAcceptLength > byte.MaxValue || presentationPayload.Length > byte.MaxValue)
            throw new InvalidOperationException("The ACSE Session Accept profile exceeds one-byte SPDU length encoding.");

        var payload = new byte[sessionAcceptLength + 2];
        payload[0] = 0x0E;
        payload[1] = (byte)sessionAcceptLength;
        sessionParameters.CopyTo(payload.AsSpan(2));

        var userDataOffset = 2 + sessionParameters.Length;
        payload[userDataOffset] = 0xC1;
        payload[userDataOffset + 1] = (byte)presentationPayload.Length;
        presentationPayload.CopyTo(payload.AsSpan(userDataOffset + 2));
        return payload;
    }

    private static bool TryBuildSessionMirroredPayload(
        ReadOnlySpan<byte> clientAssociateRequest,
        out byte[] payload)
    {
        payload = Array.Empty<byte>();

        if (!TryFindSessionUserData(clientAssociateRequest, expectedSpdu: 0x0D, out var requestUserDataOffset, out var requestUserDataLength))
            return false;

        var sessionParameterLength = requestUserDataOffset - 2;
        if (sessionParameterLength < 0)
            return false;

        var presentationRequest = clientAssociateRequest.Slice(requestUserDataOffset + 2, requestUserDataLength);
        var negotiation = TryParsePresentationNegotiation(presentationRequest, out var parsedNegotiation)
            ? parsedNegotiation
            : BuildDefaultPresentationNegotiation();
        var presentationPayload = BuildPresentationAarePayload(BuildDetailedMmsInitiateResponse(), negotiation);
        try
        {
            payload = BuildSessionAccept(clientAssociateRequest.Slice(2, sessionParameterLength), presentationPayload);
            return true;
        }
        catch (InvalidOperationException)
        {
            payload = Array.Empty<byte>();
            return false;
        }
    }

    private static byte[] BuildPresentationContextResultList(IReadOnlyList<PresentationContextResult> contexts)
    {
        var results = new byte[contexts.Count][];
        for (var i = 0; i < contexts.Count; i++)
        {
            results[i] = BerWriter.EncodeTlv(0x30, Concat(
                BerWriter.EncodeTlv(0x80, [0x00]),
                BerWriter.EncodeTlv(0x81, contexts[i].TransferSyntaxName)));
        }

        return BerWriter.EncodeTlv(0xA5, Concat(results));
    }

    private static PresentationNegotiation BuildDefaultPresentationNegotiation()
        => new(
            AcsePresentationContextId: 1,
            Contexts:
            [
                new PresentationContextResult(1, BerTransferSyntaxName),
                new PresentationContextResult(3, BerTransferSyntaxName)
            ]);

    private static bool TryParsePresentationNegotiation(ReadOnlySpan<byte> presentationRequest, out PresentationNegotiation negotiation)
    {
        negotiation = BuildDefaultPresentationNegotiation();

        try
        {
            var requestMemory = presentationRequest.ToArray().AsMemory();
            var offset = 0;
            if (!BerReader.TryReadTlv(requestMemory, ref offset, out var cpPpdu) || cpPpdu.EncodedTag != 0x31)
                return false;

            var contexts = new List<PresentationContextResult>();
            var acsePresentationContextId = 0;

            foreach (var item in BerReader.ReadChildren(cpPpdu.Value))
            {
                if (item.EncodedTag != 0xA2)
                    continue;

                foreach (var normalModeItem in BerReader.ReadChildren(item.Value))
                {
                    if (normalModeItem.EncodedTag == 0xA4)
                        contexts.AddRange(ParsePresentationContextDefinitions(normalModeItem.Value));
                    else if (normalModeItem.EncodedTag == 0x61 && TryReadFullyEncodedDataContextId(normalModeItem.Value, out var contextId))
                        acsePresentationContextId = contextId;
                }
            }

            if (contexts.Count == 0)
                return false;

            if (acsePresentationContextId == 0)
                acsePresentationContextId = contexts[0].Id;

            negotiation = new PresentationNegotiation(acsePresentationContextId, contexts);
            return true;
        }
        catch (BerFormatException)
        {
            return false;
        }
    }

    private static IReadOnlyList<PresentationContextResult> ParsePresentationContextDefinitions(ReadOnlyMemory<byte> contextDefinitionList)
    {
        var contexts = new List<PresentationContextResult>();
        foreach (var contextDefinition in BerReader.ReadChildren(contextDefinitionList))
        {
            if (contextDefinition.EncodedTag != 0x30)
                continue;

            var id = 0;
            byte[]? transferSyntaxName = null;
            foreach (var field in BerReader.ReadChildren(contextDefinition.Value))
            {
                if (field.EncodedTag == 0x02)
                    id = checked((int)(BerReader.ReadUnsignedInteger(field) ?? 0));
                else if (field.EncodedTag == 0x30)
                    transferSyntaxName = ReadFirstObjectIdentifierValue(field.Value);
            }

            if (id > 0)
                contexts.Add(new PresentationContextResult(id, transferSyntaxName ?? BerTransferSyntaxName));
        }

        return contexts;
    }

    private static byte[]? ReadFirstObjectIdentifierValue(ReadOnlyMemory<byte> transferSyntaxList)
    {
        foreach (var syntaxName in BerReader.ReadChildren(transferSyntaxList))
        {
            if (syntaxName.EncodedTag == 0x06)
                return syntaxName.Value.ToArray();
        }

        return null;
    }

    private static bool TryReadFullyEncodedDataContextId(ReadOnlyMemory<byte> fullyEncodedDataValue, out int contextId)
    {
        contextId = 0;

        foreach (var pdvList in BerReader.ReadChildren(fullyEncodedDataValue))
        {
            if (pdvList.EncodedTag != 0x30)
                continue;

            foreach (var item in BerReader.ReadChildren(pdvList.Value))
            {
                if (item.EncodedTag == 0x02)
                {
                    contextId = checked((int)(BerReader.ReadUnsignedInteger(item) ?? 0));
                    return contextId > 0;
                }
            }
        }

        return false;
    }

    private static byte[] EncodeInteger(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        if (value <= 0x7F)
            return BerWriter.EncodeTlv(0x02, [(byte)value]);

        if (value <= 0xFF)
            return BerWriter.EncodeTlv(0x02, [0x00, (byte)value]);

        return BerWriter.EncodeTlv(0x02, [(byte)(value >> 8), (byte)value]);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts)
            length += part.Length;

        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }

    private static bool TryFindSessionUserData(ReadOnlySpan<byte> spdu, byte expectedSpdu, out int offset, out int length)
    {
        offset = -1;
        length = 0;

        if (spdu.Length < 4 || spdu[0] != expectedSpdu)
            return false;

        var declaredLength = spdu[1];
        if (declaredLength + 2 != spdu.Length)
            return false;

        for (var i = 2; i + 2 <= spdu.Length; i++)
        {
            if (spdu[i] != 0xC1)
                continue;

            var candidateLength = spdu[i + 1];
            if (i + 2 + candidateLength != spdu.Length)
                continue;

            offset = i;
            length = candidateLength;
            return true;
        }

        return false;
    }

    private sealed record PresentationNegotiation(int AcsePresentationContextId, IReadOnlyList<PresentationContextResult> Contexts);

    private sealed record PresentationContextResult(int Id, byte[] TransferSyntaxName);
}
