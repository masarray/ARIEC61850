using AR.Iec61850.Control;
using AR.Iec61850.Mms;
using System.Threading.Channels;

namespace AR.Iec61850.Tests.Control;

public sealed class AuthoritativeControlInventoryTests
{
    [Fact]
    public async Task OpenCore_WithAuthoritativeInventory_DoesNotBrowseDomainVariablesAgain()
    {
        var transport = new CountingTransport();
        var service = new Iec61850ControlService();
        var authority = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["LD0"] = new[] { "CSWI1$ST$Pos$stVal" }
        };

        await using var session = await service.OpenCoreAsync(
            transport,
            "LD0/CSWI1.Pos",
            authority,
            CancellationToken.None);

        Assert.Equal(0, transport.DomainDiscoveryCalls);
        Assert.Equal("LD0/CSWI1.Pos.stVal", session.Descriptor.StatusReference);
        Assert.Equal("ST", session.Descriptor.StatusFunctionalConstraint);
        Assert.Contains("domainInventory=authoritative-reuse", session.Descriptor.DiscoveryEvidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenCore_WithoutAuthoritativeInventory_PreservesLegacyFallback()
    {
        var transport = new CountingTransport();
        var service = new Iec61850ControlService();

        await using var session = await service.OpenCoreAsync(
            transport,
            "LD0/CSWI1.Pos",
            CancellationToken.None);

        Assert.Equal(1, transport.DomainDiscoveryCalls);
        Assert.Equal("LD0/CSWI1.Pos.stVal", session.Descriptor.StatusReference);
        Assert.Contains("domainInventory=live-fallback", session.Descriptor.DiscoveryEvidence, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CountingTransport : IIec61850ControlTransport
    {
        private readonly Channel<MmsPduEnvelope> _reports = Channel.CreateUnbounded<MmsPduEnvelope>();

        public object AssociationIdentity { get; } = new();
        public bool IsAssociated => true;
        public string LastRequestHex => string.Empty;
        public string LastResponseHex => string.Empty;
        public int DomainDiscoveryCalls { get; private set; }

        public Task<MmsReadResult> ReadAsync(MmsObjectReference reference, CancellationToken cancellationToken)
        {
            if (reference.Item.EndsWith("$ctlModel", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new MmsReadResult
                {
                    IsSuccess = true,
                    Value = MmsDataValue.Integer(3)
                });
            }

            return Task.FromResult(new MmsReadResult
            {
                IsSuccess = false,
                Message = "optional value not configured"
            });
        }

        public Task<MmsVariableAccessAttributesResult> GetVariableSpecificationAsync(
            MmsObjectReference reference,
            CancellationToken cancellationToken)
        {
            var configured = reference.Item.EndsWith("$Oper", StringComparison.OrdinalIgnoreCase) ||
                             reference.Item.EndsWith("$Cancel", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new MmsVariableAccessAttributesResult
            {
                IsSuccess = configured,
                Reference = reference,
                TypeSpecification = configured ? CommandSpecification() : null,
                Message = configured ? "ok" : "not configured"
            });
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverDomainVariablesAsync(
            CancellationToken cancellationToken)
        {
            DomainDiscoveryCalls++;
            IReadOnlyDictionary<string, IReadOnlyList<string>> names =
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LD0"] = new[] { "CSWI1$ST$Pos$stVal" }
                };
            return Task.FromResult(names);
        }

        public Task<MmsWriteResult> WriteControlAsync(
            MmsObjectReference reference,
            MmsDataValue value,
            CancellationToken cancellationToken)
            => Task.FromResult(new MmsWriteResult { IsSuccess = true });

        public IAsyncDisposable SubscribeInformationReports(out ChannelReader<MmsPduEnvelope> reader, int capacity = 32)
        {
            reader = _reports.Reader;
            return new NoopSubscription();
        }

        private static MmsTypeSpecificationNode CommandSpecification()
            => new()
            {
                MmsType = "structure",
                Children = new MmsTypeSpecificationNode[]
                {
                    new() { Name = "ctlVal", MmsType = "bit-string", Size = 2 },
                    new() { Name = "operTm", MmsType = "utc-time" },
                    new()
                    {
                        Name = "origin",
                        MmsType = "structure",
                        Children = new MmsTypeSpecificationNode[]
                        {
                            new() { Name = "orCat", MmsType = "integer" },
                            new() { Name = "orIdent", MmsType = "octet-string", Size = 64 }
                        }
                    },
                    new() { Name = "ctlNum", MmsType = "unsigned" },
                    new() { Name = "T", MmsType = "utc-time" },
                    new() { Name = "Test", MmsType = "boolean" },
                    new() { Name = "Check", MmsType = "bit-string", Size = 2 }
                }
            };

        private sealed class NoopSubscription : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
