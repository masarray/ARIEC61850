using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class SclStaticReportActivationPolicyTests
{
    [Fact]
    public void Brcb_Primary_Is_Read_Enable_And_Two_Readbacks()
    {
        var steps = SclStaticReportActivationPolicy.BuildPrimary(
            buffered: true,
            new[] { "RptEna", "ResvTms", "DatSet" });

        Assert.Equal(
            new[]
            {
                SclStaticReportActivationStepKind.ReadRcb,
                SclStaticReportActivationStepKind.EnableReport,
                SclStaticReportActivationStepKind.VerifyRcb,
                SclStaticReportActivationStepKind.VerifyRcb
            },
            steps);
    }

    [Fact]
    public void Urcb_Primary_Is_Read_Reserve_Enable_And_Two_Readbacks()
    {
        var steps = SclStaticReportActivationPolicy.BuildPrimary(
            buffered: false,
            new[] { "RptEna", "Resv", "DatSet" });

        Assert.Equal(
            new[]
            {
                SclStaticReportActivationStepKind.ReadRcb,
                SclStaticReportActivationStepKind.ReserveUrcb,
                SclStaticReportActivationStepKind.EnableReport,
                SclStaticReportActivationStepKind.VerifyRcb,
                SclStaticReportActivationStepKind.VerifyRcb
            },
            steps);
    }

    [Fact]
    public void Brcb_Explicit_Reservation_Is_Retry_Only()
    {
        var fallback = SclStaticReportActivationPolicy.BuildBrcbEnableFallback(
            buffered: true,
            new[] { "RptEna", "ResvTms" });

        Assert.Equal(
            new[]
            {
                SclStaticReportActivationStepKind.FallbackReserveBrcb,
                SclStaticReportActivationStepKind.EnableReport
            },
            fallback);
    }

    [Fact]
    public void Urcb_Has_No_Brcb_Reservation_Fallback()
    {
        var fallback = SclStaticReportActivationPolicy.BuildBrcbEnableFallback(
            buffered: false,
            new[] { "RptEna", "Resv" });

        Assert.Empty(fallback);
    }
}
