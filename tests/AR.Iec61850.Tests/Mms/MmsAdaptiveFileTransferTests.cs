using System.Text;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsAdaptiveFileTransferTests
{
    [Fact]
    public void RootedFileOpenRequest_MatchesCapturedSingleGraphicStringForm()
    {
        const string fileName = "28_248202310728550_FRA00028.cfg";

        var request = MmsRootBackslashFileOpenRequest.Build(229, fileName);
        var rootedName = Encoding.ASCII.GetBytes("\\" + fileName);

        Assert.True(request.AsSpan().IndexOf(rootedName) >= 0);
        Assert.Contains((byte)0xBF, request);
        Assert.Contains((byte)0x48, request);
        Assert.Contains((byte)0x19, request);
        Assert.Equal("\\28_248202310728550_FRA00028.cfg", MmsRootBackslashFileOpenRequest.BuildRootedPath(fileName));
    }

    [Fact]
    public void RootedFileOpenRequest_UsesBackslashesForNestedPath()
    {
        var path = MmsRootBackslashFileOpenRequest.BuildRootedPath(@"COMTRADE/FRA00028.dat");

        Assert.Equal(@"\COMTRADE\FRA00028.dat", path);
    }

    [Fact]
    public void FallbackPolicy_RetriesOnlyForFileOpenFileNonExistentBeforeRead()
    {
        var result = new MmsFileTransferResult
        {
            IsSuccess = false,
            BytesTransferred = 0,
            ReadOperations = 0,
            Message = "MMS Confirmed-Error PDU during FileOpen: A2 0A 80 01 02 A2 05 A0 03 8B 01 07"
        };

        Assert.True(MmsFileOpenPathFallbackPolicy.ShouldRetryWithRootedBackslash(result, string.Empty));

        var afterRead = new MmsFileTransferResult
        {
            IsSuccess = false,
            BytesTransferred = 1024,
            ReadOperations = 1,
            Message = result.Message
        };
        Assert.False(MmsFileOpenPathFallbackPolicy.ShouldRetryWithRootedBackslash(afterRead, string.Empty));

        var accessDenied = new MmsFileTransferResult
        {
            IsSuccess = false,
            BytesTransferred = 0,
            ReadOperations = 0,
            Message = "MMS Confirmed-Error PDU during FileOpen: A2 0A 80 01 02 A2 05 A0 03 8B 01 03"
        };
        Assert.False(MmsFileOpenPathFallbackPolicy.ShouldRetryWithRootedBackslash(accessDenied, string.Empty));
    }
}
