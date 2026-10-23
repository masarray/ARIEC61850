using System.Reflection;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Control;

/// <summary>
/// Mechanical and data-contract guards for the opt-in client-side phase timer.
/// These tests do NOT simulate breaker operation or claim measured wire RTT.
/// </summary>
public sealed class P0SboServiceWireStagesTests
{
    [Fact]
    public void MmsWriteResult_ExposesOptionalPerRequestTimingsWithoutFabricatedZero()
    {
        var failed = new MmsWriteResult
        {
            IsSuccess = false,
            Message = "simulated timeout before response"
        };
        Assert.Null(failed.TpktSendElapsed);
        Assert.Null(failed.ConfirmedResponseWaitElapsed);
        Assert.Null(failed.ResponseDecodeElapsed);
    }

    [Fact]
    public void ConfirmedMmsTimer_IsPerInvokeOptInNotGlobalSessionStopwatch()
    {
        var method = typeof(MmsClientSession).GetMethod(
            "SendConfirmedPresentationPayloadAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[2].ParameterType);
        Assert.Equal(typeof(Action<TimeSpan, TimeSpan>), parameters[3].ParameterType);
        Assert.True(parameters[3].IsOptional);
    }

    [Fact]
    public void TelemetryCannotTurnOffLastApplErrorOrPositiveTermination()
    {
        var session = ReadRepoFile("src/AR.Iec61850/Control/Iec61850ControlObjectSession.cs");
        var transport = ReadRepoFile("src/AR.Iec61850/Mms/MmsClientSession.cs");
        var tcp = ReadRepoFile("src/AR.Iec61850/Osi/TpktClient.cs");

        Assert.Contains("WaitForApplicationErrorAsync(", session, StringComparison.Ordinal);
        Assert.Contains("_options.ApplicationErrorGracePeriod", session, StringComparison.Ordinal);
        Assert.Contains("WaitForTerminationAsync(reports, remaining, cancellationToken)", session,
            StringComparison.Ordinal);
        Assert.Contains("tpktSendMs=", session, StringComparison.Ordinal);
        Assert.Contains("confirmedResponseWaitMs=", session, StringComparison.Ordinal);
        Assert.Contains("mmsDecodeMs=", session, StringComparison.Ordinal);
        Assert.Contains("RegisterConfirmedOperation(expectedInvokeId)", transport,
            StringComparison.Ordinal);
        Assert.Contains("new TcpClient { NoDelay = true }", tcp, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string path)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null)
        {
            var file = Path.Combine(folder.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file))
                return File.ReadAllText(file);
            folder = folder.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
