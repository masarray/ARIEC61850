using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportSessionDiagnosticsTests
{
    [Fact]
    public void Diagnostics_status_warns_on_buffer_overflow_without_failed_operation()
    {
        var report = new MmsReportFrame
        {
            Header = new MmsReportHeader
            {
                ReportId = "LD0/LLN0$BR$brcbA01",
                BufferOverflow = true,
                EntryIdHex = "0000000000000001",
                ConfRev = 1
            },
            InclusionBitstringItemIndex = 8,
            IncludedDataSetIndexes = [0],
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    ReasonForInclusion = ["application-trigger"]
                }
            ]
        };

        var diagnostics = MmsReportSessionDiagnostics.Analyze([report]);

        Assert.Equal("PASS_WITH_WARNING", diagnostics.OverallStatus);
        Assert.True(diagnostics.BufferOverflowObserved);
        Assert.Single(diagnostics.WarningMessages);
        Assert.Equal(0, diagnostics.MappingFailureCount);
    }

    [Fact]
    public void Diagnostics_status_fails_on_unmapped_report()
    {
        var report = new MmsReportFrame
        {
            Header = new MmsReportHeader { ReportId = "LD0/LLN0$BR$brcbA01" }
        };

        var diagnostics = MmsReportSessionDiagnostics.Analyze([report]);

        Assert.Equal("FAIL", diagnostics.OverallStatus);
        Assert.Equal(1, diagnostics.MappingFailureCount);
    }

    [Fact]
    public void Diagnostics_detects_partial_mapping_as_warning()
    {
        var report = new MmsReportFrame
        {
            Header = new MmsReportHeader { ReportId = "LD0/LLN0$BR$brcbA01", ConfRev = 1 },
            InclusionBitstringItemIndex = 8,
            IncludedDataSetIndexes = [0, 1],
            Values = [new MmsReportValue { Index = 0 }]
        };

        var diagnostics = MmsReportSessionDiagnostics.Analyze([report]);

        Assert.Equal("PASS_WITH_WARNING", diagnostics.OverallStatus);
        Assert.Equal(1, diagnostics.PartialMappingFailureCount);
        Assert.Single(diagnostics.WarningMessages);
    }
}
