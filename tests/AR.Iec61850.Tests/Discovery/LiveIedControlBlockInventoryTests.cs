using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Discovery;

public sealed class LiveIedControlBlockInventoryTests
{
    [Fact]
    public void Builder_Groups_Goose_Sv_Setting_And_Log_Control_Blocks_From_Fc_Attributes()
    {
        var discovery = new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory(
            [
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "GO",
                    DataObjectPath = "gcbA01.GoEna",
                    MmsItemName = "LLN0$GO$gcbA01$GoEna"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "GO",
                    DataObjectPath = "gcbA01.DatSet",
                    MmsItemName = "LLN0$GO$gcbA01$DatSet"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "MS",
                    DataObjectPath = "msvcb01.SvEna",
                    MmsItemName = "LLN0$MS$msvcb01$SvEna"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "SG",
                    DataObjectPath = "SGCB.ActSG",
                    MmsItemName = "LLN0$SG$SGCB$ActSG"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "LG",
                    DataObjectPath = "lcbA01.LogEna",
                    MmsItemName = "LLN0$LG$lcbA01$LogEna"
                }
            ])
        };

        var document = LiveIedModelDiscoveryBuilder.Build(discovery, new LiveIedModelDiscoveryBuildOptions());

        Assert.Single(document.GooseControlBlocks);
        Assert.Single(document.SampledValueControlBlocks);
        Assert.Single(document.SettingGroupControls);
        Assert.Single(document.LogControls);
        Assert.Equal("gcbA01", document.GooseControlBlocks[0].Name);
        Assert.Contains("DatSet", document.GooseControlBlocks[0].Attributes);
        Assert.Equal("AttributePresentValueNotRead", document.GooseControlBlocks[0].DataSetReferenceStatus);
        Assert.Equal(1, document.Coverage.GooseControlBlockCount);
        Assert.Equal(1, document.Coverage.SampledValueControlBlockCount);
        Assert.Equal(1, document.Coverage.SettingGroupControlCount);
        Assert.Equal(1, document.Coverage.LogControlCount);
    }

    [Fact]
    public void Builder_Detects_SettingGroupControl_From_Sp_Sgcb_Attributes()
    {
        var discovery = new MmsDiscoveryResult
        {
            IedDirectory = new MmsIedModelDirectory(
            [
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "SP",
                    DataObjectPath = "SGCB.ActSG",
                    MmsItemName = "LLN0$SP$SGCB$ActSG"
                },
                new MmsFcResolvedPoint
                {
                    Domain = "LD0",
                    LogicalNode = "LLN0",
                    FunctionalConstraint = "SP",
                    DataObjectPath = "SGCB.NumOfSG",
                    MmsItemName = "LLN0$SP$SGCB$NumOfSG"
                }
            ])
        };

        var document = LiveIedModelDiscoveryBuilder.Build(discovery, new LiveIedModelDiscoveryBuildOptions());

        Assert.Single(document.SettingGroupControls);
        Assert.Equal("SGCB", document.SettingGroupControls[0].Name);
        Assert.Equal("SP", document.SettingGroupControls[0].FunctionalConstraint);
        Assert.Equal(1, document.Coverage.SettingGroupControlCount);
    }
}
