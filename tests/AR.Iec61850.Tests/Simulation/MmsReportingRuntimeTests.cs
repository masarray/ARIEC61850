using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;
using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class MmsReportingRuntimeTests
{
    [Fact]
    public void Dispatcher_AcknowledgesConcludeWithConcludeResponsePdu()
    {
        var session = CreateSession();
        var concludeRequest = MmsPresentation.WrapIsoPresentationPData(
            BerWriter.EncodeTlv(0x8B, ReadOnlySpan<byte>.Empty));

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(concludeRequest, session);
        var responseMms = MmsPresentation.StripPresentationPrefix(dispatch.ResponsePresentationPayload);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.Equal(MmsReadOnlyOperation.Conclude, dispatch.Request.Operation);
        Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);
        Assert.NotEmpty(responseMms.ToArray());
        Assert.Equal((byte)0x8C, responseMms[0]);
    }

    [Fact]
    public void Dispatcher_ReturnsIdentifyResponseWithMatchingInvokeId()
    {
        const int invokeId = 41;
        var identifyService = BerWriter.EncodeTlv(0x82, ReadOnlySpan<byte>.Empty);
        var confirmedRequest = BerWriter.EncodeTlv(
            0xA0,
            Concat(
                BerWriter.EncodeTlv(0x02, BerWriter.EncodeUnsignedInteger(invokeId)),
                identifyService));
        var request = MmsPresentation.WrapIsoPresentationPData(confirmedRequest);

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, CreateSession());
        var responseMms = MmsPresentation.StripPresentationPrefix(dispatch.ResponsePresentationPayload);
        var offset = 0;

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.Equal(MmsReadOnlyOperation.Identify, dispatch.Request.Operation);
        Assert.True(BerReader.TryReadTlv(responseMms, ref offset, out var response));
        Assert.Equal(0xA1, response.EncodedTag);
        var children = BerReader.ReadChildren(response.Value);
        Assert.Equal((ulong)invokeId, BerReader.ReadUnsignedInteger(children[0])!.Value);
        Assert.Contains(children, child => child.EncodedTag == 0xA2);
    }

    [Fact]
    public void Dispatcher_MultiVariableReadReturnsOneAccessResultPerVariable()
    {
        const int invokeId = 42;
        var references = new[]
        {
            new MmsObjectReference("IED1LD0", "XCBR1$ST$Pos$stVal", "ST"),
            new MmsObjectReference("IED1LD0", "PTOC1$ST$Op$general", "ST")
        };
        var variableEntries = references.Select(reference =>
            BerWriter.EncodeTlv(
                0x30,
                BerWriter.EncodeTlv(
                    0xA0,
                    BerWriter.EncodeTlv(
                        0xA1,
                        Concat(
                            MmsPresentation.VisibleString(reference.Domain),
                            MmsPresentation.VisibleString(reference.Item))))));
        var listOfVariable = BerWriter.EncodeTlv(0xA0, Concat(variableEntries.ToArray()));
        var variableAccessSpecification = BerWriter.EncodeTlv(0xA1, listOfVariable);
        var readService = BerWriter.EncodeTlv(0xA4, variableAccessSpecification);
        var confirmedRequest = BerWriter.EncodeTlv(
            0xA0,
            Concat(
                BerWriter.EncodeTlv(0x02, BerWriter.EncodeUnsignedInteger(invokeId)),
                readService));

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(
            MmsPresentation.WrapIsoPresentationPData(confirmedRequest),
            CreateSession());
        var responseMms = MmsPresentation.StripPresentationPrefix(dispatch.ResponsePresentationPayload);
        var offset = 0;

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.True(BerReader.TryReadTlv(responseMms, ref offset, out var response));
        var responseChildren = BerReader.ReadChildren(response.Value);
        var readResponse = Assert.Single(responseChildren, child => child.EncodedTag == 0xA4);
        var accessResultList = Assert.Single(BerReader.ReadChildren(readResponse.Value), child => child.EncodedTag == 0xA1);
        Assert.Equal(references.Length, BerReader.ReadChildren(accessResultList.Value).Count);
    }

    [Fact]
    public async Task ReportingRuntime_RequiresExplicitGiAndEmitsStandardReasonBits()
    {
        var sentReport = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runtime = new MmsAssociationReportingRuntime(
            () => CreateSession(),
            (payload, _) =>
            {
                sentReport.TrySetResult(payload);
                return Task.CompletedTask;
            });
        var state = Assert.Single(runtime.States, candidate => !candidate.Definition.Buffered);

        Assert.True(runtime.TryWriteRcbAttribute(
            $"{state.MmsReference}$RptEna",
            MmsDataValue.Boolean(true),
            out var enableError));
        Assert.Equal(0, enableError);

        await Task.Delay(100);
        Assert.False(sentReport.Task.IsCompleted);

        Assert.True(runtime.TryWriteRcbAttribute(
            $"{state.MmsReference}$GI",
            MmsDataValue.Boolean(true),
            out var giError));
        Assert.Equal(0, giError);

        var payload = await sentReport.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var report = MmsInformationReportDecoder.Decode(payload);
        var reason = report.Items.Last(item => item.Value?.Kind == MmsDataKind.BitString).Value!;

        Assert.True(report.IsSuccess, report.Message);
        Assert.Equal((byte)2, reason.RawValue[0]);
        Assert.Equal((byte)0x08, reason.RawValue[1]);
    }

    [Fact]
    public async Task ReportingRuntime_DataChangeTriggerEmitsUnsolicitedReportWithoutClientPolling()
    {
        var simulatorProfile = IedSimulatorProfile.CreateDefaultFeederProfile();
        var engine = new IedSimulatorEngine(simulatorProfile);
        var builder = new MmsReadOnlyServerModelBuilder();
        MmsReadOnlyServerSession SessionFactory()
            => new(builder.Build(simulatorProfile, engine.CreateSnapshot(DateTimeOffset.UtcNow)));

        var sentReport = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runtime = new MmsAssociationReportingRuntime(
            SessionFactory,
            (payload, _) =>
            {
                sentReport.TrySetResult(payload);
                return Task.CompletedTask;
            });
        var state = runtime.States.First(candidate =>
            MmsReportControlBlockLayout.TriggerDataChange(candidate.TrgOps));

        Assert.True(runtime.TryWriteRcbAttribute(
            $"{state.MmsReference}$RptEna",
            MmsDataValue.Boolean(true),
            out var enableError));
        Assert.Equal(0, enableError);

        await Task.Delay(120); // allow baseline capture; no report should be generated by enabling alone.
        Assert.False(sentReport.Task.IsCompleted);

        var dataSet = simulatorProfile.DataSets.Single(ds =>
            string.Equals(ds.Reference, state.Definition.DataSetReference, StringComparison.OrdinalIgnoreCase));
        var member = dataSet.Members[0];
        var slash = member.IndexOf('/');
        var pointReference = slash >= 0 ? member[(slash + 1)..] : member;
        Assert.True(engine.TryGetPointState(pointReference, out var pointState));
        pointState.Value = string.Equals(pointState.Value, "true", StringComparison.OrdinalIgnoreCase) ? "false" : "987.654";
        pointState.Quality = "valid";
        pointState.TimestampUtc = DateTimeOffset.UtcNow;
        pointState.Reason = "data-change";

        var payload = await sentReport.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var report = MmsInformationReportDecoder.Decode(payload);
        var reason = report.Items.Last(item => item.Value?.Kind == MmsDataKind.BitString).Value!;

        Assert.True(report.IsSuccess, report.Message);
        Assert.Equal((byte)2, reason.RawValue[0]);
        Assert.Equal((byte)0x80, reason.RawValue[1]);
    }

    [Fact]
    public void ReportControlBlockLayout_UsesExpectedPackedListWidths()
    {
        var optionalFields = MmsReportControlBlockLayout.ParseOptionalFields(
            "seqNum, timeStamp, reasonCode, dataSet, dataRef, bufOvfl, entryID, confRev, segmentation");
        var triggerOptions = MmsReportControlBlockLayout.ParseTriggerOptions(
            "data-change, quality-change, data-update, integrity, GI");

        Assert.Equal(new byte[] { 0x7F, 0xC0 }, optionalFields);
        Assert.Equal((byte)0x7C, triggerOptions);
        Assert.True(MmsReportControlBlockLayout.TriggerDataChange(triggerOptions));
    }

    private static MmsReadOnlyServerSession CreateSession()
    {
        var simulatorProfile = IedSimulatorProfile.CreateDefaultFeederProfile();
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(simulatorProfile);
        return new MmsReadOnlyServerSession(serverProfile);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }
}
