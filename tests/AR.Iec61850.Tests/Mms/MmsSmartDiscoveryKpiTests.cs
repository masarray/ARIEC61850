using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsSmartDiscoveryKpiTests
{
    [Fact]
    public void EquivalentEvidence_DifferentCompletionOrder_ProducesSameSignature()
    {
        var first = new MmsSmartDiscoveryKpiRecorder(1);
        using (var a = first.BeginRequest("structure", "GetNameList", "NamedVariable|LD0|<first>"))
        using (var b = first.BeginRequest("type-enrichment", "GetVariableAccessAttributes", "LD0/XCBR1"))
        {
            b.Complete(true);
            a.Complete(true);
        }
        first.UpdateCompleteness(32, 119, 3465, 4925, 2, 2, 58, 32, 16, 16);

        var second = new MmsSmartDiscoveryKpiRecorder(2);
        using (var b = second.BeginRequest("type-enrichment", "GetVariableAccessAttributes", "LD0/XCBR1"))
        using (var a = second.BeginRequest("structure", "GetNameList", "NamedVariable|LD0|<first>"))
        {
            a.Complete(true);
            b.Complete(true);
        }
        second.UpdateCompleteness(32, 119, 3465, 4925, 2, 2, 58, 32, 16, 16);

        Assert.Equal(first.Snapshot().DeterministicSignature, second.Snapshot().DeterministicSignature);
    }

    [Fact]
    public void RepeatedSemanticRequest_IsCountedAsDuplicate()
    {
        var recorder = new MmsSmartDiscoveryKpiRecorder(1);

        using (var first = recorder.BeginRequest("structure", "GetNameList", "NamedVariable|LD0|<first>"))
            first.Complete(true);
        using (var second = recorder.BeginRequest("structure", "GetNameList", "NamedVariable|LD0|<first>"))
            second.Complete(true);

        var snapshot = recorder.Snapshot();
        var phase = Assert.Single(snapshot.Phases);

        Assert.Equal(2, snapshot.TotalRequests);
        Assert.Equal(2, snapshot.SuccessfulRequests);
        Assert.Equal(1, snapshot.DuplicateRequests);
        Assert.Equal(1, phase.DuplicateRequests);
    }

    [Fact]
    public void PeakOutstanding_TracksConcurrentRequestsWithoutChangingRequestCount()
    {
        var recorder = new MmsSmartDiscoveryKpiRecorder(1);

        using var first = recorder.BeginRequest("type-enrichment", "GetVariableAccessAttributes", "LD0/XCBR1");
        using var second = recorder.BeginRequest("type-enrichment", "GetVariableAccessAttributes", "LD0/XSWI1");
        using var third = recorder.BeginRequest("type-enrichment", "GetVariableAccessAttributes", "LD0/MMXU1");

        third.Complete(true);
        first.Complete(true);
        second.Complete(false);

        var snapshot = recorder.Snapshot();

        Assert.Equal(3, snapshot.TotalRequests);
        Assert.Equal(3, snapshot.PeakOutstandingRequests);
        Assert.Equal(2, snapshot.SuccessfulRequests);
        Assert.Equal(1, snapshot.FailedRequests);
        Assert.Equal(0, snapshot.DuplicateRequests);
    }

    [Fact]
    public void ModelRefresh_ChangesCompletenessAndSignatureWithoutAddingRequests()
    {
        var recorder = new MmsSmartDiscoveryKpiRecorder(1);
        recorder.UpdateCompleteness(32, 119, 3465, 3465, 2, 2, 58, 32, 16, 16);
        var before = recorder.Snapshot();

        recorder.UpdateModelCompleteness(32, 119, 4925);
        var after = recorder.Snapshot();

        Assert.Equal(0, before.TotalRequests);
        Assert.Equal(0, after.TotalRequests);
        Assert.Equal(4925, after.FcPointCount);
        Assert.NotEqual(before.DeterministicSignature, after.DeterministicSignature);
    }

    [Fact]
    public void PartialWireAccounting_IsExplicitAndDeterministic()
    {
        var first = new MmsSmartDiscoveryKpiRecorder(1);
        first.MarkAccountingPartial("Report-enrichment confirmed Reads are not individually observed");

        var second = new MmsSmartDiscoveryKpiRecorder(2);
        second.MarkAccountingPartial("Report-enrichment confirmed Reads are not individually observed");

        var firstSnapshot = first.Snapshot();
        var secondSnapshot = second.Snapshot();

        Assert.False(firstSnapshot.WireAccountingComplete);
        Assert.Single(firstSnapshot.AccountingNotes);
        Assert.Equal(firstSnapshot.DeterministicSignature, secondSnapshot.DeterministicSignature);
    }
}
