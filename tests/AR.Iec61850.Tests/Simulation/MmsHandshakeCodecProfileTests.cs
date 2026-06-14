using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public class MmsHandshakeCodecProfileTests
{
    [Fact]
    public void Builder_CreatesReadyHandshakeCodecProfile()
    {
        var profile = new MmsHandshakeCodecProfileBuilder().BuildDefault();

        Assert.True(profile.IsServerTransportReady, string.Join("; ", profile.Findings));
        Assert.Empty(profile.Findings);
        Assert.Contains(profile.Steps, x => x.Area == "TPKT" && x.IsPass);
        Assert.Contains(profile.Steps, x => x.Area == "COTP-CR" && x.IsPass);
        Assert.Contains(profile.Steps, x => x.Area == "COTP-CC" && x.IsPass);
        Assert.Contains(profile.Steps, x => x.Area.StartsWith("ACSE:", StringComparison.Ordinal) && x.IsPass);
    }

    [Fact]
    public void ToMarkdown_RendersEvidence()
    {
        var profile = new MmsHandshakeCodecProfileBuilder().BuildDefault();

        var markdown = profile.ToMarkdown();

        Assert.Contains("MMS Handshake Codec Profile", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Server transport readiness", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COTP-CR", markdown, StringComparison.OrdinalIgnoreCase);
    }
}
