using AR.Iec61850.Asn1;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Mms;

public sealed class InitialFcReadTests
{
    [Fact]
    public void BatchCodec_Single_Variable_Reproduces_Existing_Read_Pdu()
    {
        var reference = new MmsObjectReference("IED01LD0", "LLN0$ST", "ST");

        var existing = MmsReadRequest.BuildSingleVariableRead(
            7,
            reference,
            MmsReadPayloadProfile.RawMmsPdu);
        var batch = MmsReadBatchCodec.BuildRequest(
            7,
            new[] { reference },
            MmsReadPayloadProfile.RawMmsPdu);

        Assert.Equal(existing, batch);
    }

    [Fact]
    public void BatchCodec_Rejects_More_Than_Ten_Variables()
    {
        var references = Enumerable.Range(0, 11)
            .Select(index => new MmsObjectReference("IED01LD0", $"LN{index}$ST", "ST"))
            .ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MmsReadBatchCodec.BuildRequest(1, references));
    }

    [Fact]
    public void BatchDecoder_Preserves_AccessResult_Order_And_Individual_Failure()
    {
        var references = new[]
        {
            new MmsObjectReference("IED01LD0", "LLN0$ST", "ST"),
            new MmsObjectReference("IED01LD0", "MMXU1$MX", "MX")
        };
        var accessResults = Concat(
            MmsDataCodec.Encode(MmsDataValue.Integer(42)),
            BerWriter.EncodeTlv(0x80, new byte[] { 0x04 }));
        var readService = BerWriter.EncodeTlv(0xA4, BerWriter.EncodeTlv(0xA1, accessResults));
        var confirmed = BerWriter.EncodeTlv(
            0xA1,
            Concat(BerWriter.EncodeTlv(0x02, new byte[] { 0x05 }), readService));
        var response = MmsPresentation.WrapIsoPresentationPData(confirmed);

        var decoded = MmsReadBatchCodec.DecodeResponse(response, references, expectedInvokeId: 5);

        Assert.False(decoded.IsSuccess);
        Assert.True(decoded.HasAnySuccess);
        Assert.Equal(2, decoded.Results.Count);
        Assert.True(decoded.Results[0].IsSuccess);
        Assert.Equal(42L, decoded.Results[0].Value?.Value);
        Assert.False(decoded.Results[1].IsSuccess);
        Assert.Equal(4, decoded.Results[1].FailureCode);
    }

    [Fact]
    public void Planner_Batches_At_Most_Ten_And_Uses_One_Outstanding_Read()
    {
        var targets = Enumerable.Range(0, 23)
            .Select(index => new InitialFcReadTarget
            {
                Domain = "IED01LD0",
                LogicalNode = $"LN{index:00}",
                FunctionalConstraint = "ST",
                MmsItemName = $"LN{index:00}$ST",
                Source = "Synthetic"
            })
            .ToArray();

        var plan = InitialFcReadPlanner.Build(targets);

        Assert.True(plan.IsValid, string.Join(" | ", plan.Errors));
        Assert.Equal(1, plan.MaximumOutstandingReads);
        Assert.Equal(10, plan.MaximumVariableReferencesPerRead);
        Assert.Equal(new[] { 10, 10, 3 }, plan.Batches.Select(batch => batch.Targets.Count).ToArray());
    }

    [Fact]
    public void Live_Directory_And_Scl_Model_Use_The_Same_Fc_Root_Form()
    {
        var directory = new MmsIedModelDirectory(new[]
        {
            new MmsFcResolvedPoint
            {
                Domain = "IED01LD0",
                LogicalNode = "LLN0",
                FunctionalConstraint = "ST",
                DataObjectPath = "Beh.stVal",
                MmsItemName = "LLN0$ST$Beh$stVal"
            },
            new MmsFcResolvedPoint
            {
                Domain = "IED01LD0",
                LogicalNode = "LLN0",
                FunctionalConstraint = "ST",
                DataObjectPath = "Beh.q",
                MmsItemName = "LLN0$ST$Beh$q"
            }
        });
        var design = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IED01LD0",
                    Inst = "LD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = "LLN0",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Name = "Beh",
                                    Reference = "IED01LD0/LLN0.Beh",
                                    Attributes = new[]
                                    {
                                        Attribute("IED01LD0/LLN0.Beh.stVal", "stVal", "ST"),
                                        Attribute("IED01LD0/LLN0.Beh.q", "q", "ST")
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var livePlan = InitialFcReadPlanner.FromLiveDirectory(directory);
        var sclPlan = InitialFcReadPlanner.FromSclModel(design);

        Assert.Equal("IED01LD0/LLN0$ST", Assert.Single(livePlan.Targets).MmsReference);
        Assert.Equal("IED01LD0/LLN0$ST", Assert.Single(sclPlan.Targets).MmsReference);
    }

    [Fact]
    public void Scl_Step4_Design_Uses_Explicit_LdName_And_Preserves_Ordered_Leaf_Shape()
    {
        const string xml = """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
              <IED name="IED01">
                <AccessPoint name="AP1">
                  <Server>
                    <LDevice inst="LD0" ldName="CUSTOM_LD">
                      <LN0 lnClass="LLN0" inst="" lnType="LNT0" />
                    </LDevice>
                  </Server>
                </AccessPoint>
              </IED>
              <DataTypeTemplates>
                <LNodeType id="LNT0" lnClass="LLN0">
                  <DO name="Beh" type="DOT_Beh" />
                </LNodeType>
                <DOType id="DOT_Beh" cdc="INS">
                  <DA name="stVal" bType="INT32" fc="ST" />
                  <DA name="q" bType="Quality" fc="ST" />
                  <DA name="t" bType="Timestamp" fc="ST" />
                </DOType>
              </DataTypeTemplates>
            </SCL>
            """;

        var design = SclInitialFcReadDesignBuilder.Read(xml, "IED01", "AP1");
        var plan = InitialFcReadPlanner.FromSclModel(
            design.Model,
            design.DomainInventory.ExpectedDomains);

        Assert.True(design.IsSuccess, string.Join(" | ", design.Errors));
        var target = Assert.Single(plan.Targets);
        Assert.Equal("CUSTOM_LD/LLN0$ST", target.MmsReference);
        var dataObject = Assert.Single(target.DataObjects);
        Assert.Equal("Beh", dataObject.Name);
        Assert.Equal(new[] { "stVal", "q", "t" }, dataObject.Leaves.Select(leaf => leaf.AttributePath).ToArray());
        Assert.All(dataObject.Leaves, leaf => Assert.StartsWith("CUSTOM_LD/", leaf.Reference, StringComparison.Ordinal));
    }

    [Fact]
    public void Projector_Maps_Exact_Structure_And_Rejects_Arrays()
    {
        var target = new InitialFcReadTarget
        {
            Domain = "CUSTOM_LD",
            LogicalNode = "LLN0",
            FunctionalConstraint = "ST",
            MmsItemName = "LLN0$ST",
            DataObjects = new[]
            {
                new InitialFcReadDataObjectBinding
                {
                    Name = "Beh",
                    Reference = "CUSTOM_LD/LLN0.Beh",
                    Leaves = new[]
                    {
                        Leaf("CUSTOM_LD/LLN0.Beh.stVal", "stVal"),
                        Leaf("CUSTOM_LD/LLN0.Beh.q", "q"),
                        Leaf("CUSTOM_LD/LLN0.Beh.t", "t")
                    }
                }
            }
        };
        var exactValue = MmsDataValue.Structure(new[]
        {
            MmsDataValue.Structure(new[]
            {
                MmsDataValue.Integer(1),
                MmsDataValue.BitString(0, new byte[] { 0x00, 0x00 }),
                MmsDataValue.VisibleString("time")
            })
        });

        var exact = InitialFcValueProjector.Project(target, exactValue);
        var ambiguousArray = InitialFcValueProjector.Project(
            target,
            MmsDataValue.Structure(new[]
            {
                MmsDataValue.Array(new[] { MmsDataValue.Integer(1) })
            }));

        Assert.True(exact.IsExact, string.Join(" | ", exact.Errors));
        Assert.Equal(3, exact.Leaves.Count);
        Assert.Equal(1L, exact.Leaves[0].Value.Value);
        Assert.False(ambiguousArray.IsExact);
        Assert.Contains(ambiguousArray.Errors, error => error.Contains("Array", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Executor_Rejects_Invalid_Plan_Without_A_Network_Side_Effect()
    {
        await using var session = new MmsClientSession();

        var result = await session.ExecuteInitialFcReadPlanAsync(new InitialFcReadPlan());

        Assert.Equal(InitialFcReadExecutionStatus.InvalidPlan, result.Status);
        Assert.False(session.IsTcpConnected);
    }

    private static LiveIedDataAttributeModel Attribute(string reference, string path, string fc)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = fc,
            SclBType = "Synthetic"
        };

    private static InitialFcReadLeafBinding Leaf(string reference, string path)
        => new()
        {
            Reference = reference,
            AttributePath = path,
            FunctionalConstraint = "ST",
            SclBType = "Synthetic"
        };

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
