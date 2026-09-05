namespace AR.Iec61850.Tests.Mms;

public sealed class MmsStaticReceiverFirstActivationContractTests
{
    [Fact]
    public void AttemptEvidence_StaticPlans_RouteThroughReceiverFirstActivation()
    {
        var source = Read("src/AR.Iec61850/Mms/MmsPersistentReportMonitorAttemptEvidence.cs");

        Assert.Contains("var start = isDynamic", source, StringComparison.Ordinal);
        Assert.Contains("StartPersistentReportMonitorAsync(", source, StringComparison.Ordinal);
        Assert.Contains("StartStaticPersistentReportMonitorReceiverFirstAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticActivation_RegistersReceiverBeforeRptEnaAndGi()
    {
        var source = Read("src/AR.Iec61850/Mms/MmsPersistentReportMonitor.StaticReceiverFirst.cs");

        var register = source.IndexOf("RegisterPersistentReportMonitor(monitor);", StringComparison.Ordinal);
        var enable = source.IndexOf("\"RptEna\",\n                MmsDataValue.Boolean(true)", StringComparison.Ordinal);
        var gi = source.IndexOf("\"GI\",\n                    MmsDataValue.Boolean(true)", StringComparison.Ordinal);

        Assert.True(register >= 0, "receiver registration marker missing");
        Assert.True(enable > register, "RptEna=true must occur after receiver registration");
        Assert.True(gi > enable, "GI=true must occur after RptEna=true");
        Assert.Contains("InformationReport receiver registered before RptEna/GI", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticActivation_FailedStartDisablesBeforeUnregisteringAndNeverMutatesDataSet()
    {
        var source = Read("src/AR.Iec61850/Mms/MmsPersistentReportMonitor.StaticReceiverFirst.cs");

        var cleanupStart = source.IndexOf("CleanupFailedStaticReceiverFirstStartAsync", StringComparison.Ordinal);
        var cleanupBody = source.LastIndexOf("CleanupFailedStaticReceiverFirstStartAsync", StringComparison.Ordinal);
        var disable = source.IndexOf("\"RptEna\",\n                MmsDataValue.Boolean(false)", cleanupBody, StringComparison.Ordinal);
        var unregister = source.IndexOf("UnregisterPersistentReportMonitor(monitor);", cleanupBody, StringComparison.Ordinal);

        Assert.True(cleanupStart >= 0 && cleanupBody >= cleanupStart);
        Assert.True(disable > cleanupBody, "failed-start cleanup must attempt RptEna=false");
        Assert.True(unregister > disable, "receiver must remain registered until disable cleanup is attempted");
        Assert.DoesNotContain("DefineNamedVariableList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteNamedVariableList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"DatSet\"", source, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
