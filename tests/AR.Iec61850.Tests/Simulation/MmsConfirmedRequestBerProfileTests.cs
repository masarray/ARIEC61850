using AR.Iec61850.Asn1;
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
    public void Dispatcher_Uses_Negotiated_Presentation_Context_Id()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var point = serverProfile.Points.First(x => x.Reference.EndsWith("XCBR1.Pos.stVal", StringComparison.OrdinalIgnoreCase));
        var request = MmsReadRequest.BuildSingleVariableRead(17, MmsObjectReference.Parse(point.Reference));

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, session, presentationContextId: 5);

        Assert.Equal((ulong)5, ReadPresentationContextId(dispatch.ResponsePresentationPayload));
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
        Assert.Contains("MMXU1", names.Names);
        Assert.Contains("MMXU1$MX", names.Names);
        Assert.Contains(names.Names, x => x.Contains("$MX$", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names.Names, x => x.Contains("$ST$", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dispatcher_Paginates_Large_NamedVariable_Directory_And_Honors_ContinueAfter()
    {
        var points = Enumerable.Range(0, 130)
            .Select(index => IedSimulatorPoint.Status($"MMXU1.S{index}.stVal", "ST", "false"))
            .ToArray();
        var simulatorProfile = new IedSimulatorProfile
        {
            Name = "Paged IED",
            LogicalDevices =
            [
                new IedSimulatorLogicalDevice
                {
                    Name = "IED1LD0",
                    LogicalNodes =
                    [
                        new IedSimulatorLogicalNode
                        {
                            Name = "MMXU1",
                            LnClass = "MMXU",
                            Points = points
                        }
                    ]
                }
            ]
        };
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(simulatorProfile);
        var session = new MmsReadOnlyServerSession(serverProfile);
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var continueAfter = string.Empty;
        var pageCount = 0;

        do
        {
            var request = MmsGetNameListRequest.Build(
                30 + pageCount,
                MmsGetNameListObjectClass.NamedVariable,
                "IED1LD0",
                continueAfter);
            var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, session);
            var page = MmsGetNameListResponseDecoder.Decode(dispatch.ResponsePresentationPayload, 30 + pageCount);

            Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
            Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);
            Assert.True(page.IsSuccess, page.Message);
            Assert.NotEmpty(page.Names);
            Assert.DoesNotContain(page.Names, name => name.Contains('/', StringComparison.Ordinal));
            Assert.All(page.Names, name => Assert.True(discovered.Add(name), $"Duplicate directory item: {name}"));

            continueAfter = page.Names[^1];
            pageCount++;
            if (!page.MoreFollows)
                break;
        }
        while (pageCount < 8);

        Assert.True(pageCount > 1, $"Expected multiple pages, received {pageCount}.");
        Assert.Contains("MMXU1", discovered);
        Assert.Contains("MMXU1$ST", discovered);
        Assert.Contains("MMXU1$ST$S129$stVal", discovered);
    }

    [Fact]
    public void Directory_Normalizes_Nested_Domain_Prefixes_Before_Paging()
    {
        var simulatorProfile = IedSimulatorProfile.CreateDefaultFeederProfile();
        var baseProfile = new MmsReadOnlyServerModelBuilder().Build(simulatorProfile);
        var nestedProfile = baseProfile with
        {
            Points = baseProfile.Points
                .Select(point => point with { Reference = $"{point.LogicalDevice}/{point.Reference}" })
                .ToArray()
        };
        var session = new MmsReadOnlyServerSession(nestedProfile);

        var response = session.Handle(new MmsReadOnlyServerRequest
        {
            Operation = MmsReadOnlyOperation.GetNamedVariableDirectory,
            Target = "IED1LD0"
        });

        Assert.True(response.IsSuccess, response.Message);
        Assert.DoesNotContain(response.Items, item => item.Contains('/', StringComparison.Ordinal));
        Assert.Contains("MMXU1", response.Items);
        Assert.Contains("MMXU1$MX", response.Items);
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
    public void Dispatcher_Accepts_Known_Hierarchy_Variable_Access_Attributes()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var reference = new MmsObjectReference("IED1LD0", "MMXU1$MX", "MX");
        var request = MmsVariableAccessAttributesRequest.Build(13, reference);

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, session);
        var attributes = MmsVariableAccessAttributesResponseDecoder.Decode(dispatch.ResponsePresentationPayload, 13, reference);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);
        Assert.True(attributes.IsSuccess, attributes.Message);
        Assert.Equal("structure", attributes.MmsType);
        Assert.Contains(attributes.TypeSpecification!.Children, x => x.Name == "A");
        Assert.Contains(attributes.TypeSpecification.Children, x => x.Name == "PhV");
    }

    [Fact]
    public void Dispatcher_Exposes_Report_Control_Block_Hierarchy_In_Named_Variable_Directory()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var directoryRequest = MmsGetNameListRequest.Build(15, MmsGetNameListObjectClass.NamedVariable, "IED1LD0");

        var directoryDispatch = MmsConfirmedRequestBerDispatcher.Dispatch(directoryRequest, session);
        var names = MmsGetNameListResponseDecoder.Decode(directoryDispatch.ResponsePresentationPayload, 15);

        Assert.True(directoryDispatch.Response.IsSuccess, directoryDispatch.Response.Message);
        Assert.True(names.IsSuccess, names.Message);
        Assert.Contains("LLN0$RP", names.Names);
        Assert.Contains("LLN0$RP$rptStatus01", names.Names);

        var reference = new MmsObjectReference("IED1LD0", "LLN0$RP$rptStatus01", "RP");
        var attributesRequest = MmsVariableAccessAttributesRequest.Build(16, reference);
        var attributesDispatch = MmsConfirmedRequestBerDispatcher.Dispatch(attributesRequest, session);
        var attributes = MmsVariableAccessAttributesResponseDecoder.Decode(attributesDispatch.ResponsePresentationPayload, 16, reference);

        Assert.True(attributesDispatch.Response.IsSuccess, attributesDispatch.Response.Message);
        Assert.True(attributes.IsSuccess, attributes.Message);
        Assert.Equal("structure", attributes.MmsType);
        Assert.Contains(attributes.TypeSpecification!.Children, child => child.Name == "RptID");
        Assert.Contains(attributes.TypeSpecification.Children, child => child.Name == "RptEna");

        var dataSetReference = new MmsObjectReference("IED1LD0", "LLN0$RP$rptStatus01$DatSet", "RP");
        var dataSetAttributesRequest = MmsVariableAccessAttributesRequest.Build(17, dataSetReference);
        var dataSetAttributesDispatch = MmsConfirmedRequestBerDispatcher.Dispatch(dataSetAttributesRequest, session);
        var dataSetAttributes = MmsVariableAccessAttributesResponseDecoder.Decode(
            dataSetAttributesDispatch.ResponsePresentationPayload,
            17,
            dataSetReference);

        Assert.True(dataSetAttributesDispatch.Response.IsSuccess, dataSetAttributesDispatch.Response.Message);
        Assert.True(dataSetAttributes.IsSuccess, dataSetAttributes.Message);
        Assert.Equal("visible-string", dataSetAttributes.MmsType);

        var dataSetRead = MmsReadRequest.BuildSingleVariableRead(18, dataSetReference);
        var dataSetReadDispatch = MmsConfirmedRequestBerDispatcher.Dispatch(dataSetRead, session);
        var dataSetReadResult = MmsReadResponseDecoder.DecodeSingleVariable(dataSetReadDispatch.ResponsePresentationPayload, 18);

        Assert.True(dataSetReadDispatch.Response.IsSuccess, dataSetReadDispatch.Response.Message);
        Assert.True(dataSetReadResult.IsSuccess, dataSetReadResult.Message);
        Assert.Equal(MmsDataKind.VisibleString, dataSetReadResult.Value!.Kind);
    }

    [Fact]
    public void Dispatcher_Accepts_VmdSpecific_Variable_Access_Attributes_Name()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var objectName = BerWriter.EncodeTlv(0x82, BerWriter.EncodeAscii("A50PTOC1"));
        var service = BerWriter.EncodeTlv(0xA6, objectName);
        var invoke = BerWriter.EncodeTlv(0x02, BerWriter.EncodeUnsignedInteger(14));
        var request = BerWriter.EncodeTlv(0xA0, invoke.Concat(service).ToArray());

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(
            MmsPresentation.WrapIsoPresentationPData(request),
            session);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.Equal(nameof(MmsReadOnlyOperation.GetVariableAccessAttributes), dispatch.Response.Operation);
        Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);
    }

    [Fact]
    public void Dispatcher_Accepts_FileDirectory_Request_And_Returns_Empty_ReadOnly_Directory()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var fileName = BerWriter.EncodeTlv(0x19, ReadOnlySpan<byte>.Empty);
        var fileDirectoryRequest = BerWriter.EncodeTlv(
            BerClass.ContextSpecific,
            true,
            77,
            BerWriter.EncodeTlv(0xA0, fileName));
        var confirmedRequest = BerWriter.EncodeTlv(0xA0,
            BerWriter.EncodeTlv(0x02, BerWriter.EncodeUnsignedInteger(19))
                .Concat(fileDirectoryRequest)
                .ToArray());

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(
            MmsPresentation.WrapIsoPresentationPData(confirmedRequest),
            session);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.Equal(MmsReadOnlyOperation.GetFileDirectory, dispatch.Request.Operation);
        Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);

        var mms = MmsPresentation.StripPresentationPrefix(dispatch.ResponsePresentationPayload);
        var offset = 0;
        Assert.True(BerReader.TryReadTlv(mms, ref offset, out var response));
        var service = BerReader.ReadChildren(response.Value).Single(x => x.Class == BerClass.ContextSpecific && x.TagNumber == 77);
        var fields = BerReader.ReadChildren(service.Value);
        Assert.Contains(fields, field => field.Class == BerClass.ContextSpecific && field.TagNumber == 0);
        Assert.Contains(fields, field => field.Class == BerClass.ContextSpecific && field.TagNumber == 1);
    }

    [Fact]
    public void Dispatcher_Reads_FunctionalConstraint_Root_As_Structure_And_Normalizes_DataSet_Name()
    {
        var simulatorProfile = IedSimulatorProfile.CreateDefaultFeederProfile();
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(simulatorProfile);
        var session = new MmsReadOnlyServerSession(serverProfile);

        var rootReference = new MmsObjectReference("IED1LD0", "MMXU1$MX", "MX");
        var rootRequest = MmsReadRequest.BuildSingleVariableRead(20, rootReference);
        var rootDispatch = MmsConfirmedRequestBerDispatcher.Dispatch(rootRequest, session);
        var rootRead = MmsReadResponseDecoder.DecodeSingleVariable(rootDispatch.ResponsePresentationPayload, 20);

        Assert.True(rootDispatch.Response.IsSuccess, rootDispatch.Response.Message);
        Assert.True(rootRead.IsSuccess, rootRead.Message);
        Assert.Equal(MmsDataKind.Structure, rootRead.Value!.Kind);
        Assert.NotEmpty(rootRead.Value.Children);

        var dataSetRead = session.Handle(new MmsReadOnlyServerRequest
        {
            Operation = MmsReadOnlyOperation.ReadDataSet,
            Target = "IED1LD0/LLN0.ds_Meas"
        });

        Assert.True(dataSetRead.IsSuccess, dataSetRead.Message);
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
    public void Dispatcher_Accepts_IedScout_VmdSpecific_DataSet_Request_And_Encodes_Standard_Response()
    {
        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(serverProfile);
        var dataSet = serverProfile.DataSets.First(x => x.Reference.EndsWith("dsMeas", StringComparison.OrdinalIgnoreCase));

        // Captured IEDScout form: GetNamedVariableListAttributes [12] with a
        // VMD-specific ObjectName (0x80) carrying LLN0$dsMeas.
        var vmdSpecificName = BerWriter.EncodeTlv(0x80, BerWriter.EncodeAscii("LLN0$dsMeas"));
        var service = BerWriter.EncodeTlv(0xAC, vmdSpecificName);
        var confirmedRequest = BerWriter.EncodeTlv(0xA0,
            BerWriter.EncodeTlv(0x02, BerWriter.EncodeUnsignedInteger(7))
                .Concat(service)
                .ToArray());
        var request = MmsPresentation.WrapIsoPresentationPData(confirmedRequest);

        var dispatch = MmsConfirmedRequestBerDispatcher.Dispatch(request, session);
        var directory = MmsDataSetDirectoryResponseDecoder.Decode(dispatch.ResponsePresentationPayload, 7, dataSet.Reference);

        Assert.True(dispatch.IsRequestDecoded, dispatch.Message);
        Assert.True(dispatch.Response.IsSuccess, dispatch.Response.Message);
        Assert.Equal(dataSet.Reference, dispatch.Response.Target);
        Assert.True(directory.IsSuccess, directory.Message);
        Assert.NotEmpty(directory.Members);

        var mms = MmsPresentation.StripPresentationPrefix(dispatch.ResponsePresentationPayload);
        var offset = 0;
        Assert.True(BerReader.TryReadTlv(mms, ref offset, out var response));
        var responseChildren = BerReader.ReadChildren(response.Value);
        var responseService = Assert.Single(responseChildren.Where(x => x.EncodedTag == 0xAC));
        var listOfVariable = Assert.Single(BerReader.ReadChildren(responseService.Value).Where(x => x.EncodedTag == 0xA1));
        var variableDefinitions = BerReader.ReadChildren(listOfVariable.Value).Where(x => x.EncodedTag == 0x30).ToArray();
        Assert.NotEmpty(variableDefinitions);
        var variableDefinition = variableDefinitions[0];
        var variableSpecification = Assert.Single(BerReader.ReadChildren(variableDefinition.Value).Where(x => x.EncodedTag == 0xA0));
        Assert.Contains(BerReader.ReadChildren(variableSpecification.Value), x => x.EncodedTag == 0xA1);
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

    private static ulong ReadPresentationContextId(byte[] presentationPayload)
    {
        var offset = 4;
        Assert.True(BerReader.TryReadTlv(presentationPayload, ref offset, out var fullyEncodedData));
        Assert.Equal(0x61, fullyEncodedData.EncodedTag);
        var pdvList = BerReader.ReadChildren(fullyEncodedData.Value).Single(x => x.EncodedTag == 0x30);
        var contextId = BerReader.ReadChildren(pdvList.Value).Single(x => x.EncodedTag == 0x02);
        return BerReader.ReadUnsignedInteger(contextId) ?? 0;
    }
}
