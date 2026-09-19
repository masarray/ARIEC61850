using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class InitialFcReadCfOrderingRegressionTests
{
    [Fact]
    public void Planner_Splits_MultiDataObject_CF_Into_Exact_DataObject_Reads()
    {
        var plan = InitialFcReadPlanner.FromSclModel(BuildTctrLikeDesign());

        var cfTargets = plan.Targets
            .Where(target => target.FunctionalConstraint == "CF")
            .ToArray();

        Assert.Equal(3, cfTargets.Length);
        Assert.Equal(
            [
                "IED1LD0/I01ATCTR1$CF$AmpSv",
                "IED1LD0/I01ATCTR1$CF$Mod",
                "IED1LD0/I01ATCTR1$CF$ARtg"
            ],
            cfTargets.Select(target => target.MmsReference).ToArray());
        Assert.All(cfTargets, target => Assert.True(target.IsDataObjectScoped));
        Assert.All(cfTargets, target => Assert.Single(target.DataObjects));
        Assert.DoesNotContain(plan.Targets, target => target.MmsReference == "IED1LD0/I01ATCTR1$CF");
    }

    [Fact]
    public void Projector_DataObjectScoped_Read_Does_Not_Require_FcRoot_Wrapper()
    {
        var plan = InitialFcReadPlanner.FromSclModel(BuildTctrLikeDesign());
        var ampSv = Assert.Single(
            plan.Targets,
            target => target.MmsReference == "IED1LD0/I01ATCTR1$CF$AmpSv");

        var projection = InitialFcValueProjector.Project(
            ampSv,
            MmsDataValue.Structure(
            [
                MmsDataValue.Integer(1),
                MmsDataValue.Integer(2),
                MmsDataValue.Integer(3),
                MmsDataValue.Integer(4)
            ]));

        Assert.Empty(projection.Errors);
        Assert.Equal(4, projection.Leaves.Count);
        Assert.Equal(
            ["sVC.scaleFactor", "sVC.offset", "min", "max"],
            projection.Leaves.Select(leaf => leaf.AttributePath).ToArray());
    }

    [Fact]
    public void MultiDataObject_NonCF_Remains_FcRoot_Read()
    {
        var design = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IED1LD0",
                    Inst = "LD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "GGIO1",
                            DataObjects =
                            [
                                DataObject("GGIO1", "Ind1", "ST", ("stVal", "BOOLEAN")),
                                DataObject("GGIO1", "Ind2", "ST", ("stVal", "BOOLEAN"))
                            ]
                        }
                    ]
                }
            ]
        };

        var target = Assert.Single(InitialFcReadPlanner.FromSclModel(design).Targets);

        Assert.Equal("IED1LD0/GGIO1$ST", target.MmsReference);
        Assert.False(target.IsDataObjectScoped);
        Assert.Equal(2, target.DataObjects.Count);
    }

    private static LiveIedModelDiscoveryDocument BuildTctrLikeDesign()
        => new()
        {
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IED1LD0",
                    Inst = "LD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "I01ATCTR1",
                            LnClass = "TCTR",
                            LnInst = "1",
                            DataObjects =
                            [
                                DataObject(
                                    "I01ATCTR1",
                                    "AmpSv",
                                    "CF",
                                    ("sVC.scaleFactor", "FLOAT32"),
                                    ("sVC.offset", "FLOAT32"),
                                    ("min", "INT32"),
                                    ("max", "INT32")),
                                DataObject("I01ATCTR1", "Mod", "CF", ("ctlModel", "INT32")),
                                DataObject(
                                    "I01ATCTR1",
                                    "ARtg",
                                    "CF",
                                    ("setMag.f", "FLOAT32"),
                                    ("units.SIUnit", "Enum"),
                                    ("units.multiplier", "Enum"),
                                    ("min.f", "FLOAT32"),
                                    ("max.f", "FLOAT32"))
                            ]
                        }
                    ]
                }
            ]
        };

    private static LiveIedDataObjectModel DataObject(
        string logicalNode,
        string name,
        string fc,
        params (string Path, string BType)[] leaves)
        => new()
        {
            Name = name,
            Reference = $"IED1LD0/{logicalNode}.{name}",
            Attributes = leaves.Select(leaf => new LiveIedDataAttributeModel
            {
                ObjectReference = $"IED1LD0/{logicalNode}.{name}.{leaf.Path}",
                AttributePath = leaf.Path,
                FunctionalConstraint = fc,
                SclBType = leaf.BType
            }).ToArray()
        };
}
