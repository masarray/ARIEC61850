using AR.Iec61850.Asn1;
using AR.Iec61850.Ethernet;
using AR.Iec61850.Mms;

namespace AR.Iec61850.SampledValues;

public static class SampledValuesFrameParser
{
    private const int SavPduApplicationTag = 0;

    public static bool TryParseEthernetFrame(ReadOnlyMemory<byte> frameBytes, out SampledValuesFrame frame)
    {
        frame = null!;

        if (!EthernetFrameCodec.TryDecode(frameBytes, out var ethernet) ||
            ethernet.EtherType != EthernetConstants.SampledValuesEtherType ||
            !ProcessBusFrameCodec.TryDecode(ethernet, out var processBus))
        {
            return false;
        }

        if (!TryParsePdu(processBus.Apdu, out var pdu))
            return false;

        frame = new SampledValuesFrame
        {
            Destination = ethernet.Destination,
            Source = ethernet.Source,
            Vlan = ethernet.Vlan,
            AppId = processBus.AppId,
            Reserved1 = processBus.Reserved1,
            Reserved2 = processBus.Reserved2,
            Pdu = pdu
        };

        return true;
    }

    public static bool TryParsePdu(ReadOnlyMemory<byte> apdu, out SampledValuesPdu pdu)
    {
        pdu = null!;

        var offset = 0;
        if (!BerReader.TryReadTlv(apdu, ref offset, out var savPdu) ||
            savPdu.Class != BerClass.Application ||
            savPdu.TagNumber != SavPduApplicationTag ||
            !savPdu.Constructed)
        {
            return false;
        }

        ushort? noAsdu = null;
        var asdus = new List<SampledValueAsdu>();

        foreach (var field in BerReader.ReadChildren(savPdu.Value))
        {
            if (field.Class != BerClass.ContextSpecific)
                continue;

            switch (field.TagNumber)
            {
                case 0:
                    noAsdu = BerReader.ReadUInt16(field);
                    break;
                case 2:
                    if (!ReadAsduSequence(field.Value, asdus))
                        return false;
                    break;
            }
        }

        if (noAsdu.HasValue && noAsdu.Value != asdus.Count)
            return false;

        pdu = new SampledValuesPdu { Asdus = asdus };
        return true;
    }

    private static bool ReadAsduSequence(ReadOnlyMemory<byte> sequenceValue, ICollection<SampledValueAsdu> asdus)
    {
        foreach (var sequenceChild in BerReader.ReadChildren(sequenceValue))
        {
            if (sequenceChild.EncodedTag != 0x30 || !sequenceChild.Constructed)
                return false;

            asdus.Add(ReadAsdu(sequenceChild.Value));
        }

        return true;
    }

    private static SampledValueAsdu ReadAsdu(ReadOnlyMemory<byte> asduValue)
    {
        string svId = string.Empty;
        string dataSetReference = string.Empty;
        ushort sampleCount = 0;
        uint configurationRevision = 0;
        Iec61850UtcTime? referenceTime = null;
        byte sampleSynchronization = 0;
        ushort? sampleRate = null;
        ushort? sampleMode = null;
        byte[] samplePayload = [];

        foreach (var field in BerReader.ReadChildren(asduValue))
        {
            if (field.Class != BerClass.ContextSpecific)
                continue;

            switch (field.TagNumber)
            {
                case 0:
                    svId = BerReader.ReadAsciiString(field);
                    break;
                case 1:
                    dataSetReference = BerReader.ReadAsciiString(field);
                    break;
                case 2:
                    sampleCount = BerReader.ReadUInt16(field) ?? 0;
                    break;
                case 3:
                    configurationRevision = BerReader.ReadUInt32(field) ?? 0;
                    break;
                case 4:
                    referenceTime = Iec61850UtcTime.FromBytes(field.Value.Span);
                    break;
                case 5:
                    sampleSynchronization = (byte)(BerReader.ReadUnsignedInteger(field) ?? 0);
                    break;
                case 6:
                    sampleRate = BerReader.ReadUInt16(field);
                    break;
                case 7:
                    samplePayload = field.Value.ToArray();
                    break;
                case 8:
                    sampleMode = BerReader.ReadUInt16(field);
                    break;
            }
        }

        return new SampledValueAsdu
        {
            SvId = svId,
            DataSetReference = dataSetReference,
            SampleCount = sampleCount,
            ConfigurationRevision = configurationRevision,
            ReferenceTime = referenceTime,
            SampleSynchronization = sampleSynchronization,
            SampleRate = sampleRate,
            SampleMode = sampleMode,
            SamplePayload = samplePayload
        };
    }
}
