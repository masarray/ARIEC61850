using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Discovery;

public sealed class LiveIedModelDiscoveryBuilderTests
{
    [Theory]
    [InlineData("LLN0", "NamPlt", "vendor;swRev;configRev", "DC", "LPL")]
    [InlineData("LPHD", "PhyNam", "vendor;hwRev;swRev;serNum", "DC", "DPL")]
    [InlineData("PTOC", "Beh", "stVal;q;t", "ST", "INS")]
    [InlineData("PTOC", "Health", "stVal;q;t", "ST", "INS")]
    [InlineData("PTOC", "Mod", "stVal;q;t;ctlModel", "ST;CF", "INC")]
    [InlineData("PTOC", "Op", "general;q;t", "ST", "ACT")]
    [InlineData("PTOC", "Str", "general;dirGeneral;q;t", "ST", "ACD")]
    [InlineData("XCBR", "SumSwARs1", "actVal;q;t;pulsQty", "ST;CF", "BCR")]
    [InlineData("GGIO", "SPCSO1", "ctlVal;q;t;ctlModel", "CO;ST;CF", "SPC")]
    [InlineData("MMXU", "PhV", "phsA.cVal.mag.f;phsB.cVal.mag.f;phsC.cVal.mag.f;q;t", "MX", "WYE")]
    [InlineData("MMXU", "TotW", "instMag.f;mag.f;q;t;units.SIUnit", "MX;CF", "MV")]
    public void CdcInference_Uses_Standard_Cdc_Values_For_Common_Live_Discovery_Patterns(
        string lnClass,
        string doName,
        string attributes,
        string fcs,
        string expectedCdc)
    {
        var result = CdcInferenceEngine.Infer(
            lnClass,
            doName,
            attributes.Split(';', StringSplitOptions.RemoveEmptyEntries),
            fcs.Split(';', StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal(expectedCdc, result.Cdc);
        Assert.True(CdcInferenceEngine.IsKnownCdc(result.Cdc));
    }

    [Theory]
    [InlineData("GEN")]
    [InlineData("Status")]
    [InlineData("Controllable")]
    [InlineData("Setting")]
    [InlineData("Measurement")]
    public void CdcInference_Rejects_Internal_Labels_As_Cdc_Values(string cdc)
    {
        Assert.False(CdcInferenceEngine.IsKnownCdc(cdc));
    }

    [Fact]
    public void Builds_logical_model_and_infers_protection_operation_cdc()
    {
        var result = new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory(
            [
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.stVal",
                    MmsItemName = "A50PTOC1$ST$Op$stVal"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.q",
                    MmsItemName = "A50PTOC1$ST$Op$q"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op.t",
                    MmsItemName = "A50PTOC1$ST$Op$t"
                }
            ])
        };

        var document = LiveIedModelDiscoveryBuilder.Build(result, new LiveIedModelDiscoveryBuildOptions { Host = "192.0.2.10" });

        Assert.Equal(1, document.Coverage.LogicalDeviceCount);
        Assert.Equal(1, document.Coverage.LogicalNodeCount);
        Assert.Equal(1, document.Coverage.DataObjectCount);
        Assert.Equal(3, document.Coverage.DataAttributeCount);
        var dataObject = document.LogicalDevices[0].LogicalNodes[0].DataObjects[0];
        Assert.Equal("ACT", dataObject.InferredCdc);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.High, dataObject.ConfidenceLevel);
        Assert.Equal("A50", document.LogicalDevices[0].LogicalNodes[0].Prefix);
        Assert.Equal("PTOC", document.LogicalDevices[0].LogicalNodes[0].LnClass);
        Assert.Equal("1", document.LogicalDevices[0].LogicalNodes[0].LnInst);
    }

    [Fact]
    public void Parses_dataset_directory_members_into_model()
    {
        var directory = new MmsIedModelDirectory(
        [
            new MmsFcResolvedPoint
            {
                Domain = "LD0",
                LogicalNode = "LLN0",
                FunctionalConstraint = "BR",
                DataObjectPath = "brcbA01.RptEna",
                MmsItemName = "LLN0$BR$brcbA01$RptEna"
            }
        ]);
        var result = new MmsDiscoveryResult
        {
            IedDirectory = directory,
            ReportInventory = new MmsReportInventory()
        };
        result.ReportInventory.DataSets.Add(new MmsDataSetCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "DataSet",
            Reference = "LD0/LLN0.DataSet"
        });
        result.ReportInventory.ReportControls.Add(new MmsReportControlCandidate
        {
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "brcbA01",
            Reference = "LD0/LLN0.BR.brcbA01",
            Buffered = true,
            DataSetReference = "LD0/LLN0.DataSet"
        });
        var dataSetDirectory = new MmsDataSetDirectoryResult
        {
            IsSuccess = true,
            DataSetReference = "LD0/LLN0.DataSet",
            Members =
            [
                new MmsDataSetDirectoryMember
                {
                    Domain = "LD0",
                    LogicalNode = "A50PTOC1",
                    FunctionalConstraint = "ST",
                    DataObjectPath = "Op",
                    UserReference = "LD0/A50PTOC1.Op",
                    MmsItemName = "A50PTOC1$ST$Op",
                    Confidence = 100
                }
            ]
        };

        var document = LiveIedModelDiscoveryBuilder.Build(result, new LiveIedModelDiscoveryBuildOptions(), [dataSetDirectory]);

        Assert.Single(document.DataSets);
        Assert.Equal(1, document.DataSets[0].MemberCount);
        Assert.Equal("LD0/A50PTOC1.Op", document.DataSets[0].Members[0].Reference);
        Assert.Equal("LD0/LLN0.BR.brcbA01", document.DataSets[0].UsedByReportControls[0]);
    }

    [Fact]
    public void Attaches_exact_mms_type_discovery_to_data_attributes()
    {
        var point = new MmsFcResolvedPoint
        {
            Domain = "LD0",
            LogicalNode = "A50PTOC1",
            FunctionalConstraint = "ST",
            DataObjectPath = "Op.stVal",
            MmsItemName = "A50PTOC1$ST$Op$stVal"
        };
        var result = new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory([point])
        };
        var typeResult = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true,
            Reference = new MmsObjectReference("LD0", "A50PTOC1$ST$Op$stVal", "ST"),
            TypeSpecification = new MmsTypeSpecificationNode
            {
                MmsType = "boolean",
                SclBType = "BOOLEAN"
            },
            Message = "type ok"
        };

        var document = LiveIedModelDiscoveryBuilder.Build(
            result,
            new LiveIedModelDiscoveryBuildOptions(),
            variableTypeAttributes: [typeResult]);

        var attribute = document.LogicalDevices[0].LogicalNodes[0].DataObjects[0].Attributes[0];
        Assert.Equal("BOOLEAN", attribute.SclBType);
        Assert.Equal("boolean", attribute.MmsType);
        Assert.Equal("Exact", attribute.TypeDiscoveryStatus);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Exact, attribute.TypeConfidence);
        Assert.Equal(1, document.Coverage.VariableTypeReadSuccessCount);
        Assert.Equal(1, document.Coverage.ExactMmsTypeCount);
    }

}
