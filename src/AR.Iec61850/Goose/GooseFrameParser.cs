using AR.Iec61850.Asn1;
using AR.Iec61850.Ethernet;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Goose;

public static class GooseFrameParser
{
    private const int GoosePduApplicationTag = 1;

    public static bool TryParseEthernetFrame(ReadOnlyMemory<byte> frameBytes, out GooseFrame frame)
    {
        frame = null!;

        if (!EthernetFrameCodec.TryDecode(frameBytes, out var ethernet) ||
            ethernet.EtherType != EthernetConstants.GooseEtherType ||
            !ProcessBusFrameCodec.TryDecode(ethernet, out var processBus))
        {
            return false;
        }

        if (!TryParsePdu(processBus.Apdu, out var pdu))
            return false;

        frame = new GooseFrame
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

    public static bool TryParsePdu(ReadOnlyMemory<byte> apdu, out GoosePdu pdu)
    {
        pdu = null!;

        var offset = 0;
        if (!BerReader.TryReadTlv(apdu, ref offset, out var goosePdu) ||
            goosePdu.Class != BerClass.Application ||
            goosePdu.TagNumber != GoosePduApplicationTag ||
            !goosePdu.Constructed)
        {
            return false;
        }

        string goCbRef = string.Empty;
        uint timeAllowedToLive = 0;
        string dataSet = string.Empty;
        string goId = string.Empty;
        Iec61850UtcTime timestamp = new(DateTimeOffset.UnixEpoch, 0);
        uint stNum = 0;
        uint sqNum = 0;
        bool test = false;
        uint confRev = 0;
        bool needsCommissioning = false;
        uint numDataSetEntries = 0;
        IReadOnlyList<MmsDataValue> values = Array.Empty<MmsDataValue>();

        foreach (var field in BerReader.ReadChildren(goosePdu.Value))
        {
            if (field.Class != BerClass.ContextSpecific)
                continue;

            switch (field.TagNumber)
            {
                case 0:
                    goCbRef = BerReader.ReadAsciiString(field);
                    break;
                case 1:
                    timeAllowedToLive = BerReader.ReadUInt32(field) ?? 0;
                    break;
                case 2:
                    dataSet = BerReader.ReadAsciiString(field);
                    break;
                case 3:
                    goId = BerReader.ReadAsciiString(field);
                    break;
                case 4:
                    timestamp = Iec61850UtcTime.FromBytes(field.Value.Span);
                    break;
                case 5:
                    stNum = BerReader.ReadUInt32(field) ?? 0;
                    break;
                case 6:
                    sqNum = BerReader.ReadUInt32(field) ?? 0;
                    break;
                case 7:
                    test = BerReader.ReadBoolean(field) ?? false;
                    break;
                case 8:
                    confRev = BerReader.ReadUInt32(field) ?? 0;
                    break;
                case 9:
                    needsCommissioning = BerReader.ReadBoolean(field) ?? false;
                    break;
                case 10:
                    numDataSetEntries = BerReader.ReadUInt32(field) ?? 0;
                    break;
                case 11:
                    values = MmsDataCodec.DecodeAllData(field.Value);
                    break;
            }
        }

        if (numDataSetEntries != 0 && values.Count != numDataSetEntries)
            return false;

        pdu = new GoosePdu
        {
            GoCbRef = goCbRef,
            TimeAllowedToLiveMilliseconds = timeAllowedToLive,
            DataSetReference = dataSet,
            GoId = goId,
            Timestamp = timestamp,
            StateNumber = stNum,
            SequenceNumber = sqNum,
            Test = test,
            ConfigurationRevision = confRev,
            NeedsCommissioning = needsCommissioning,
            Values = values
        };

        return true;
    }
}
