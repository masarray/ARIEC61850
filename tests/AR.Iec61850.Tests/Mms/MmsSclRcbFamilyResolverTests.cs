using AR.Iec61850.Mms;
using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsSclRcbFamilyResolverTests
{
    [Fact]
    public void Resolve_IndexedControlKeepsExactAndConcreteIndexedSiblings()
    {
        var scl = CreateReportControl("Buffer", indexed: true);
        var live = new[]
        {
            CreateCandidate("Buffer", "LD0/LLN0.BR.Buffer"),
            CreateCandidate("Buffer01", "LD0/LLN0.BR.Buffer01")
        };

        var result = MmsSclRcbFamilyResolver.Resolve(scl, live);

        Assert.True(result.IsSuccess);
        Assert.Equal(MmsSclRcbFamilyResolutionKind.IndexedFamily, result.Kind);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(result.Candidates, candidate => candidate.Reference == "LD0/LLN0.BR.Buffer");
        Assert.Contains(result.Candidates, candidate => candidate.Reference == "LD0/LLN0.BR.Buffer01");
    }

    [Theory]
    [InlineData("Buffer1")]
    [InlineData("Buffer01")]
    [InlineData("Buffer001")]
    public void Resolve_AcceptsDecimalIndexedVendorWidths(string liveName)
    {
        var scl = CreateReportControl("Buffer", indexed: true);

        var result = MmsSclRcbFamilyResolver.Resolve(
            scl,
            [CreateCandidate(liveName, $"LD0/LLN0.BR.{liveName}")]);

        Assert.Equal(MmsSclRcbFamilyResolutionKind.IndexedFamily, result.Kind);
        Assert.Single(result.Candidates);
    }

    [Theory]
    [InlineData("BufferA")]
    [InlineData("BufferBackup01")]
    [InlineData("Buffer01A")]
    public void Resolve_RejectsNonDecimalFamilySuffixes(string liveName)
    {
        var scl = CreateReportControl("Buffer", indexed: true);

        var result = MmsSclRcbFamilyResolver.Resolve(
            scl,
            [CreateCandidate(liveName, $"LD0/LLN0.BR.{liveName}")]);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Resolve_NonIndexedControlRequiresExactLiveName()
    {
        var scl = CreateReportControl("Buffer", indexed: false);

        var result = MmsSclRcbFamilyResolver.Resolve(
            scl,
            [CreateCandidate("Buffer01", "LD0/LLN0.BR.Buffer01")]);

        Assert.False(result.IsSuccess);
        Assert.Contains("Non-indexed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_DoesNotCrossFunctionalConstraintOrLogicalNode()
    {
        var scl = CreateReportControl("Buffer", indexed: true);
        var live = new[]
        {
            CreateCandidate("Buffer01", "LD0/LLN0.RP.Buffer01", fc: "RP", buffered: false),
            CreateCandidate("Buffer02", "LD0/GGIO1.BR.Buffer02", logicalNode: "GGIO1")
        };

        var result = MmsSclRcbFamilyResolver.Resolve(scl, live);

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("ld0", "LLN0", "BR", "Buffer01")]
    [InlineData("LD0", "lln0", "BR", "Buffer01")]
    [InlineData("LD0", "LLN0", "br", "Buffer01")]
    [InlineData("LD0", "LLN0", "BR", "buffer01")]
    public void Resolve_IsCaseSensitiveForCanonicalObjectIdentity(
        string domain,
        string logicalNode,
        string fc,
        string name)
    {
        var scl = CreateReportControl("Buffer", indexed: true);
        var live = CreateCandidate(
            name,
            $"{domain}/{logicalNode}.{fc}.{name}",
            logicalNode,
            fc,
            buffered: fc == "BR",
            domain: domain);

        var result = MmsSclRcbFamilyResolver.Resolve(scl, [live]);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Resolve_UsesLiveCandidateFieldsAsAuthorityAcrossReferenceSeparatorStyles()
    {
        var scl = new SclReportControl
        {
            IedName = string.Empty,
            LdInst = string.Empty,
            LogicalNodePath = "LLN0",
            Name = "Buffer",
            Buffered = true,
            Indexed = true,
            ControlBlockReference = "LD0/LLN0$BR$Buffer"
        };
        var live = CreateCandidate("Buffer01", "LD0/LLN0$BR$Buffer01");

        var result = MmsSclRcbFamilyResolver.Resolve(scl, [live]);

        Assert.True(result.IsSuccess);
        Assert.Equal("LD0/LLN0$BR$Buffer01", result.Candidates[0].Reference);
    }

    private static SclReportControl CreateReportControl(string name, bool indexed)
        => new()
        {
            LogicalNodePath = "LLN0",
            Name = name,
            Buffered = true,
            Indexed = indexed,
            ControlBlockReference = $"LD0/LLN0$BR${name}",
            DataSetReference = "LD0/LLN0.DataSet"
        };

    private static MmsReportControlCandidate CreateCandidate(
        string name,
        string reference,
        string logicalNode = "LLN0",
        string fc = "BR",
        bool buffered = true,
        string domain = "LD0")
        => new()
        {
            Domain = domain,
            LogicalNode = logicalNode,
            FunctionalConstraint = fc,
            Name = name,
            Reference = reference,
            Buffered = buffered,
            DataSetReference = "LD0/LLN0.DataSet",
            EnabledState = "false",
            ReservationTimeSeconds = buffered ? "0" : string.Empty,
            ReservationState = buffered ? string.Empty : "false",
            Status = "Attribute-probed"
        };
}
