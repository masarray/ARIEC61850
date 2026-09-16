using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class LiveIedTypeHierarchyPointMaterializerTests
{
    [Fact]
    public void Build_MaterializesMissingNamedLnTreeLeavesWithoutReplacingObservedPoints()
    {
        var observed = new MmsFcResolvedPoint
        {
            Domain = "LD0",
            LogicalNode = "CSWI1",
            FunctionalConstraint = "ST",
            DataObjectPath = "Pos.stVal",
            MmsItemName = "CSWI1$ST$Pos$stVal",
            Source = "LiveMmsGetNameList",
            Confidence = 100
        };
        var directory = new MmsIedModelDirectory([observed]);
        var result = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true,
            Reference = new MmsObjectReference("LD0", "CSWI1", string.Empty),
            TypeSpecification = Structure(
                "",
                Structure(
                    "ST",
                    Structure(
                        "Pos",
                        Leaf("stVal", "bit-string", "Dbpos"),
                        Leaf("q", "bit-string", "Quality"),
                        Leaf("t", "utc-time", "Timestamp"),
                        Leaf("stSeld", "boolean", "BOOLEAN"))),
                Structure(
                    "CO",
                    Structure(
                        "Pos",
                        Leaf("SBO", "visible-string", "ObjRef"),
                        Structure(
                            "Oper",
                            Leaf("ctlVal", "bit-string", "Dbpos"),
                            Leaf("Test", "boolean", "BOOLEAN")))),
                Structure(
                    "CF",
                    Structure(
                        "Pos",
                        Leaf("ctlModel", "integer", "Enum"),
                        Leaf("sboTimeout", "unsigned", "INT32U"),
                        Leaf("operTimeout", "unsigned", "INT32U"))),
                Structure(
                    "ZZ",
                    Structure(
                        "Ignored",
                        Leaf("value", "integer", "INT32"))),
                new MmsTypeSpecificationNode
                {
                    Name = "MX",
                    MmsType = "structure",
                    SclBType = "Struct",
                    Children =
                    [
                        new MmsTypeSpecificationNode
                        {
                            Name = "Harm",
                            MmsType = "array",
                            SclBType = "Struct",
                            Children = [Leaf("element", "floating-point", "FLOAT32")]
                        }
                    ]
                })
        };

        var index = LiveIedVariableTypeHierarchyIndex.Build(directory, [result]);

        Assert.Equal(10, directory.PointCount);
        Assert.True(directory.TryFindByMmsReference("LD0/CSWI1$CO$Pos$SBO", out var sbo));
        Assert.Equal("Pos.SBO", sbo.DataObjectPath);
        Assert.Equal("GetVariableAccessAttributesLogicalNodeTree", sbo.Source);
        Assert.True(index.TryResolve(sbo, out var sboType));
        Assert.Equal("ObjRef", sboType.TypeSpecification.SclBType);

        Assert.True(directory.TryFindByMmsReference("LD0/CSWI1$ST$Pos$stVal", out var retained));
        Assert.Equal("LiveMmsGetNameList", retained.Source);

        Assert.DoesNotContain(directory.Points, point => point.FunctionalConstraint == "ZZ");
        Assert.DoesNotContain(directory.Points, point => point.MmsItemName.Contains("$element", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_DoesNotMaterializeHierarchyForUnknownLogicalNode()
    {
        var directory = new MmsIedModelDirectory(
        [
            new MmsFcResolvedPoint
            {
                Domain = "LD0",
                LogicalNode = "CSWI1",
                FunctionalConstraint = "ST",
                DataObjectPath = "Pos.stVal",
                MmsItemName = "CSWI1$ST$Pos$stVal"
            }
        ]);
        var foreign = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true,
            Reference = new MmsObjectReference("LD0", "XCBR9", string.Empty),
            TypeSpecification = Structure(
                "",
                Structure("ST", Structure("Pos", Leaf("q", "bit-string", "Quality"))))
        };

        var index = LiveIedVariableTypeHierarchyIndex.Build(directory, [foreign]);

        Assert.Single(directory.Points);
        Assert.False(directory.TryFindByMmsReference("LD0/XCBR9$ST$Pos$q", out _));
        Assert.Equal(0, index.ResolvedAttributeCount);
    }

    private static MmsTypeSpecificationNode Structure(
        string name,
        params MmsTypeSpecificationNode[] children)
        => new()
        {
            Name = name,
            MmsType = "structure",
            SclBType = "Struct",
            Children = children
        };

    private static MmsTypeSpecificationNode Leaf(
        string name,
        string mmsType,
        string sclBType)
        => new()
        {
            Name = name,
            MmsType = mmsType,
            SclBType = sclBType
        };
}
