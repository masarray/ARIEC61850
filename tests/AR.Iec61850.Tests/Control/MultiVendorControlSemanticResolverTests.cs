using AR.Iec61850.Control;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Control;

public sealed class MultiVendorControlSemanticResolverTests
{
    [Theory]
    [InlineData("BSC", "BSC", "bit-string", 2, "LD/ATCC1.TapChg.valWTr.posVal", true)]
    [InlineData("DPC", "DPC", "bit-string", 2, "LD/CSWI1.Pos.stVal", true)]
    [InlineData("SPC", "SPC", "boolean", 0, "LD/GGIO1.Enable.stVal", true)]
    [InlineData("BSC", "DPC", "bit-string", 2, "LD/ATCC1.TapChg.stVal", false)]
    [InlineData("DPC", "BSC", "bit-string", 2, "LD/CSWI1.Pos.valWTr.posVal", false)]
    [InlineData("INC", "INC/ISC", "integer", 0, "LD/GGIO1.Set.stVal", true)]
    [InlineData("", "INC/ISC", "integer", 0, "LD/GGIO1.Set.stVal", false)]
    [InlineData("ISC", "INC/ISC", "bit-string", 2, "LD/ATCC1.Step.stVal", false)]
    [InlineData("", "DPC", "bit-string", 2, "LD/CSWI1.Pos.stVal", true)]
    public void ResolvesOnlyCorroboratedCommandSemantics(
        string declared, string observed, string mmsType, int size, string status, bool safe)
    {
        var reference = status[..status.LastIndexOf('.')];
        if (status.EndsWith(".valWTr.posVal", StringComparison.OrdinalIgnoreCase))
            reference = reference[..reference.LastIndexOf('.')];
        var descriptor = NewDescriptor(reference, observed, mmsType, size, status);
        var result = Iec61850ControlSemanticResolver.Resolve(descriptor, declared);
        Assert.Equal(safe, result.IsCommandSafe);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void VerifiedLiveTapBscSupersedesStaleProjectedSpc_WithoutBooleanFallback()
    {
        var reference = "LD/ATCC1.TapChg";
        var feedback = reference + ".valWTr.posVal";
        var descriptor = NewDescriptor(reference, "BSC", "bit-string", 2, feedback);

        // Reinspect/refresh after a static DataSet projected SPC. The
        // descriptor is generated from the live MMS ctlVal + step status.
        var first = Iec61850ControlSemanticResolver.Resolve(descriptor, "SPC", feedback);
        Assert.True(first.IsCommandSafe);
        Assert.Equal("BSC", first.Cdc);
        Assert.Contains("advisory", first.Reason, StringComparison.OrdinalIgnoreCase);

        // Subsequent Operate validation uses the verified BSC authority.
        var repeated = Iec61850ControlSemanticResolver.Resolve(descriptor, first.Cdc, feedback);
        Assert.True(repeated.IsCommandSafe);
        Assert.Equal("BSC", repeated.Cdc);
    }

    [Theory]
    [InlineData("bit-string", 1)]
    [InlineData("bit-string", 3)]
    [InlineData("boolean", 0)]
    public void ProjectedSpcConflictCannotAuthorizeWrongLiveCtlVal(string wireType, int width)
    {
        var reference = "LD/ATCC1.TapChg";
        var descriptor = NewDescriptor(reference, "BSC", wireType, width,
            reference + ".valWTr.posVal");
        var resolution = Iec61850ControlSemanticResolver.Resolve(descriptor, "SPC");
        Assert.False(resolution.IsCommandSafe);
        Assert.NotEqual("BSC", resolution.Cdc);
    }

    [Fact]
    public void ProjectedSpcConflictCannotOverrideDifferentObjectOrPersistedFeedback()
    {
        var reference = "LD/ATCC1.TapChg";
        var exact = reference + ".valWTr.posVal";
        var differentObject = NewDescriptor(reference, "BSC", "bit-string", 2,
            "LD/ATCC2.TapChg.valWTr.posVal");
        Assert.False(Iec61850ControlSemanticResolver.Resolve(differentObject, "SPC").IsCommandSafe);

        var live = NewDescriptor(reference, "BSC", "bit-string", 2, exact);
        var conflict = Iec61850ControlSemanticResolver.Resolve(
            live, "SPC", "LD/ATCC2.TapChg.valWTr.posVal");
        Assert.False(conflict.IsCommandSafe);
        Assert.Contains("disagrees", conflict.Reason);
    }

    [Fact]
    public void UnrelatedDeclaredCdcDoesNotGainBscAuthority()
    {
        var descriptor = NewDescriptor("LD/ATCC1.TapChg", "BSC", "bit-string", 2,
            "LD/ATCC1.TapChg.valWTr.posVal");
        var result = Iec61850ControlSemanticResolver.Resolve(descriptor, "DPC");
        Assert.False(result.IsCommandSafe);
    }

    [Fact]
    public void DisagreedPersistedBscFeedbackIsNotCommandAuthority()
    {
        var descriptor = NewDescriptor("LD/ATCC1.TapChg", "BSC", "bit-string", 2,
            "LD/ATCC1.TapChg.valWTr.posVal");
        var result = Iec61850ControlSemanticResolver.Resolve(
            descriptor, "BSC", "LD/ATCC2.TapChg.valWTr.posVal");
        Assert.False(result.IsCommandSafe);
        Assert.Contains("disagrees", result.Reason);
    }

    [Fact]
    public void RawTwoBitEncodingAloneCannotEstablishBscDirection()
    {
        var descriptor = NewDescriptor("LD/ATCC1.TapChg", "DPC", "bit-string", 2,
            "LD/ATCC1.TapChg.stVal");
        Assert.False(Iec61850ControlSemanticResolver.Resolve(descriptor, "BSC").IsCommandSafe);
    }


    [Theory]
    [InlineData("SG7_BCUSystem/Bkr0XCBR2.Pos")]
    [InlineData("SG7_BCUSystem/Disc0XSWI1.Pos")]
    [InlineData("SG7_BCUSystem/BkrCSWI4.Pos")]
    [InlineData("LD/CSWI1.Pos")]
    [InlineData("LD/XCBR1.Pos")]
    [InlineData("LD/XSWI1.Pos")]
    public void Edition2PrefixedLogicalNode_BooleanOperAndDbposFeedback_ResolvesDpc(
        string reference)
    {
        // The DPC control value may be MMS Boolean although Pos.stVal is
        // double-point. Class identity comes from <prefix><LNClass><inst>.
        var descriptor = NewDescriptor(reference, "SPC", "boolean", 0,
            reference + ".stVal");
        descriptor = new Iec61850ControlObjectDescriptor
        {
            ObjectReference = descriptor.ObjectReference,
            Cdc = descriptor.Cdc,
            ControlModel = Iec61850ControlModel.SelectBeforeOperateEnhanced,
            CtlValSpecification = descriptor.CtlValSpecification,
            OperSpecification = descriptor.OperSpecification,
            StatusReference = descriptor.StatusReference,
            SelectWithValueSpecification = new MmsTypeSpecificationNode { MmsType = "structure" },
            CancelSpecification = new MmsTypeSpecificationNode { MmsType = "structure" },
            SupportsCommandTermination = true
        };
        var result = Iec61850ControlSemanticResolver.Resolve(descriptor, "DPC");
        Assert.True(result.IsCommandSafe, result.Reason);
        Assert.Equal("DPC", result.Cdc);
        // StatusOnly and missing enhanced SBO components must still fail.
        var blocked = new Iec61850ControlObjectDescriptor
        {
            ObjectReference = reference, Cdc = "SPC",
            ControlModel = Iec61850ControlModel.SelectBeforeOperateEnhanced,
            CtlValSpecification = new MmsTypeSpecificationNode { MmsType = "boolean" },
            StatusReference = reference + ".stVal",
            SupportsCommandTermination = true
        };
        Assert.False(Iec61850ControlSemanticResolver.Resolve(blocked, "DPC").IsCommandSafe);
    }

    [Theory]
    [InlineData("LD/Bkr0XCBR.Pos")]
    [InlineData("LD/Bkr0XCBR2.Mode")]
    [InlineData("LD/Bkr0XCBR2.PosExtra")]
    [InlineData("LD/Bkr0GGIO2.Pos")]
    [InlineData("LD/XCBR1Extra.Pos")]
    [InlineData("LD/NotASwitch1.Pos")]
    public void PositionSemantics_NotInferredWithoutExactPrefixedLnClassInstance(
        string reference)
    {
        var descriptor = NewDescriptor(reference, "SPC", "boolean", 0,
            reference + ".stVal");
        Assert.False(Iec61850ControlSemanticResolver.Resolve(
            descriptor, "DPC").IsCommandSafe);
    }

    [Fact]
    public void DpcBooleanRejectsOtherObjectsStatus_AndWrongWireType()
    {
        var reference = "LD/Bkr0XCBR2.Pos";
        var otherStatus = NewDescriptor(reference, "SPC", "boolean", 0,
            "LD/Bkr0XCBR3.Pos.stVal");
        Assert.False(Iec61850ControlSemanticResolver.Resolve(
            otherStatus, "DPC").IsCommandSafe);
        var invalidShape = NewDescriptor(reference, "SPC", "integer", 0,
            reference + ".stVal");
        Assert.False(Iec61850ControlSemanticResolver.Resolve(
            invalidShape, "DPC").IsCommandSafe);
    }

    [Fact]
    public void PrefixedSwitch_SpcDeclarationNeverGrantsUnverifiedDpc()
    {
        const string reference = "LD/Disc0XSWI9.Pos";
        var descriptor = NewDescriptor(reference, "SPC", "boolean", 0,
            reference + ".stVal");
        var result = Iec61850ControlSemanticResolver.Resolve(descriptor, "SPC");
        Assert.True(result.IsCommandSafe); // A genuinely SPC declaration stays SPC.
        Assert.Equal("SPC", result.Cdc);
        Assert.False(Iec61850ControlSemanticResolver.Resolve(
            descriptor, "APC").IsCommandSafe);
    }

    private static Iec61850ControlObjectDescriptor NewDescriptor(
        string reference, string cdc, string type, int size, string status) => new()
    {
        ObjectReference = reference,
        Cdc = cdc,
        ControlModel = Iec61850ControlModel.DirectNormal,
        CtlValSpecification = new MmsTypeSpecificationNode
        {
            Name = "ctlVal", MmsType = type, Size = size <= 0 ? null : size
        },
        StatusReference = status,
        OperSpecification = new MmsTypeSpecificationNode
        {
            MmsType = "structure",
            Children = new[] { new MmsTypeSpecificationNode { Name = "ctlVal", MmsType = type } }
        }
    };
}
