using AR.Iec61850.Asn1;
using AR.Iec61850.Ethernet;

namespace AR.Iec61850.SampledValues;

public static class SampledValuesFrameBuilder
{
    private const int SavPduApplicationTag = 0;

    public static byte[] BuildEthernetFrame(SampledValuesFrame frame)
    {
        var apdu = EncodePdu(frame.Pdu);
        var ethernet = ProcessBusFrameCodec.EncodeEthernetFrame(
            frame.Destination,
            frame.Source,
            EthernetConstants.SampledValuesEtherType,
            frame.AppId,
            apdu,
            frame.Vlan,
            frame.Reserved1,
            frame.Reserved2);

        return EthernetFrameCodec.Encode(ethernet);
    }

    public static byte[] EncodePdu(SampledValuesPdu pdu)
    {
        if (pdu.Asdus.Count > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(pdu), "A sampled value PDU contains too many ASDUs.");

        var content = new BerWriter();
        content.WriteTlv(ContextPrimitive(0), BerWriter.EncodeUnsignedInteger((ulong)pdu.Asdus.Count));
        content.WriteTlv(ContextConstructed(2), EncodeAsduSequence(pdu.Asdus));

        return BerWriter.EncodeTlv(
            BerClass.Application,
            constructed: true,
            SavPduApplicationTag,
            content.ToArray());
    }

    private static byte[] EncodeAsduSequence(IEnumerable<SampledValueAsdu> asdus)
    {
        var writer = new BerWriter();

        foreach (var asdu in asdus)
        {
            writer.WriteTlv(0x30, EncodeAsdu(asdu));
        }

        return writer.ToArray();
    }

    private static byte[] EncodeAsdu(SampledValueAsdu asdu)
    {
        var writer = new BerWriter();

        writer.WriteTlv(ContextPrimitive(0), BerWriter.EncodeAscii(asdu.SvId));

        if (!string.IsNullOrWhiteSpace(asdu.DataSetReference))
            writer.WriteTlv(ContextPrimitive(1), BerWriter.EncodeAscii(asdu.DataSetReference));

        writer.WriteTlv(ContextPrimitive(2), BerWriter.EncodeUnsignedInteger(asdu.SampleCount));
        writer.WriteTlv(ContextPrimitive(3), BerWriter.EncodeUnsignedInteger(asdu.ConfigurationRevision));

        if (asdu.ReferenceTime is { } referenceTime)
            writer.WriteTlv(ContextPrimitive(4), BerWriter.EncodeUtcTime(referenceTime.Value, referenceTime.Quality));

        writer.WriteTlv(ContextPrimitive(5), BerWriter.EncodeUnsignedInteger(asdu.SampleSynchronization));

        if (asdu.SampleRate is { } sampleRate)
            writer.WriteTlv(ContextPrimitive(6), BerWriter.EncodeUnsignedInteger(sampleRate));

        writer.WriteTlv(ContextPrimitive(7), asdu.SamplePayload);

        if (asdu.SampleMode is { } sampleMode)
            writer.WriteTlv(ContextPrimitive(8), BerWriter.EncodeUnsignedInteger(sampleMode));

        return writer.ToArray();
    }

    private static byte ContextPrimitive(int tagNumber)
        => BerWriter.EncodeIdentifier(BerClass.ContextSpecific, constructed: false, tagNumber);

    private static byte ContextConstructed(int tagNumber)
        => BerWriter.EncodeIdentifier(BerClass.ContextSpecific, constructed: true, tagNumber);
}
