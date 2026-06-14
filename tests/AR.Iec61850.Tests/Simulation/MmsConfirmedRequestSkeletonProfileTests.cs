using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class MmsConfirmedRequestSkeletonProfileTests
{
    [Fact]
    public void EnvelopeCodec_RoundTrips_Request_And_Response()
    {
        var request = new MmsReadOnlyServerRequest
        {
            Operation = MmsReadOnlyOperation.Read,
            Target = "IED1LD0/XCBR1.Pos.stVal"
        };

        var requestBytes = MmsConfirmedRequestEnvelopeCodec.EncodeRequest(request);
        Assert.True(MmsConfirmedRequestEnvelopeCodec.TryDecodeRequest(requestBytes, out var decodedRequest, out var requestMessage), requestMessage);
        Assert.Equal(MmsReadOnlyOperation.Read, decodedRequest.Operation);
        Assert.Equal(request.Target, decodedRequest.Target);

        var response = new MmsReadOnlyServerResponse
        {
            IsSuccess = true,
            Operation = nameof(MmsReadOnlyOperation.Read),
            Target = request.Target,
            Message = "Returned 1 value."
        };

        var responseBytes = MmsConfirmedRequestEnvelopeCodec.EncodeResponse(response);
        Assert.True(MmsConfirmedRequestEnvelopeCodec.TryDecodeResponse(responseBytes, out var decodedResponse, out var responseMessage), responseMessage);
        Assert.True(decodedResponse.IsSuccess);
        Assert.Equal(response.Target, decodedResponse.Target);
    }

    [Fact]
    public async Task LoopbackProbe_Dispatches_ReadOnly_Confirmed_Requests()
    {
        var profile = await new MmsConfirmedRequestSkeletonProfileBuilder().RunLoopbackProbeAsync(new MmsConfirmedRequestSkeletonOptions
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
        Assert.True(profile.ServerAssociateResponseSent);
        Assert.True(profile.ClientAssociateResponseAccepted);
        Assert.True(profile.ConfirmedRequestObserved);
        Assert.True(profile.ConfirmedResponseSent);
        Assert.True(profile.ConfirmedResponseAccepted);
        Assert.True(profile.ReadOnlyDispatchVerified);
        Assert.True(profile.WriteGuardVerified);
        Assert.True(profile.SuccessfulResponseCount >= 4);
        Assert.True(profile.FailedResponseCount >= 1);
        Assert.Contains(profile.ProbeResults, x => x.Operation == nameof(MmsReadOnlyOperation.Read) && x.IsServerSuccess);
        Assert.Contains(profile.ProbeResults, x => x.Operation == nameof(MmsReadOnlyOperation.ReadDataSet) && x.IsServerSuccess);
        Assert.Contains(profile.ProbeResults, x => x.Operation == nameof(MmsReadOnlyOperation.Write) && !x.IsServerSuccess);
    }

    [Fact]
    public async Task LoopbackProbe_Reports_Invalid_Target_As_Response_Failure_But_Transport_Success()
    {
        var requests = new[]
        {
            new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Read, Target = "IED1LD0/MISSING.stVal" },
            new MmsReadOnlyServerRequest { Operation = MmsReadOnlyOperation.Write, Target = "IED1LD0/XCBR1.Pos.stVal", Value = "open" }
        };

        var profile = await new MmsConfirmedRequestSkeletonProfileBuilder().RunLoopbackProbeAsync(new MmsConfirmedRequestSkeletonOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        }, requests);

        Assert.False(profile.IsReady);
        Assert.Equal(2, profile.RequestCount);
        Assert.Equal(0, profile.SuccessfulResponseCount);
        Assert.Equal(2, profile.FailedResponseCount);
        Assert.True(profile.WriteGuardVerified);
        Assert.Contains(profile.ProbeResults, x => x.Operation == nameof(MmsReadOnlyOperation.Read) && x.IsTransportSuccess && !x.IsServerSuccess);
    }

    [Fact]
    public async Task ToMarkdown_Renders_Confirmed_Request_Gates()
    {
        var profile = await new MmsConfirmedRequestSkeletonProfileBuilder().RunLoopbackProbeAsync(new MmsConfirmedRequestSkeletonOptions
        {
            Port = 0,
            ProbeTimeoutMilliseconds = 5000
        });

        var markdown = profile.ToMarkdown();

        Assert.Contains("MMS Confirmed Request Skeleton Profile", markdown);
        Assert.Contains("Confirmed request observed", markdown);
        Assert.Contains("Read-only dispatch verified", markdown);
        Assert.Contains("Write guard verified", markdown);
        Assert.Contains("ReadDataSet", markdown);
    }
}
