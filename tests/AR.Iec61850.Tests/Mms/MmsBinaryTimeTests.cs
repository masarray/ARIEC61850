using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsBinaryTimeTests
{
    [Fact]
    public void Six_octet_binary_time_decodes_to_iec_epoch_utc_timestamp()
    {
        var value = MmsBinaryTime.FromBytes(Convert.FromHexString("047D1E733C8F"));

        Assert.True(value.UtcValue.HasValue);
        Assert.Equal(2026, value.UtcValue.Value.Year);
        Assert.Equal(6, value.UtcValue.Value.Month);
        Assert.Equal(12, value.UtcValue.Value.Day);
        Assert.Contains("binary-time=047D1E733C8F", value.ToDisplayString());
    }

    [Fact]
    public void Renderer_shows_decoded_binary_time_with_raw_evidence()
    {
        var display = MmsDataValueRenderer.ToCompactString(MmsDataValue.BinaryTime(Convert.FromHexString("047D1E733C8F")));

        Assert.Contains("2026-06-12", display);
        Assert.Contains("binary-time=047D1E733C8F", display);
    }
}
