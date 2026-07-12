using AR.Iec61850.Control;
using AR.Iec61850.Mms;
using AR.Iec61850.Asn1;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AR.Iec61850.Tests.Control;

public sealed class SmartControlStackTests
{
    [Theory]
    [InlineData("LD0/CSWI1.ctlModel")]
    [InlineData("LD0/CSWI1.Pos.Oper")]
    [InlineData("LD0/CSWI1.Pos.SBOw")]
    [InlineData("LD0/CSWI1.Pos.Cancel")]
    public void ControlRootParser_RejectsServiceLeaves(string reference)
        => Assert.Throws<ArgumentException>(() => Iec61850ControlObjectReferences.Parse(reference));

    [Fact]
    public void ControlRootParser_BuildsExactFcReferences()
    {
        var references = Iec61850ControlObjectReferences.Parse("LD0/CSWI1.Pos");

        Assert.Equal("CSWI1$CF$Pos$ctlModel", references.CtlModel.Item);
        Assert.Equal("CSWI1$CO$Pos$Oper", references.Oper.Item);
        Assert.Equal("CSWI1$CO$Pos$SBOw", references.SboWithValue.Item);
    }

    [Fact]
    public void DpcBinder_UsesExactTwoBitMmsValue()
    {
        var specification = Type("ctlVal", "bit-string", size: 2);

        var open = Iec61850ControlValueBinder.Bind(Iec61850ControlValue.Open(), specification);
        var close = Iec61850ControlValueBinder.Bind(Iec61850ControlValue.Close(), specification);

        Assert.Equal(MmsDataKind.BitString, open.Kind);
        Assert.Equal(new byte[] { 6, 0x40 }, open.RawValue);
        Assert.Equal(new byte[] { 6, 0x80 }, close.RawValue);
    }

