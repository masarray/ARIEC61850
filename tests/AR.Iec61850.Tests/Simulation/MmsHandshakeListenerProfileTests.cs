using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class MmsHandshakeListenerProfileTests
{
    [Fact]
    public async Task LoopbackProbe_Verifies_Tpkt_Cotp_And_Association_Payload()
    {
        var profile = await new MmsHandshakeListenerProfileBuilder().RunLoopbackProbeAsync(new MmsHandshakeListenerOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        Assert.True(profile.IsReady, string.Join("; ", profile.Findings));
        Assert.True(profile.BoundPort > 0);
        Assert.Equal(1, profile.AcceptedConnectionCount);
        Assert.True(profile.TpktExchangeVerified);
        Assert.True(profile.CotpConnectionConfirmed);
        Assert.True(profile.CotpDataObserved);
        Assert.True(profile.AssociationPayloadObserved);
        Assert.Contains(profile.Steps, x => x.Side == "server" && x.Layer == "COTP-RECV-CR" && x.IsPass);
        Assert.Contains(profile.Steps, x => x.Side == "client" && x.Layer == "COTP-RECV-CC" && x.IsPass);
        Assert.Contains(profile.Steps, x => x.Side == "server" && x.Layer == "ACSE-INSPECT" && x.IsPass);
    }

    [Fact]
    public async Task LoopbackProbe_Supports_Legacy_Association_Profile()
    {
        var profile = await new MmsHandshakeListenerProfileBuilder().RunLoopbackProbeAsync(new MmsHandshakeListenerOptions
        {
            Port = 0,
            AssociationProfileName = "LegacyMinimal",
            ProbeTimeoutMilliseconds = 5000
        });

        Assert.True(profile.IsReady, string.Join("; ", profile.Findings));
        Assert.Contains(profile.Steps, x => x.Layer == "COTP-SEND-DATA" && x.Message.Contains("LegacyMinimal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ToMarkdown_Renders_Transport_Gates()
    {
        var profile = await new MmsHandshakeListenerProfileBuilder().RunLoopbackProbeAsync(new MmsHandshakeListenerOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        var markdown = profile.ToMarkdown();

        Assert.Contains("MMS Handshake Listener Profile", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Transport gates", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COTP connection confirmed", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Association payload observed", markdown, StringComparison.OrdinalIgnoreCase);
    }
}
