using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Tests.Engineering;

public sealed class CanonicalLiveModelAdapterTests
{
    [Fact]
    public void FromLiveDiscovery_Preserves_Identity_Provenance_And_Compacts_Repeated_Strings()
    {
        var source = new LiveIedModelDiscoveryDocument
        {
            Source = "LiveMmsDiscovery",
            Host = "192.0.2.10",
            Port = 102,
            IedName = "IED_A",
            AccessPointName = "P1",
            IedIdentity = new LiveIedIdentity
            {
                IedName = "IED_A",
                Source = "MmsDomainCommonPrefix",
                Confidence = LiveIedDiscoveryConfidenceLevel.High,
                CandidateNames = ["IED_A"],
                Evidence = ["domain consensus"]
            },
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IED_ALD0",
                    Inst = "LD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "LLN0",
                            LnClass = "LLN0",
                            ProposedLnTypeId = "LT_LLN0",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IED_ALD0/LLN0.Mod",
                                    Name = "Mod",
                                    ProposedDoTypeId = "DOT_Mod",
                                    InferredCdc = "ENC",
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
                                    Attributes =
                                    [
                                        Attribute("IED_ALD0/LLN0.Mod.stVal", "stVal"),
                                        Attribute("IED_ALD0/LLN0.Mod.q", "q")
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var model = CanonicalLiveModelAdapter.FromLiveDiscovery(source);

        Assert.Equal(CanonicalIngressKind.LiveMmsDiscovery, model.Source.Ingress);
        Assert.Equal("IED_A", model.Identity.Name);
        Assert.Equal(CanonicalEvidenceSource.LiveMms, model.Identity.Provenance.Source);
        Assert.Equal(CanonicalConfidence.High, model.Identity.Provenance.Confidence);
        Assert.Equal(2, model.SignalCount);
        Assert.Equal(model.Signals[0].FunctionalConstraint, model.Signals[1].FunctionalConstraint);
        Assert.Equal("ST", model.Strings.Resolve(model.Signals[0].FunctionalConstraint));
        Assert.Equal(CanonicalEvidenceSource.LiveMms, model.Signals[0].Provenance.Source);
        Assert.Equal("192.0.2.10", model.Communication.Host.Value);
    }

    private static LiveIedDataAttributeModel Attribute(string reference, string path)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = "ST",
            SclBType = "BOOLEAN",
            MmsType = "boolean",
            MmsTypeSignature = "boolean",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
}
