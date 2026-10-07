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
    [Fact]
    public void Diagnostics_treats_sequence_zero_after_nonzero_as_reset_warning_not_regression()
    {
        var reports = new[]
        {
            new MmsReportFrame
            {
                Header = new MmsReportHeader
                {
                    ReportId = "LD0/LLN0$BR$brcbA01",
                    DataSetReference = "LD0/LLN0$DataSet",
                    ConfRev = 1,
                    SequenceNumber = 1
                },
                InclusionBitstringItemIndex = 8,
                IncludedDataSetIndexes = [0],
                Values = [new MmsReportValue { Index = 0 }]
            },
            new MmsReportFrame
            {
                Header = new MmsReportHeader
                {
                    ReportId = "LD0/LLN0$BR$brcbA01",
                    DataSetReference = "LD0/LLN0$DataSet",
                    ConfRev = 1,
                    SequenceNumber = 0
                },
                InclusionBitstringItemIndex = 8,
                IncludedDataSetIndexes = [0],
                Values = [new MmsReportValue { Index = 0 }]
            }
        };

        var diagnostics = MmsReportSessionDiagnostics.Analyze(reports);

        Assert.Equal("PASS_WITH_WARNING", diagnostics.OverallStatus);
        Assert.Equal(1, diagnostics.SequenceResetCount);
        Assert.Equal(0, diagnostics.SequenceRegressionCount);
        Assert.Contains(diagnostics.WarningMessages, x => x.Contains("reset-to-zero", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Diagnostics_surfaces_structured_RCB_type_and_preflight_failures()
    {
        var writes = new[]
        {
            new MmsReportAttributeWriteStep
            {
                Attribute = "ResvTms",
                Reference = "LD0/LLN0.BR.B01.ResvTms",
                Attempted = true,
                IsSuccess = false,
                FailureCode = 7,
                FailureName = "type-inconsistent",
                FailureKind = MmsInteropFailureKind.TypeMismatch,
                RecoveryHint = MmsInteropRecoveryHint.RevalidateType,
                TypeEvidence = new MmsReportSemanticTypeEvidence
                {
                    Status = MmsReportSemanticTypeEvidenceStatus.Unavailable,
                    Attribute = "ResvTms",
                    ExpectedMmsType = "integer",
                    Message = "Live TypeSpecification unavailable."
                }
            },
            new MmsReportAttributeWriteStep
            {
                Attribute = "RptEna",
                Reference = "LD0/LLN0.BR.B01.RptEna",
                Attempted = false,
                IsSuccess = false,
                TypeEvidence = new MmsReportSemanticTypeEvidence
                {
                    Status = MmsReportSemanticTypeEvidenceStatus.ExactMismatch,
                    Attribute = "RptEna",
                    ExpectedMmsType = "boolean",
                    LiveMmsType = "integer"
                }
            },
            new MmsReportAttributeWriteStep
            {
                Attribute = "GI",
                Reference = "LD0/LLN0.BR.B01.GI",
                Attempted = true,
                IsSuccess = false,
                FailureCode = 3,
                FailureName = "object-access-denied",
                FailureKind = MmsInteropFailureKind.AccessDenied,
                RecoveryHint = MmsInteropRecoveryHint.FailClosed
            }
        };

        var diagnostics = MmsReportSessionDiagnostics.Analyze(
            Array.Empty<MmsReportFrame>(),
            writeSteps: writes);

        Assert.Equal("FAIL", diagnostics.OverallStatus);
        Assert.Equal(3, diagnostics.WriteFailureCount);
        Assert.Equal(1, diagnostics.WritePreflightBlockedCount);
        Assert.Equal(1, diagnostics.WriteTypeMismatchCount);
        Assert.Equal(1, diagnostics.WriteAccessDeniedCount);
        Assert.Equal(1, diagnostics.WriteTypeEvidenceConflictCount);
        Assert.Equal(1, diagnostics.WriteTypeEvidenceUnavailableCount);
        Assert.Contains("typeMismatch=1", diagnostics.Summary, StringComparison.Ordinal);
        Assert.Contains("preflightBlocked=1", diagnostics.Summary, StringComparison.Ordinal);
    }


}
