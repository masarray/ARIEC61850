using AR.Iec61850.Osi;

namespace AR.Iec61850.Tests.Osi;

public class CotpConnectionConfirmTests
{
    [Fact]
    public void ParseAcceptsConnectionConfirm()
    {
        var confirm = CotpConnectionConfirm.Parse([0x06, 0xD0, 0x00, 0x01, 0x12, 0x34, 0x00]);

        Assert.True(confirm.IsAccepted);
        Assert.Equal(0xD0, confirm.TpduCode);
        Assert.Equal(0x0001, confirm.DestinationReference);
        Assert.Equal(0x1234, confirm.SourceReference);
    }

    [Fact]
    public void ParseRejectsDisconnectRequest()
    {
        var confirm = CotpConnectionConfirm.Parse([0x06, 0x80, 0x00, 0x01, 0x12, 0x34, 0x00]);

        Assert.False(confirm.IsAccepted);
        Assert.Equal(0x80, confirm.TpduCode);
    }
}
