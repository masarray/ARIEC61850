using AR.Iec61850.Asn1;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsRawDirectoryIdentityRecoveryTests
{
    [Fact]
    public void Decide_LeadingSlashRawIdentity_IsNewEvidenceAndShouldRetry()
    {
        var decision = MmsRawDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00056.cfg",
            [SuccessPage(new MmsFileDirectoryEntry
            {
                Name = "COMTRADE/FRA00056.cfg",
                Path = "COMTRADE/FRA00056.cfg",
                RawName = "/COMTRADE/FRA00056.cfg",
                RawNameComponents = ["/COMTRADE/FRA00056.cfg"]
            })]);

        Assert.True(decision.ShouldRetry);
        Assert.Equal("/COMTRADE/FRA00056.cfg", decision.CandidateFileName);
    }

    [Fact]
    public void Decide_UnrootedBackslashRawIdentity_IsNewEvidenceAndShouldRetry()
    {
        var decision = MmsRawDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00056.cfg",
            [SuccessPage(new MmsFileDirectoryEntry
            {
                Name = "COMTRADE/FRA00056.cfg",
                Path = "COMTRADE/FRA00056.cfg",
                RawName = @"COMTRADE\FRA00056.cfg",
                RawNameComponents = [@"COMTRADE\FRA00056.cfg"]
            })]);

        Assert.True(decision.ShouldRetry);
        Assert.Equal(@"COMTRADE\FRA00056.cfg", decision.CandidateFileName);
    }

    [Fact]
    public void Decide_NormalizedSlashRawIdentity_WasAlreadyTried()
    {
        var decision = MmsRawDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00056.cfg",
            [SuccessPage(new MmsFileDirectoryEntry
            {
                Name = "COMTRADE/FRA00056.cfg",
                Path = "COMTRADE/FRA00056.cfg",
                RawName = "COMTRADE/FRA00056.cfg",
                RawNameComponents = ["COMTRADE/FRA00056.cfg"]
            })]);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(MmsRawDirectoryIdentityRecoveryStatus.RawIdentityAlreadyTried, decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_RootedBackslashRawIdentity_WasAlreadyTriedByP0Fallback()
    {
        var decision = MmsRawDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00056.cfg",
            [SuccessPage(new MmsFileDirectoryEntry
            {
                Name = "COMTRADE/FRA00056.cfg",
                Path = "COMTRADE/FRA00056.cfg",
                RawName = @"\COMTRADE\FRA00056.cfg",
                RawNameComponents = [@"\COMTRADE\FRA00056.cfg"]
            })]);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(MmsRawDirectoryIdentityRecoveryStatus.RawIdentityAlreadyTried, decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_MultiComponentFileName_DoesNotInventSingleRawIdentity()
    {
        var decision = MmsRawDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00056.cfg",
            [SuccessPage(new MmsFileDirectoryEntry
            {
                Name = "COMTRADE/FRA00056.cfg",
                Path = "COMTRADE/FRA00056.cfg",
                RawName = string.Empty,
                RawNameComponents = ["COMTRADE", "FRA00056.cfg"]
            })]);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(MmsRawDirectoryIdentityRecoveryStatus.NoRawSingleGraphicString, decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_IncompleteDirectoryRead_RemainsInconclusive()
    {
        var decision = MmsRawDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00056.cfg",
            [new MmsFileDirectoryResult
            {
                IsSuccess = true,
                DirectoryName = "COMTRADE",
                MoreFollows = true,
                Entries = [new MmsFileDirectoryEntry
                {
                    Name = "COMTRADE/FRA00055.cfg",
                    Path = "COMTRADE/FRA00055.cfg",
                    RawName = "/COMTRADE/FRA00055.cfg",
                    RawNameComponents = ["/COMTRADE/FRA00055.cfg"]
                }]
            }]);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(MmsRawDirectoryIdentityRecoveryStatus.DirectoryReadFailed, decision.NoRetryStatus);
    }

    [Fact]
    public void Decide_CompletedDirectoryWithoutEntry_DoesNotInventIdentity()
    {
        var decision = MmsRawDirectoryIdentityRecoveryPolicy.Decide(
            "COMTRADE/FRA00056.cfg",
            [SuccessPage(new MmsFileDirectoryEntry
            {
                Name = "COMTRADE/FRA00055.cfg",
                Path = "COMTRADE/FRA00055.cfg",
                RawName = "/COMTRADE/FRA00055.cfg",
                RawNameComponents = ["/COMTRADE/FRA00055.cfg"]
            })]);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(MmsRawDirectoryIdentityRecoveryStatus.EntryNoLongerPresent, decision.NoRetryStatus);
    }

    [Theory]
    [InlineData("/COMTRADE/FRA00056.cfg")]
    [InlineData(@"COMTRADE\FRA00056.cfg")]
    public void RawFileOpenRequest_PreservesExactSingleGraphicString(string rawName)
    {
        var request = MmsRawObservedFileOpenRequest.Build(23, rawName);
        var mms = MmsPresentation.StripPresentationPrefix(request);
        var outer = ReadSingle(mms);
        var outerChildren = BerReader.ReadChildren(outer.Value);
        Assert.Equal((ulong)23, BerReader.ReadUnsignedInteger(outerChildren[0]));

        var service = outerChildren[1];
        Assert.Equal(BerClass.ContextSpecific, service.Class);
        Assert.True(service.Constructed);
        Assert.Equal(72, service.TagNumber);

        var parameters = BerReader.ReadChildren(service.Value);
        Assert.Equal(2, parameters.Count);
        Assert.Equal(0, parameters[0].TagNumber);
        var graphicString = Assert.Single(BerReader.ReadChildren(parameters[0].Value));
        Assert.Equal((byte)0x19, graphicString.EncodedTag);
        Assert.Equal(rawName, BerReader.ReadAsciiString(graphicString));
        Assert.Equal(1, parameters[1].TagNumber);
    }

    [Fact]
    public void RecoveryStatus_OrdinalsAreStableAndAppendOnly()
    {
        Assert.Equal(0, (int)MmsRawDirectoryIdentityRecoveryStatus.NotChecked);
        Assert.Equal(1, (int)MmsRawDirectoryIdentityRecoveryStatus.RecoveredByRawObservedFileName);
        Assert.Equal(2, (int)MmsRawDirectoryIdentityRecoveryStatus.RawObservedFileNameRetryFailed);
        Assert.Equal(3, (int)MmsRawDirectoryIdentityRecoveryStatus.NoRawSingleGraphicString);
        Assert.Equal(4, (int)MmsRawDirectoryIdentityRecoveryStatus.RawIdentityAlreadyTried);
        Assert.Equal(5, (int)MmsRawDirectoryIdentityRecoveryStatus.EntryNoLongerPresent);
        Assert.Equal(6, (int)MmsRawDirectoryIdentityRecoveryStatus.DirectoryReadFailed);
        Assert.Equal(7, (int)MmsRawDirectoryIdentityRecoveryStatus.AssociationUnavailable);
        Assert.Equal(8, (int)MmsRawDirectoryIdentityRecoveryStatus.RetryUnavailable);
    }

    private static MmsFileDirectoryResult SuccessPage(params MmsFileDirectoryEntry[] entries)
        => new()
        {
            IsSuccess = true,
            DirectoryName = "COMTRADE",
            MoreFollows = false,
            Entries = entries,
            Message = "ok"
        };

    private static BerTlv ReadSingle(ReadOnlyMemory<byte> source)
    {
        var offset = 0;
        Assert.True(BerReader.TryReadTlv(source, ref offset, out var tlv));
        Assert.Equal(source.Length, offset);
        return tlv;
    }
}
