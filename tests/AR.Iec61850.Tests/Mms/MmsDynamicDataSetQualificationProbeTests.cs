using System.Text;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsDynamicDataSetQualificationProbeTests
{
    [Fact]
    public void DefaultOptions_AreBoundedWithoutClaimingIedCapability()
    {
        var options = new MmsDynamicDataSetQualificationProbeOptions();

        Assert.Equal(64, options.ApplicationSafetyMemberLimit);
        Assert.Equal(256, MmsDynamicDataSetQualificationProbeOptions.AbsoluteApplicationSafetyMemberLimit);
        Assert.True(options.RejectKnownNegotiatedPduOverflow);
    }

    [Fact]
    public void ValidateOptions_RejectsZeroAndAboveAbsoluteSafetyCeiling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MmsDynamicDataSetQualificationPolicy.ValidateOptions(
                new MmsDynamicDataSetQualificationProbeOptions
                {
                    ApplicationSafetyMemberLimit = 0
                }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MmsDynamicDataSetQualificationPolicy.ValidateOptions(
                new MmsDynamicDataSetQualificationProbeOptions
                {
                    ApplicationSafetyMemberLimit =
                        MmsDynamicDataSetQualificationProbeOptions.AbsoluteApplicationSafetyMemberLimit + 1
                }));
    }

    [Fact]
    public void ExactOrderedMembersMatch_RequiresSameCountAndOrder()
    {
        string[] expected =
        [
            "LD0/GGIO1$ST$Ind1$stVal",
            "LD0/GGIO1$ST$Ind2$stVal",
            "LD1/MMXU1$MX$Hz$mag$f"
        ];

        Assert.True(MmsDynamicDataSetQualificationPolicy.ExactOrderedMembersMatch(
            expected,
            expected.ToArray()));

        Assert.False(MmsDynamicDataSetQualificationPolicy.ExactOrderedMembersMatch(
            expected,
            [expected[1], expected[0], expected[2]]));

        Assert.False(MmsDynamicDataSetQualificationPolicy.ExactOrderedMembersMatch(
            expected,
            expected[..2]));
    }

    [Theory]
    [InlineData(100, null, true)]
    [InlineData(100, 0, true)]
    [InlineData(100, 100, true)]
    [InlineData(100, 101, true)]
    [InlineData(101, 100, false)]
    public void NegotiatedPduPreflight_IsConservative(
        int requestBytes,
        int? maxPdu,
        bool expected)
    {
        Assert.Equal(
            expected,
            MmsDynamicDataSetQualificationPolicy.IsWithinKnownNegotiatedPdu(requestBytes, maxPdu));
    }

    [Fact]
    public void MultiMemberDefineRequest_IsDeterministicAndPreservesMemberOrder()
    {
        MmsObjectReference[] members =
        [
            new("LD0", "GGIO1$ST$Ind1$stVal", "ST"),
            new("LD0", "GGIO1$ST$Ind2$stVal", "ST"),
            new("LD1", "MMXU1$MX$Hz$mag$f", "MX"),
            new("LD1", "MMXU1$MX$TotW$mag$f", "MX")
        ];

        var first = MmsDefineNamedVariableListRequest.Build(
            21,
            "LD0/LLN0.AR_G2Q_04",
            members);
        var second = MmsDefineNamedVariableListRequest.Build(
            21,
            "LD0/LLN0.AR_G2Q_04",
            members);

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
        Assert.True(ContainsAscii(first, "LLN0$AR_G2Q_04"));

        var positions = members
            .Select(member => IndexOfAscii(first, member.Item))
            .ToArray();

        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.True(positions.SequenceEqual(positions.OrderBy(position => position)));
    }

    [Fact]
    public void EncodedDefineRequest_GrowsWithQualificationMemberCount()
    {
        var one = MmsDefineNamedVariableListRequest.Build(
            22,
            "LD0/LLN0.AR_G2Q_01",
            BuildMembers(1));
        var four = MmsDefineNamedVariableListRequest.Build(
            22,
            "LD0/LLN0.AR_G2Q_04",
            BuildMembers(4));
        var eight = MmsDefineNamedVariableListRequest.Build(
            22,
            "LD0/LLN0.AR_G2Q_08",
            BuildMembers(8));

        Assert.True(one.Length < four.Length);
        Assert.True(four.Length < eight.Length);
    }

    [Fact]
    public void Result_DefineSucceededButAssociationLostBeforeDelete_FailsCleanupClosed()
    {
        var result = new MmsDynamicDataSetQualificationProbeResult
        {
            DirectoryAttempted = true,
            RequestedMemberReferences = ["LD0/GGIO1$ST$Ind1$stVal"],
            DefineEvidence = new MmsDynamicDataSetQualificationServiceEvidence
            {
                Attempted = true,
                IsSuccess = true,
                StateBefore = MmsAssociationState.MmsInitiated,
                StateAfter = MmsAssociationState.MmsInitiated,
                MemberReferences = ["LD0/GGIO1$ST$Ind1$stVal"]
            },
            DeleteEvidence = new MmsDynamicDataSetQualificationServiceEvidence
            {
                Attempted = false,
                IsSuccess = false,
                StateBefore = MmsAssociationState.MmsInitiateFailed,
                StateAfter = MmsAssociationState.MmsInitiateFailed,
                MemberReferences = ["LD0/GGIO1$ST$Ind1$stVal"]
            }
        };

        Assert.True(result.DynamicMutationAttempted);
        Assert.False(result.CleanupAttempted);
        Assert.False(result.CleanupSucceeded);
        Assert.False(result.AssociationSurvived);
    }

    private static MmsObjectReference[] BuildMembers(int count)
        => Enumerable.Range(1, count)
            .Select(index => new MmsObjectReference(
                "LD0",
                $"GGIO1$ST$Ind{index}$stVal",
                "ST"))
            .ToArray();

    private static bool ContainsAscii(byte[] source, string text)
        => IndexOfAscii(source, text) >= 0;

    private static int IndexOfAscii(byte[] source, string text)
    {
        var needle = Encoding.ASCII.GetBytes(text);
        if (needle.Length == 0 || source.Length < needle.Length)
            return -1;

        for (var offset = 0; offset <= source.Length - needle.Length; offset++)
        {
            var match = true;
            for (var index = 0; index < needle.Length; index++)
            {
                if (source[offset + index] == needle[index])
                    continue;
                match = false;
                break;
            }

            if (match)
                return offset;
        }

        return -1;
    }
}
