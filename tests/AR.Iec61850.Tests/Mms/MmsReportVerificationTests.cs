using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportVerificationTests
{
    [Fact]
    public void Verification_status_is_pass_when_all_checks_pass()
    {
        var verification = new MmsReportSessionVerification
        {
            Checks =
            [
                new MmsReportVerificationCheck
                {
                    Stage = "after-cleanup",
                    Target = "LD0/LLN0.BR.brcbA01.RptEna",
                    Expected = "false",
                    Observed = "false",
                    Severity = MmsReportVerificationSeverity.Pass,
                    Message = "verified"
                }
            ]
        };

        Assert.Equal("PASS", verification.OverallStatus);
        Assert.Equal(1, verification.PassCount);
        Assert.Equal(0, verification.WarningCount);
        Assert.Equal(0, verification.FailureCount);
    }

    [Fact]
    public void Verification_status_prioritizes_fail_over_warning()
    {
        var verification = new MmsReportSessionVerification
        {
            Checks =
            [
                new MmsReportVerificationCheck { Severity = MmsReportVerificationSeverity.Warning },
                new MmsReportVerificationCheck { Severity = MmsReportVerificationSeverity.Fail }
            ]
        };

        Assert.Equal("FAIL", verification.OverallStatus);
        Assert.Equal(1, verification.WarningCount);
        Assert.Equal(1, verification.FailureCount);
    }

    [Fact]
    public void Verification_status_returns_warning_when_no_checks_fail()
    {
        var verification = new MmsReportSessionVerification
        {
            Checks =
            [
                new MmsReportVerificationCheck { Severity = MmsReportVerificationSeverity.Pass },
                new MmsReportVerificationCheck { Severity = MmsReportVerificationSeverity.Warning }
            ]
        };

        Assert.Equal("PASS_WITH_WARNING", verification.OverallStatus);
        Assert.Equal(1, verification.PassCount);
        Assert.Equal(1, verification.WarningCount);
        Assert.Equal(0, verification.FailureCount);
    }
}
