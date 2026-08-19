using System.Threading.Channels;
using AR.Iec61850.Asn1;
using AR.Iec61850.Control;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Control;

public sealed class G1OrderedWireEvidenceTests
{
    [Fact]
    public async Task SboEnhanced_PositiveTermination_PreservesSboWThenOperateWireSteps()
    {
        var transport = new WireEvidenceTransport();
        transport.OnWrite = (reference, _) =>
        {
            if (reference.Item.EndsWith("$Oper", StringComparison.Ordinal))
                transport.EmitReport(reference, MmsDataValue.Boolean(true));
        };

        var references = Iec61850ControlObjectReferences.Parse("LD0/CSWI1.Pos");
        await using var session = new Iec61850ControlObjectSession(
            transport,
            new Iec61850ControlObjectDescriptor
            {
                ObjectReference = references.ObjectReference,
                Cdc = "DPC",
                ControlModel = Iec61850ControlModel.SelectBeforeOperateEnhanced,
                CtlValSpecification = Type("ctlVal", "bit-string", 2),
                OperSpecification = CommandSpecification(),
                SelectWithValueSpecification = CommandSpecification(),
                CancelSpecification = CommandSpecification(),
                StatusReference = "LD0/CSWI1.Pos.stVal",
                StatusFunctionalConstraint = "ST",
                SboTimeout = TimeSpan.FromSeconds(2),
                OperTimeout = TimeSpan.FromMilliseconds(100),
                SupportsTimeActivatedOperate = true,
                SupportsCommandTermination = true,
                References = references
            },
            new Iec61850ControlServiceOptions
            {
                DefaultOperateTimeout = TimeSpan.FromMilliseconds(100),
                ApplicationErrorGracePeriod = TimeSpan.FromMilliseconds(5)
            });

        var result = await session.OperateAsync(new Iec61850ControlRequest
        {
            ControlValue = Iec61850ControlValue.Close(),
            Origin = Iec61850Origin.FromText("ARG1"),
            InterlockCheck = true,
            AutoSelect = true,
            CommandTerminationTimeout = TimeSpan.FromMilliseconds(100)
        });

        Assert.Equal(Iec61850ControlCompletionState.PositiveTermination, result.CompletionState);
        Assert.True(result.RequestAccepted);
        Assert.True(result.CommandTerminationReceived);
        Assert.True(result.PositiveTermination);

        Assert.Equal(2, result.WireSteps.Count);
        Assert.Equal(Iec61850ControlAction.SelectWithValue, result.WireSteps[0].Action);
        Assert.EndsWith("$SBOw", result.WireSteps[0].Reference, StringComparison.Ordinal);
        Assert.True(result.WireSteps[0].RequestAccepted);
        Assert.False(string.IsNullOrWhiteSpace(result.WireSteps[0].RequestHex));
        Assert.False(string.IsNullOrWhiteSpace(result.WireSteps[0].ResponseHex));

        Assert.Equal(Iec61850ControlAction.Operate, result.WireSteps[1].Action);
        Assert.EndsWith("$Oper", result.WireSteps[1].Reference, StringComparison.Ordinal);
        Assert.True(result.WireSteps[1].RequestAccepted);
        Assert.Contains("positive CommandTermination", result.WireSteps[1].Detail, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(2, transport.Writes.Count);
        Assert.EndsWith("$SBOw", transport.Writes[0].Reference.Item, StringComparison.Ordinal);
        Assert.EndsWith("$Oper", transport.Writes[1].Reference.Item, StringComparison.Ordinal);
    }

    private static MmsTypeSpecificationNode CommandSpecification()
        => Structure(string.Empty,
            Type("ctlVal", "bit-string", 2),
            Type("operTm", "utc-time"),
            Structure("origin", Type("orCat", "integer"), Type("orIdent", "octet-string", 64)),
            Type("ctlNum", "unsigned"),
            Type("T", "utc-time"),
            Type("Test", "boolean"),
            Type("Check", "bit-string", 2));

    private static MmsTypeSpecificationNode Type(string name, string type, int? size = null)
        => new() { Name = name, MmsType = type, Size = size };

    private static MmsTypeSpecificationNode Structure(string name, params MmsTypeSpecificationNode[] children)
        => new() { Name = name, MmsType = "structure", Children = children };

    private sealed class WireEvidenceTransport : IIec61850ControlTransport
    {
        private readonly Channel<MmsPduEnvelope> _reports = Channel.CreateUnbounded<MmsPduEnvelope>();
        private int _writeSequence;

        public object AssociationIdentity { get; } = new();
        public bool IsAssociated { get; private set; } = true;
        public string LastRequestHex { get; private set; } = string.Empty;
        public string LastResponseHex { get; private set; } = string.Empty;
        public List<(MmsObjectReference Reference, MmsDataValue Value)> Writes { get; } = new();
        public Action<MmsObjectReference, MmsDataValue>? OnWrite { get; set; }

        public Task<MmsReadResult> ReadAsync(MmsObjectReference reference, CancellationToken cancellationToken)
            => Task.FromResult(new MmsReadResult { IsSuccess = false, Message = "not used" });

        public Task<MmsWriteResult> WriteControlAsync(
            MmsObjectReference reference,
            MmsDataValue value,
            CancellationToken cancellationToken)
        {
            Writes.Add((reference, value));
            var sequence = Interlocked.Increment(ref _writeSequence);
            LastRequestHex = sequence == 1 ? "A1B1" : "A2B2";
            LastResponseHex = sequence == 1 ? "C1D1" : "C2D2";
            OnWrite?.Invoke(reference, value);
            return Task.FromResult(new MmsWriteResult
            {
                IsSuccess = true,
                AccessResults = [new MmsWriteAccessResult { IsSuccess = true, Message = "success" }],
                Message = "success",
                ResponseHexPreview = LastResponseHex
            });
        }

        public Task<MmsVariableAccessAttributesResult> GetVariableSpecificationAsync(
            MmsObjectReference reference,
            CancellationToken cancellationToken)
            => Task.FromResult(new MmsVariableAccessAttributesResult
            {
                IsSuccess = false,
                Reference = reference,
                Message = "not used"
            });

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverDomainVariablesAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                new Dictionary<string, IReadOnlyList<string>>());

        public IAsyncDisposable SubscribeInformationReports(
            out ChannelReader<MmsPduEnvelope> reader,
            int capacity = 32)
        {
            reader = _reports.Reader;
            return new NoopSubscription();
        }

        public void EmitReport(MmsObjectReference reference, MmsDataValue value)
            => _reports.Writer.TryWrite(BuildInformationReport(reference, value));

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
