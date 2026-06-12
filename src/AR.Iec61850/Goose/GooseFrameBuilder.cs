using AR.Iec61850.Asn1;
using AR.Iec61850.Ethernet;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Goose;

public static class GooseFrameBuilder
{
    private const int GoosePduApplicationTag = 1;

    public static byte[] BuildEthernetFrame(GooseFrame frame)
    {
        var apdu = EncodePdu(frame.Pdu);
        var ethernet = ProcessBusFrameCodec.EncodeEthernetFrame(
            frame.Destination,
            frame.Source,
            EthernetConstants.GooseEtherType,
            frame.AppId,
            apdu,
            frame.Vlan,
            frame.Reserved1,
            frame.Reserved2);

        return EthernetFrameCodec.Encode(ethernet);
    }

    public static byte[] EncodePdu(GoosePdu pdu)
    {
        var content = new BerWriter();

        content.WriteTlv(ContextPrimitive(0), BerWriter.EncodeAscii(pdu.GoCbRef));
        content.WriteTlv(ContextPrimitive(1), BerWriter.EncodeUnsignedInteger(pdu.TimeAllowedToLiveMilliseconds));
        content.WriteTlv(ContextPrimitive(2), BerWriter.EncodeAscii(pdu.DataSetReference));
        content.WriteTlv(ContextPrimitive(3), BerWriter.EncodeAscii(pdu.GoId));
        content.WriteTlv(ContextPrimitive(4), BerWriter.EncodeUtcTime(pdu.Timestamp.Value, pdu.Timestamp.Quality));
        content.WriteTlv(ContextPrimitive(5), BerWriter.EncodeUnsignedInteger(pdu.StateNumber));
        content.WriteTlv(ContextPrimitive(6), BerWriter.EncodeUnsignedInteger(pdu.SequenceNumber));
        content.WriteTlv(ContextPrimitive(7), BerWriter.EncodeBoolean(pdu.Test));
        content.WriteTlv(ContextPrimitive(8), BerWriter.EncodeUnsignedInteger(pdu.ConfigurationRevision));
        content.WriteTlv(ContextPrimitive(9), BerWriter.EncodeBoolean(pdu.NeedsCommissioning));
        content.WriteTlv(ContextPrimitive(10), BerWriter.EncodeUnsignedInteger((ulong)pdu.Values.Count));
        content.WriteTlv(ContextConstructed(11), MmsDataCodec.EncodeAllData(pdu.Values));

        return BerWriter.EncodeTlv(
            BerClass.Application,
            constructed: true,
            GoosePduApplicationTag,
            content.ToArray());
    }

    private static byte ContextPrimitive(int tagNumber)
        => BerWriter.EncodeIdentifier(BerClass.ContextSpecific, constructed: false, tagNumber);

    private static byte ContextConstructed(int tagNumber)
        => BerWriter.EncodeIdentifier(BerClass.ContextSpecific, constructed: true, tagNumber);
}
