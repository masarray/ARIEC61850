using AR.Iec61850.Osi;

namespace AR.Iec61850.Tests.Osi;

public sealed class CotpLargeSegmentedResponseTests
{
    [Fact]
    public void Reassembles_More_Than_ThirtyTwo_Data_Tpdu_Fragments()
    {
        const int fragmentCount = 2_048;
        const int bytesPerFragment = 997;
        using var accumulator = new CotpDataSequenceAccumulator(
            maximumBytes: 4 * 1024 * 1024,
            maximumFragments: 4_096,
            maximumEmptyNonFinalFragments: 32);

        var expected = new byte[fragmentCount * bytesPerFragment];
        for (var fragment = 0; fragment < fragmentCount; fragment++)
        {
            var data = new byte[bytesPerFragment];
            for (var index = 0; index < data.Length; index++)
            {
                var value = (byte)((fragment + index) % 251);
                data[index] = value;
                expected[(fragment * bytesPerFragment) + index] = value;
            }

            accumulator.AppendTpktPayload(BuildDataTpdu(
                data,
                endOfTransmission: fragment == fragmentCount - 1));
        }

        var actual = accumulator.Complete();

        Assert.Equal(fragmentCount, accumulator.FragmentCount);
        Assert.Equal(expected.LongLength, accumulator.ReassembledBytes);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Rejects_Response_When_Bounded_Byte_Limit_Is_Exceeded()
    {
        using var accumulator = new CotpDataSequenceAccumulator(
            maximumBytes: 10,
            maximumFragments: 100,
            maximumEmptyNonFinalFragments: 10);

        accumulator.AppendTpktPayload(BuildDataTpdu(new byte[8], endOfTransmission: false));

        var exception = Assert.Throws<InvalidDataException>(() =>
            accumulator.AppendTpktPayload(BuildDataTpdu(new byte[3], endOfTransmission: true)));

        Assert.Contains("reassembly limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_Too_Many_Empty_NonFinal_Fragments()
    {
        using var accumulator = new CotpDataSequenceAccumulator(
            maximumBytes: 1024,
            maximumFragments: 100,
            maximumEmptyNonFinalFragments: 2);

        accumulator.AppendTpktPayload(BuildDataTpdu(Array.Empty<byte>(), endOfTransmission: false));
        accumulator.AppendTpktPayload(BuildDataTpdu(Array.Empty<byte>(), endOfTransmission: false));

        var exception = Assert.Throws<InvalidDataException>(() =>
            accumulator.AppendTpktPayload(BuildDataTpdu(Array.Empty<byte>(), endOfTransmission: false)));

        Assert.Contains("empty non-final", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildDataTpdu(byte[] userData, bool endOfTransmission)
    {
        var result = new byte[userData.Length + 3];
        result[0] = 0x02;
        result[1] = 0xF0;
        result[2] = endOfTransmission ? (byte)0x80 : (byte)0x00;
        userData.CopyTo(result, 3);
        return result;
    }
}