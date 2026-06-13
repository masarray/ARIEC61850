using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsRcbContentionProbeEvidenceTests
{
    [Fact]
    public void Summary_DescribesCooldownSkipWhenContended()
    {
        var result = new MmsRcbContentionProbeResult
        {
            RcbReference = "LD0/LLN0.BR.brcbA01",
            IsContended = true,
            IsBusyAtProbe = true,
            IsFlapping = false,
            CooldownSeconds = 60,
            Decision = "CooldownSkip",
            Reason = "RCB became busy/reserved during pre-claim probes.",
            Observations =
            [
                new MmsRcbContentionProbeObservation
                {
                    ProbeNumber = 1,
                    CapturedAt = DateTimeOffset.UnixEpoch,
                    RcbReference = "LD0/LLN0.BR.brcbA01",
                    RptEna = "false",
                    ResvTms = "0",
                    DataSetReference = "LD0/LLN0.DataSet",
                    ConfRev = "1"
                },
                new MmsRcbContentionProbeObservation
                {
                    ProbeNumber = 2,
                    CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
                    RcbReference = "LD0/LLN0.BR.brcbA01",
                    RptEna = "true",
                    ResvTms = "42",
                    DataSetReference = "LD0/LLN0.DataSet",
                    ConfRev = "1"
                }
            ]
        };

        Assert.Contains("CooldownSkip", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contended=true", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.Observations.Count);
        Assert.Contains("RptEna=true", result.Observations[1].Summary, StringComparison.OrdinalIgnoreCase);
    }
}
