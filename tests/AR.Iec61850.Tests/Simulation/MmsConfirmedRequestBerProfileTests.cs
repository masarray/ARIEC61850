using AR.Iec61850.Mms;
using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class MmsConfirmedRequestBerProfileTests
{
    [Fact]
    public void Dispatcher_Decodes_Native_Ber_Read_Request_And_Returns_Decodable_Response()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var point = serverProfile.Points.First(x => x.Reference.EndsWith("XCBR1.Pos.stVal", StringComparison.OrdinalIgnoreCase));
        var request = MmsReadRequest.BuildSingleVariableRead(7, MmsObjectReference.Parse(point.Reference));

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, session);
        var read = MmsReadResponseDecoder.DecodeSingleVariable(dispatch.ResponsePresentationPayload, 7);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.Equal(nameof(MmsReadOnlyOperation.Read), dispatch.Response.Operation);
        Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);
        Assert.True(read.IsSuccess, read.Message);
        Assert.NotNull(read.Value);
    }

    [Fact]
    public void Dispatcher_Decodes_GetNameList_Domain_Directory()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var request = MmsGetNameListRequest.Build(8, MmsGetNameListObjectClass.Domain);

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, session);
        var names = MmsGetNameListResponseDecoder.Decode(dispatch.ResponsePresentationPayload, 8);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.Equal(nameof(MmsReadOnlyOperation.GetLogicalDeviceDirectory), dispatch.Response.Operation);
        Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);
        Assert.True(names.IsSuccess, names.Message);
        Assert.Contains("IED1LD0", names.Names);
    }

    [Fact]
    public void Dispatcher_Decodes_GetNameList_NamedVariable_Directory_With_FunctionalConstraints()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var request = MmsGetNameListRequest.Build(11, MmsGetNameListObjectClass.NamedVariable, "IED1LD0");

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, session);
        var names = MmsGetNameListResponseDecoder.Decode(dispatch.ResponsePresentationPayload, 11);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.Equal(nameof(MmsReadOnlyOperation.GetNamedVariableDirectory), dispatch.Response.Operation);
        Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);
        Assert.True(names.IsSuccess, names.Message);
        Assert.Contains(names.Names, x => x.Contains("$MX$", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names.Names, x => x.Contains("$ST$", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dispatcher_Decodes_GetVariableAccessAttributes_Request()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var point = serverProfile.Points.First(x => x.Reference.EndsWith("MMXU1.PhV.phsA.cVal.mag.f", StringComparison.OrdinalIgnoreCase));
        var reference = new MmsObjectReference(point.LogicalDevice, "MMXU1$MX$PhV$phsA$cVal$mag$f", "MX");
        var request = MmsVariableAccessAttributesRequest.Build(12, reference);

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, session);
        var attributes = MmsVariableAccessAttributesResponseDecoder.Decode(dispatch.ResponsePresentationPayload, 12, reference);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.Equal(nameof(MmsReadOnlyOperation.GetVariableAccessAttributes), dispatch.Response.Operation);
        Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);
        Assert.True(attributes.IsSuccess, attributes.Message);
        Assert.Equal("floating-point", attributes.MmsType);
    }

    [Fact]
    public void Dispatcher_Decodes_DataSet_Directory_Request()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var dataSet = serverProfile.DataSets.First(x => x.Reference.EndsWith("dsStatus", StringComparison.OrdinalIgnoreCase));
        var request = MmsDataSetDirectoryRequest.Build(9, dataSet.Reference);

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, session);
        var directory = MmsDataSetDirectoryResponseDecoder.Decode(dispatch.ResponsePresentationPayload, 9, dataSet.Reference);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.Equal(nameof(MmsReadOnlyOperation.ReadDataSet), dispatch.Response.Operation);
        Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);
        Assert.True(directory.IsSuccess, directory.Message);
        Assert.True(directory.Members.Count > 0);
    }

    [Fact]
    public void Dispatcher_Rejects_Native_Ber_Write_Request_With_Decodable_Write_Failure()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var point = serverProfile.Points.First(x => x.Reference.EndsWith("XCBR1.Pos.stVal", StringComparison.OrdinalIgnoreCase));
        var request = MmsWriteRequest.BuildSingleVariableWrite(10, MmsObjectReference.Parse(point.Reference), MmsDataValue.VisibleString("open"));

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, session);
        var write = MmsWriteResponseDecoder.Decode(dispatch.ResponsePresentationPayload, 10);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.Equal(nameof(MmsReadOnlyOperation.Write), dispatch.Response.Operation);
        Assert.False(dispatch.Response.IsSuccess);
        Assert.Contains("read-only", dispatch.Response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(write.IsSuccess);
        Assert.Contains(write.AccessResults, x => !x.IsSuccess);
    }

    [Fact]
    public async Task LoopbackProbe_Dispatches_Native_Ber_Requests_And_Verifies_Write_Guard()
    {
        var profile = await new MmsConfirmedRequestBerProfileBuilder().RunLoopbackProbeAsync(new MmsConfirmedRequestBerOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        Assert.True(profile.IsReady, string.Join("; ", profile.Findings));
        Assert.True(profile.BoundPort > 0);
        Assert.Equal(1, profile.AcceptedConnectionCount);
        Assert.True(profile.TpktExchangeVerified);
        Assert.True(profile.CotpConnectionConfirmed);
        Assert.True(profile.ClientAssociateRequestObserved);
        Assert.True(profile.ClientAssociateResponseAccepted);
        Assert.True(profile.NativeBerRequestDecoded);
        Assert.True(profile.NativeBerResponseEncoded);
        Assert.True(profile.ClientNativeResponseDecoded);
        Assert.True(profile.DirectoryDispatchVerified);
        Assert.True(profile.ReadDispatchVerified);
        Assert.True(profile.DataSetDirectoryDispatchVerified);
        Assert.True(profile.WriteGuardVerified);
        Assert.True(profile.ServerSuccessCount >= 4);
        Assert.True(profile.ServerFailureCount >= 1);
        Assert.Equal(profile.RequestCount, profile.ClientDecodeSuccessCount);
    }

    [Fact]
    public async Task ToMarkdown_Renders_Ber_Dispatch_Evidence()
    {
        var profile = await new MmsConfirmedRequestBerProfileBuilder().RunLoopbackProbeAsync(new MmsConfirmedRequestBerOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        var markdown = profile.ToMarkdown();

        Assert.Contains("MMS Confirmed Request BER Dispatch Profile", markdown);
        Assert.Contains("Native BER request decoded", markdown);
        Assert.Contains("DataSet directory dispatch verified", markdown);
        Assert.Contains("Write guard verified", markdown);
    }
}
