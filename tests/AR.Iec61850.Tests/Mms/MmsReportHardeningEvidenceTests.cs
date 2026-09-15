using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsReportHardeningEvidenceTests
{
    [Fact]
    public void DataSet_Order_Validator_Accepts_Exact_Inclusion_Order()
    {
        var members = BuildMembers();
        var frame = BuildFrame(
            included: [0, 2],
            values:
            [
                ReportValue(0, members[0]),
                ReportValue(2, members[2])
            ]);

        var result = MmsReportDataSetOrderValidator.Validate(frame, members);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(2, result.IncludedMemberCount);
    }

    [Fact]
    public void DataSet_Order_Validator_Rejects_Reordered_Or_Mismatched_Projection()
    {
        var members = BuildMembers();
        var frame = BuildFrame(
            included: [2, 0],
            values:
            [
                ReportValue(0, members[0]),
                ReportValue(2, members[2])
            ]);

        var result = MmsReportDataSetOrderValidator.Validate(frame, members);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, x => x.Contains("not strictly increasing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Diagnostics, x => x.Contains("carries DataSet index", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sanitized_Lifecycle_Export_Omits_Live_Identifiers_EntryId_And_Free_Text()
    {
        var reportControl = new MmsReportControlCandidate
        {
            Buffered = true,
            Reference = "CUSTOMER_IED01LD0/LLN0.BR.Buffer01",
            DataSetReference = "CUSTOMER_IED01LD0/LLN0.Events"
        };
        var state = new MmsReportLifecycleStateMachine(new MmsReportSubscriptionPlan
        {
            Mode = MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = reportControl,
            DataSetReference = reportControl.DataSetReference,
            Members = BuildMembers()
        });
        state.ObserveReceiveResult(new MmsPersistentReportMonitorReceiveResult
        {
            Reports =
            [
                CustomerFrame("DEADBEEFCAFEB001", 76, "2026-09-15T01:02:03Z"),
                CustomerFrame("DEADBEEFCAFEB001", 76, "2026-09-15T01:02:03Z"),
                CustomerFrame("DEADBEEFCAFEB002", 77, "2026-09-15T01:02:04Z")
            ]
        });
        var snapshot = state.Snapshot();

        using var writer = new StringWriter();
        MmsReportLifecycleEvidenceExporter.WriteJson(snapshot, writer, indented: false);
        var json = writer.ToString();

        Assert.Contains("\"ReportControlKind\":\"Brcb\"", json, StringComparison.Ordinal);
        Assert.Contains("\"AcceptedReportCount\":2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CUSTOMER_IED01", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DEADBEEF", json, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-09-15", json, StringComparison.Ordinal);
    }

    private static IReadOnlyList<MmsDataSetDirectoryMember> BuildMembers()
        =>
        [
            Member("IED1LD0/MMXU1.TotW.mag.f"),
            Member("IED1LD0/MMXU1.Hz.mag.f"),
            Member("IED1LD0/MMXU1.A.phsA.cVal.mag.f")
        ];

    private static MmsDataSetDirectoryMember Member(string reference)
        => new()
        {
            Domain = "IED1LD0",
            MmsItemName = reference.Replace("IED1LD0/", string.Empty).Replace('.', '$'),
            UserReference = reference,
            FunctionalConstraint = "MX"
        };

    private static MmsReportValue ReportValue(int index, MmsDataSetDirectoryMember member)
        => new()
        {
            Index = index,
            Member = member,
            Value = MmsDataValue.FloatingPoint(1.0f),
            ReasonForInclusion = ["data-change"]
        };

    private static MmsReportFrame BuildFrame(IReadOnlyList<int> included, IReadOnlyList<MmsReportValue> values)
        => new()
        {
            Header = new MmsReportHeader
            {
                ReportId = "IED1LD0/LLN0$BR$Buffer01",
                DataSetReference = "IED1LD0/LLN0$Events",
                ConfRev = 1
            },
            IncludedDataSetIndexes = included,
            Values = values,
            DecoderMode = "iec61850-report"
        };

    private static MmsReportFrame CustomerFrame(string entryId, ulong sequence, string time)
        => new()
        {
            Header = new MmsReportHeader
            {
                ReportId = "CUSTOMER_IED01LD0/LLN0$BR$Buffer01",
                DataSetReference = "CUSTOMER_IED01LD0/LLN0$Events",
                ConfRev = 1,
                EntryIdHex = entryId,
                SequenceNumber = sequence,
                TimeOfEntry = time
            },
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    ReasonForInclusion = ["data-change"]
                }
            ]
        };
}