    [Theory]
    [InlineData(0x40, Iec61850ControlStatusState.Open, "OPEN")]
    [InlineData(0x80, Iec61850ControlStatusState.Closed, "CLOSED")]
    public void DpcStatusInterpreter_MapsTwoBitStatus(byte encoded, Iec61850ControlStatusState expectedState, string expectedText)
    {
        var result = Iec61850ControlStatusInterpreter.Interpret(
            "LD0/CSWI1.Pos.stVal",
            "DPC",
            new MmsReadResult
            {
                IsSuccess = true,
                Value = MmsDataValue.BitString(6, new[] { encoded })
            });

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedState, result.State);
        Assert.Equal(expectedText, result.DisplayValue);
    }

    [Fact]
    public async Task ControlSession_ReadStatus_UsesDiscoveredStatusReference()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult
        {
            IsSuccess = true,
            Value = MmsDataValue.BitString(6, new byte[] { 0x40 })
        });
        await using var session = NewSession(transport, Iec61850ControlModel.DirectNormal);

        var result = await session.ReadStatusAsync();

        Assert.Equal(Iec61850ControlStatusState.Open, result.State);
        var read = Assert.Single(transport.Reads);
        Assert.Equal("CSWI1$ST$Pos$stVal", read.Item);
    }


    [Fact]
    public async Task ControlSession_ReadStatus_UsesDiscoveredMxFunctionalConstraint()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult
        {
            IsSuccess = true,
            Value = MmsDataValue.FloatingPoint(12.5)
        });
        await using var session = NewSession(
            transport,
            Iec61850ControlModel.DirectNormal,
            statusReference: "LD0/CSWI1.Pos.mag.f",
            statusFunctionalConstraint: "MX");

        var result = await session.ReadStatusAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(Iec61850ControlStatusState.Numeric, result.State);
        var read = Assert.Single(transport.Reads);
        Assert.Equal("CSWI1$MX$Pos$mag$f", read.Item);
    }

    [Fact]
    public void RawBinder_RejectsBitStringWithWrongLiveWidth()
    {
        var specification = Type("ctlVal", "bit-string", size: 2);
        var wrongWidth = Iec61850ControlValue.Raw(MmsDataValue.BitString(5, new byte[] { 0x20 })); // 3 bits

        var error = Assert.Throws<InvalidOperationException>(() =>
            Iec61850ControlValueBinder.Bind(wrongWidth, specification));

        Assert.Contains("size mismatch", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpcBinder_BindsBooleanWithoutNumericGuessing()
    {
        var value = Iec61850ControlValueBinder.Bind(Iec61850ControlValue.On(), Type("ctlVal", "boolean"));
        Assert.Equal(MmsDataKind.Boolean, value.Kind);
        Assert.Equal(true, value.Value);
    }

    [Fact]
    public void BscBinder_BindsValWithTransStructure()
    {
        var specification = Structure("ctlVal",
            Type("posVal", "integer"),
            Type("transInd", "boolean"));

        var value = Iec61850ControlValueBinder.Bind(Iec61850ControlValue.StepPosition(7, transient: true), specification);

        Assert.Equal(7L, value.Children[0].Value);
        Assert.Equal(true, value.Children[1].Value);
    }

    [Fact]
    public void ApcBinder_SelectsLiveFloatingAnalogueMember()
    {
        var specification = Structure("ctlVal",
            Type("i", "integer"),
            Type("f", "floating-point"));

        var value = Iec61850ControlValueBinder.Bind(Iec61850ControlValue.Analogue(12.5), specification);

        Assert.Equal(0L, value.Children[0].Value);
        Assert.Equal(12.5, value.Children[1].Value);
    }

    [Fact]
    public void ApcBinder_SelectsLiveIntegerAnalogueMember()
    {
        var specification = Structure("ctlVal",
            Type("i", "integer"),
            Type("f", "floating-point"));

        var value = Iec61850ControlValueBinder.Bind(Iec61850ControlValue.Integer(125), specification);

        Assert.Equal(125L, value.Children[0].Value);
        Assert.Equal(0f, value.Children[1].Value);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(-1L)]
    public void IncIscBinder_BindsRaiseAndLowerToExactInteger(long requested)
    {
        var command = requested > 0 ? Iec61850ControlValue.Raise() : Iec61850ControlValue.Lower();
        var value = Iec61850ControlValueBinder.Bind(command, Type("ctlVal", "integer"));

        Assert.Equal(requested, value.Value);
    }

    [Fact]
    public void StructureBuilder_PreservesCtlNumAndTimestampAcrossSboAndOperate()
    {
        var request = Request(Iec61850ControlValue.Close(), ctlNum: 41);
        var timestamp = new DateTimeOffset(2026, 7, 12, 1, 2, 3, TimeSpan.Zero);
        var context = Iec61850ControlStructureBuilder.CreateContext(request, Type("ctlVal", "bit-string", size: 2), 5, timestamp);
        var specification = CommandSpecification();

        var sbow = Iec61850ControlStructureBuilder.BuildSelectWithValue(context, specification, true);
        var oper = Iec61850ControlStructureBuilder.BuildOperate(context, specification, true);

        Assert.Equal(sbow.Children[3].Value, oper.Children[3].Value); // ctlNum
        Assert.Equal(sbow.Children[4].Value, oper.Children[4].Value); // T
        Assert.Equal(41UL, oper.Children[3].Value);
        Assert.Equal(timestamp, ((Iec61850UtcTime)oper.Children[4].Value!).Value);
    }

    [Fact]
    public void StructureBuilder_EncodesSynchroAndInterlockCheckBits()
    {
        var request = new Iec61850ControlRequest
        {
            ControlValue = Iec61850ControlValue.Close(),
            SynchroCheck = true,
            InterlockCheck = true
        };
        var context = Iec61850ControlStructureBuilder.CreateContext(request, Type("ctlVal", "bit-string", size: 2), 1, DateTimeOffset.UtcNow);
        var oper = Iec61850ControlStructureBuilder.BuildOperate(context, CommandSpecification(), true);

        Assert.Equal(new byte[] { 6, 0xC0 }, oper.Children[6].RawValue);
    }

    [Fact]
    public void StructureBuilder_PreservesTestFlagAndBuildsCancelWithoutCheck()
    {
        var request = new Iec61850ControlRequest
        {
            ControlValue = Iec61850ControlValue.Close(),
            Test = true
        };
        var context = Iec61850ControlStructureBuilder.CreateContext(
            request,
            Type("ctlVal", "bit-string", size: 2),
            1,
            DateTimeOffset.UtcNow);
        var cancelSpecification = Structure(string.Empty,
            Type("ctlVal", "bit-string", size: 2),
            Structure("origin", Type("orCat", "integer"), Type("orIdent", "octet-string", size: 64)),
            Type("ctlNum", "unsigned"),
            Type("T", "utc-time"),
            Type("Test", "boolean"));

        var cancel = Iec61850ControlStructureBuilder.BuildCancel(context, cancelSpecification, true);

        Assert.Equal(true, cancel.Children[4].Value);
    }

    [Fact]
    public void StructureBuilder_UsesMms1984EpochForSixByteBinaryTime()
    {
        var request = Request(Iec61850ControlValue.Close());
        var timestamp = new DateTimeOffset(1984, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var context = Iec61850ControlStructureBuilder.CreateContext(
            request,
            Type("ctlVal", "bit-string", size: 2),
            1,
            timestamp);
        var specification = Structure(string.Empty,
            Type("ctlVal", "bit-string", size: 2),
            Structure("origin", Type("orCat", "integer"), Type("orIdent", "octet-string", size: 64)),
            Type("ctlNum", "unsigned"),
            Type("T", "binary-time", size: 6),
            Type("Test", "boolean"),
            Type("Check", "bit-string", size: 2));

        var oper = Iec61850ControlStructureBuilder.BuildOperate(context, specification, true);

        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 1 }, oper.Children[3].RawValue);
    }

    [Fact]
    public async Task DirectNormal_OperatesWithOneNativeControlWrite()
    {
        var transport = new FakeControlTransport();
        await using var session = NewSession(transport, Iec61850ControlModel.DirectNormal);

        var result = await session.OperateAsync(Request(Iec61850ControlValue.Close()));

        Assert.True(result.IsSuccess, result.ClientError);
        Assert.Single(transport.Writes);
        Assert.EndsWith("$Oper", transport.Writes[0].Reference.Item, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SboNormal_AutoSelectsThenOperates()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult
        {
            IsSuccess = true,
            Value = MmsDataValue.VisibleString("LD0/CSWI1.Pos")
        });
        await using var session = NewSession(transport, Iec61850ControlModel.SelectBeforeOperateNormal);

        var result = await session.OperateAsync(Request(Iec61850ControlValue.Open()));

        Assert.True(result.IsSuccess, result.ClientError);
        Assert.Contains(transport.Reads, x => x.Item.EndsWith("$SBO", StringComparison.Ordinal));
        Assert.Single(transport.Writes);
    }

    [Fact]
    public async Task SboEnhanced_WritesSboWBeforeOperateAndRequiresTermination()
    {
        var transport = new FakeControlTransport();
        await using var session = NewSession(transport, Iec61850ControlModel.SelectBeforeOperateEnhanced);
        var request = new Iec61850ControlRequest
        {
            ControlValue = Iec61850ControlValue.Close(),
            CommandTerminationTimeout = TimeSpan.FromMilliseconds(20)
        };

        var result = await session.OperateAsync(request);

        Assert.Equal(Iec61850ControlCompletionState.TimedOut, result.CompletionState);
        Assert.True(result.RequestAccepted);
        Assert.Equal(2, transport.Writes.Count);
        Assert.EndsWith("$SBOw", transport.Writes[0].Reference.Item, StringComparison.Ordinal);
        Assert.EndsWith("$Oper", transport.Writes[1].Reference.Item, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SboEnhanced_RejectsAsynchronousLastApplErrorDuringSelectWithValue()
    {
        var transport = new FakeControlTransport();
        transport.OnWrite = (reference, _) =>
        {
            if (!reference.Item.EndsWith("$SBOw", StringComparison.Ordinal))
                return;

            var lastApplError = MmsDataValue.Structure(new[]
            {
                MmsDataValue.VisibleString("LD0/CSWI1.Pos"),
                MmsDataValue.Integer(1),
                MmsDataValue.Structure(new[] { MmsDataValue.Integer(2), MmsDataValue.OctetString("AR"u8) }),
                MmsDataValue.Unsigned(7),
                MmsDataValue.Integer(27)
            });
            transport.EmitReport(reference, lastApplError);
        };
        await using var session = NewSession(transport, Iec61850ControlModel.SelectBeforeOperateEnhanced);

        var result = await session.SelectWithValueAsync(Request(Iec61850ControlValue.Close()));

        Assert.Equal(Iec61850ControlCompletionState.Rejected, result.CompletionState);
        Assert.True(result.RequestAccepted);
        Assert.Equal("locked-by-other-client", result.AddCause);
        Assert.False(session.IsSelected);
    }

    [Fact]
    public async Task DirectEnhanced_CompletesOnlyAfterPositiveCommandTermination()
    {
        var transport = new FakeControlTransport();
        transport.OnWrite = (reference, _) =>
        {
            if (reference.Item.EndsWith("$Oper", StringComparison.Ordinal))
                transport.EmitReport(reference, MmsDataValue.Boolean(true));
        };
        await using var session = NewSession(transport, Iec61850ControlModel.DirectEnhanced);

        var result = await session.OperateAsync(Request(Iec61850ControlValue.Close()));

        Assert.Equal(Iec61850ControlCompletionState.PositiveTermination, result.CompletionState);
        Assert.True(result.RequestAccepted);
        Assert.True(result.CommandTerminationReceived);
        Assert.True(result.PositiveTermination);
    }

    [Fact]
    public async Task DirectEnhanced_DecodesNegativeTerminationAndAddCause()
    {
        var transport = new FakeControlTransport();
        transport.OnWrite = (reference, _) =>
        {
            if (!reference.Item.EndsWith("$Oper", StringComparison.Ordinal))
                return;

            var lastApplError = MmsDataValue.Structure(new[]
            {
                MmsDataValue.VisibleString("LD0/CSWI1.Pos"),
                MmsDataValue.Integer(1),
                MmsDataValue.Structure(new[] { MmsDataValue.Integer(2), MmsDataValue.OctetString("AR"u8) }),
                MmsDataValue.Unsigned(9),
                MmsDataValue.Integer(11)
            });
            transport.EmitReport(reference, lastApplError);
        };
        await using var session = NewSession(transport, Iec61850ControlModel.DirectEnhanced);

        var result = await session.OperateAsync(Request(Iec61850ControlValue.Close()));

        Assert.Equal(Iec61850ControlCompletionState.NegativeTermination, result.CompletionState);
        Assert.True(result.CommandTerminationReceived);
        Assert.False(result.PositiveTermination);
        Assert.Equal("blocked-by-synchrocheck", result.AddCause);
    }

    [Fact]
    public async Task EnhancedControl_ReportsAssociationLossWhileWaitingForTermination()
    {
        var transport = new FakeControlTransport();
        transport.OnWrite = (reference, _) =>
        {
            if (reference.Item.EndsWith("$Oper", StringComparison.Ordinal))
                transport.LoseAssociation();
        };
        await using var session = NewSession(transport, Iec61850ControlModel.DirectEnhanced);

        var result = await session.OperateAsync(Request(Iec61850ControlValue.Close()));

        Assert.Equal(Iec61850ControlCompletionState.AssociationLost, result.CompletionState);
        Assert.True(result.RequestAccepted);
    }

    [Fact]
    public async Task ExplicitSboSelection_RejectsMutationBeforeOperate()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult { IsSuccess = true, Value = MmsDataValue.VisibleString("selected") });
        await using var session = NewSession(transport, Iec61850ControlModel.SelectBeforeOperateNormal);
        var selected = Request(Iec61850ControlValue.Open());
        Assert.True((await session.SelectAsync(selected)).IsSuccess);

        var result = await session.OperateAsync(Request(Iec61850ControlValue.Close()));

        Assert.Equal(Iec61850ControlCompletionState.Rejected, result.CompletionState);
        Assert.Contains("differs", result.ClientError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(transport.Writes, write => write.Reference.Item.EndsWith("$Cancel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SboSelection_AcceptsFcQualifiedRelativeObjectReference()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult
        {
            IsSuccess = true,
            Value = MmsDataValue.VisibleString("CSWI1$CO$Pos")
        });
        await using var session = NewSession(transport, Iec61850ControlModel.SelectBeforeOperateNormal);

        var result = await session.SelectAsync(Request(Iec61850ControlValue.Open()));

        Assert.True(result.IsSuccess, result.ClientError);
        await session.CancelAsync();
    }

    [Fact]
    public async Task SboSelection_RejectsReferenceForDifferentControlObject()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult
        {
            IsSuccess = true,
            Value = MmsDataValue.VisibleString("LD0/CSWI1.Other")
        });
        await using var session = NewSession(transport, Iec61850ControlModel.SelectBeforeOperateNormal);

        var result = await session.SelectAsync(Request(Iec61850ControlValue.Open()));

        Assert.Equal(Iec61850ControlCompletionState.Rejected, result.CompletionState);
        Assert.False(session.IsSelected);
    }

    [Fact]
    public async Task ExpiredSboSelection_IsCancelledBeforeOperate()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult { IsSuccess = true, Value = MmsDataValue.VisibleString("selected") });
        await using var session = NewSession(
            transport,
            Iec61850ControlModel.SelectBeforeOperateNormal,
            sboTimeout: TimeSpan.FromMilliseconds(5));
        Assert.True((await session.SelectAsync(Request(Iec61850ControlValue.Open()))).IsSuccess);
        await Task.Delay(20);

        var result = await session.OperateAsync(Request(Iec61850ControlValue.Open()));

        Assert.Equal(Iec61850ControlCompletionState.TimedOut, result.CompletionState);
        Assert.Contains(transport.Writes, write => write.Reference.Item.EndsWith("$Cancel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExpiredSelectionLease_AutomaticallyCancelsAndReleasesLocalOwnership()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult { IsSuccess = true, Value = MmsDataValue.VisibleString("selected") });
        transport.ReadResults.Enqueue(new MmsReadResult { IsSuccess = true, Value = MmsDataValue.VisibleString("selected") });
        await using var first = NewSession(
            transport,
            Iec61850ControlModel.SelectBeforeOperateNormal,
            sboTimeout: TimeSpan.FromMilliseconds(10));
        await using var second = NewSession(
            transport,
            Iec61850ControlModel.SelectBeforeOperateNormal,
            sboTimeout: TimeSpan.FromMilliseconds(100));

        Assert.True((await first.SelectAsync(Request(Iec61850ControlValue.Open()))).IsSuccess);
        await Task.Delay(50);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var secondSelection = await second.SelectAsync(Request(Iec61850ControlValue.Close()), cts.Token);

        Assert.True(secondSelection.IsSuccess, secondSelection.ClientError);
        Assert.Contains(transport.Writes, write => write.Reference.Item.EndsWith("$Cancel", StringComparison.Ordinal));
        await second.CancelAsync();
    }

    [Fact]
    public async Task Cancel_ReleasesSboOwnership()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult { IsSuccess = true, Value = MmsDataValue.VisibleString("selected") });
        await using var session = NewSession(transport, Iec61850ControlModel.SelectBeforeOperateNormal);
        Assert.True((await session.SelectAsync(Request(Iec61850ControlValue.Open()))).IsSuccess);

        var cancel = await session.CancelAsync();

        Assert.True(cancel.IsSuccess, cancel.ClientError);
        Assert.False(session.IsSelected);
        Assert.EndsWith("$Cancel", transport.Writes.Single().Reference.Item, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentSelections_OnSameAssociationAndObject_AreSerialized()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult { IsSuccess = true, Value = MmsDataValue.VisibleString("selected") });
        await using var first = NewSession(transport, Iec61850ControlModel.SelectBeforeOperateNormal);
        await using var second = NewSession(transport, Iec61850ControlModel.SelectBeforeOperateNormal);
        Assert.True((await first.SelectAsync(Request(Iec61850ControlValue.Open()))).IsSuccess);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.SelectAsync(Request(Iec61850ControlValue.Close()), cts.Token));

        await first.CancelAsync();
    }

    [Fact]
    public void LastApplErrorDecoder_DecodesInterlockRejection()
    {
        var lastApplError = MmsDataValue.Structure(new[]
        {
            MmsDataValue.VisibleString("LD0/CSWI1.Pos"),
            MmsDataValue.Integer(1),
            MmsDataValue.Structure(new[] { MmsDataValue.Integer(2), MmsDataValue.OctetString("AR"u8) }),
            MmsDataValue.Unsigned(8),
            MmsDataValue.Integer(10)
        });

        var decoded = Iec61850CommandTerminationDecoder.TryDecodeLastApplError(lastApplError, out var error, out var addCause, out var text);

        Assert.True(decoded);
        Assert.Equal(1, error);
        Assert.Equal(10, addCause);
        Assert.Contains("blocked-by-interlocking", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LastApplErrorDecoder_MatchesEmbeddedControlObjectWhenReportNameIsGeneric()
    {
        var lastApplError = MmsDataValue.Structure(new[]
        {
            MmsDataValue.VisibleString("LD0/CSWI1.Pos"),
            MmsDataValue.Integer(1),
            MmsDataValue.Structure(new[] { MmsDataValue.Integer(2), MmsDataValue.OctetString("AR"u8) }),
            MmsDataValue.Unsigned(8),
            MmsDataValue.Integer(10)
        });
        var envelope = BuildInformationReport(
            new MmsObjectReference("LD0", "LLN0$CO$LastApplError", "CO"),
            lastApplError);
        var references = Iec61850ControlObjectReferences.Parse("LD0/CSWI1.Pos");

        var termination = Iec61850CommandTerminationDecoder.Decode(envelope, references);

        Assert.True(termination.IsForControlObject);
        Assert.False(termination.Positive);
        Assert.Equal("blocked-by-interlocking", termination.AddCause);
    }

    [Fact]
    public async Task GenericMmsWrite_BlocksControlServiceMembers()
    {
        await using var client = new MmsClientSession();
        var result = await client.WriteSingleVariableAsync(
            new MmsObjectReference("LD0", "CSWI1$CO$Pos$Oper", "CO"),
            MmsDataValue.Structure(Array.Empty<MmsDataValue>()));

        Assert.False(result.IsSuccess);
        Assert.Contains("IIec61850ControlService", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DescriptorDiscovery_UsesLiveCtlModelAndExactCtlValType()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult { IsSuccess = true, Value = MmsDataValue.Integer(3) });
        transport.Specifications["CSWI1$CO$Pos$Oper"] = CommandSpecification();
        transport.Specifications["CSWI1$CO$Pos$Cancel"] = CommandSpecification();
        transport.DomainVariables["LD0"] = new[] { "CSWI1$ST$Pos$stVal" };
        var service = new Iec61850ControlService();

        await using var session = await service.OpenCoreAsync(transport, "LD0/CSWI1.Pos");

        Assert.Equal(Iec61850ControlModel.DirectEnhanced, session.Descriptor.ControlModel);
        Assert.Equal("DPC", session.Descriptor.Cdc);
        Assert.Equal("bit-string", session.Descriptor.CtlValSpecification.MmsType);
        Assert.True(session.Descriptor.SupportsCommandTermination);
        Assert.Equal("LD0/CSWI1.Pos.stVal", session.Descriptor.StatusReference);
        Assert.Equal("ST", session.Descriptor.StatusFunctionalConstraint);
    }

    [Fact]
    public async Task DescriptorDiscovery_RejectsSboObjectWithoutCancelService()
    {
        var transport = new FakeControlTransport();
        transport.ReadResults.Enqueue(new MmsReadResult { IsSuccess = true, Value = MmsDataValue.Integer(2) });
        transport.Specifications["CSWI1$CO$Pos$Oper"] = CommandSpecification();
        transport.DomainVariables["LD0"] = new[] { "CSWI1$ST$Pos$stVal" };
        var service = new Iec61850ControlService();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.OpenCoreAsync(transport, "LD0/CSWI1.Pos"));

        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Iec61850ControlObjectSession NewSession(
        FakeControlTransport transport,
        Iec61850ControlModel model,
        TimeSpan? sboTimeout = null,
        string statusReference = "LD0/CSWI1.Pos.stVal",
        string statusFunctionalConstraint = "ST")
    {
        var references = Iec61850ControlObjectReferences.Parse("LD0/CSWI1.Pos");
        return new Iec61850ControlObjectSession(
            transport,
            new Iec61850ControlObjectDescriptor
            {
                ObjectReference = references.ObjectReference,
                Cdc = "DPC",
                ControlModel = model,
                CtlValSpecification = Type("ctlVal", "bit-string", size: 2),
                OperSpecification = CommandSpecification(),
                StatusReference = statusReference,
                StatusFunctionalConstraint = statusFunctionalConstraint,
                SelectWithValueSpecification = model == Iec61850ControlModel.SelectBeforeOperateEnhanced ? CommandSpecification() : null,
                CancelSpecification = CommandSpecification(),
                SboTimeout = sboTimeout ?? TimeSpan.FromSeconds(2),
                OperTimeout = TimeSpan.FromMilliseconds(30),
                SupportsTimeActivatedOperate = true,
                SupportsCommandTermination = model is Iec61850ControlModel.DirectEnhanced or Iec61850ControlModel.SelectBeforeOperateEnhanced,
                References = references
            },
            new Iec61850ControlServiceOptions
            {
                DefaultOperateTimeout = TimeSpan.FromMilliseconds(30),
                ApplicationErrorGracePeriod = TimeSpan.FromMilliseconds(5)
            });
    }

    private static Iec61850ControlRequest Request(Iec61850ControlValue value, byte? ctlNum = null)
        => new()
        {
            ControlValue = value,
            ControlNumber = ctlNum,
            Origin = Iec61850Origin.FromText("ARTEST"),
            InterlockCheck = true
        };

    private static MmsTypeSpecificationNode CommandSpecification()
        => Structure(string.Empty,
            Type("ctlVal", "bit-string", size: 2),
            Type("operTm", "utc-time"),
            Structure("origin", Type("orCat", "integer"), Type("orIdent", "octet-string", size: 64)),
            Type("ctlNum", "unsigned"),
            Type("T", "utc-time"),
            Type("Test", "boolean"),
            Type("Check", "bit-string", size: 2));

    private static MmsTypeSpecificationNode Type(string name, string type, int? size = null)
        => new() { Name = name, MmsType = type, Size = size };

    private static MmsTypeSpecificationNode Structure(string name, params MmsTypeSpecificationNode[] children)
        => new() { Name = name, MmsType = "structure", Children = children };

    private sealed class FakeControlTransport : IIec61850ControlTransport
    {
        private readonly Channel<MmsPduEnvelope> _reports = Channel.CreateUnbounded<MmsPduEnvelope>();

        public object AssociationIdentity { get; } = new();
        public bool IsAssociated { get; set; } = true;
        public string LastRequestHex { get; private set; } = "AABB";
        public string LastResponseHex { get; private set; } = "CCDD";
        public ConcurrentQueue<MmsReadResult> ReadResults { get; } = new();
        public Dictionary<string, MmsTypeSpecificationNode> Specifications { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<string>> DomainVariables { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<MmsObjectReference> Reads { get; } = new();
        public List<(MmsObjectReference Reference, MmsDataValue Value)> Writes { get; } = new();
        public Action<MmsObjectReference, MmsDataValue>? OnWrite { get; set; }

        public Task<MmsReadResult> ReadAsync(MmsObjectReference reference, CancellationToken cancellationToken)
        {
            Reads.Add(reference);
            if (ReadResults.TryDequeue(out var result))
                return Task.FromResult(result);
            return Task.FromResult(new MmsReadResult { IsSuccess = false, Message = "not configured" });
        }

        public Task<MmsWriteResult> WriteControlAsync(MmsObjectReference reference, MmsDataValue value, CancellationToken cancellationToken)
        {
            Writes.Add((reference, value));
            OnWrite?.Invoke(reference, value);
            return Task.FromResult(new MmsWriteResult
            {
                IsSuccess = true,
                AccessResults = new[] { new MmsWriteAccessResult { IsSuccess = true, Message = "success" } },
                Message = "success",
                ResponseHexPreview = LastResponseHex
            });
        }

        public Task<MmsVariableAccessAttributesResult> GetVariableSpecificationAsync(MmsObjectReference reference, CancellationToken cancellationToken)
        {
            var success = Specifications.TryGetValue(reference.Item, out var specification);
            return Task.FromResult(new MmsVariableAccessAttributesResult
            {
                IsSuccess = success,
                Reference = reference,
                TypeSpecification = specification,
                Message = success ? "ok" : "not configured"
            });
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverDomainVariablesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(DomainVariables);

        public IAsyncDisposable SubscribeInformationReports(out ChannelReader<MmsPduEnvelope> reader, int capacity = 32)
        {
            reader = _reports.Reader;
            return new NoopSubscription();
        }

        public void EmitReport(MmsObjectReference reference, MmsDataValue value)
            => _reports.Writer.TryWrite(BuildInformationReport(reference, value));

        public void LoseAssociation()
        {
            IsAssociated = false;
            _reports.Writer.TryComplete(new IOException("association lost"));
        }

        private sealed class NoopSubscription : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static MmsPduEnvelope BuildInformationReport(MmsObjectReference reference, MmsDataValue value)
    {
        var variableAccessSpecification = MmsWriteRequest.BuildListOfVariable(reference);
        var listOfAccessResult = BerWriter.EncodeTlv(0xA0, MmsDataCodec.Encode(value));
        var informationReport = BerWriter.EncodeTlv(
            0xA0,
            MmsPresentation.Concat(variableAccessSpecification, listOfAccessResult));
        var unconfirmedPdu = BerWriter.EncodeTlv(0xA3, informationReport);
        return MmsPduEnvelope.Decode(MmsPresentation.WrapIsoPresentationPData(unconfirmedPdu));
    }
}
