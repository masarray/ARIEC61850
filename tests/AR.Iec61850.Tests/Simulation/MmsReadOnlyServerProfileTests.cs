using AR.Iec61850.Simulation;

namespace AR.Iec61850.Tests.Simulation;

public sealed class MmsReadOnlyServerProfileTests
{
    [Fact]
    public void Builder_Creates_ReadOnly_Server_Profile_From_Default_Simulator()
    {
        var simulator = IedSimulatorProfile.CreateDefaultFeederProfile();
        var profile = new MmsReadOnlyServerModelBuilder().Build(simulator);

        Assert.True(profile.IsReady);
        Assert.Equal(1, profile.LogicalDeviceCount);
        Assert.Equal(5, profile.LogicalNodeCount);
        Assert.Equal(11, profile.PointCount);
        Assert.Equal(2, profile.DataSetCount);
        Assert.Equal(2, profile.ReportControlBlockCount);
        Assert.Contains(profile.Points, x => x.Reference == "IED1LD0/XCBR1.Pos.stVal" && x.Value == "closed");
        Assert.Contains(profile.DataSets, x => x.Reference == "IED1LD0/LLN0.dsStatus" && x.Members.Count == 5);
    }

    [Fact]
    public void Session_Reads_Point_And_DataSet()
    {
        var profile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(profile);

        var point = session.Handle(new MmsReadOnlyServerRequest
        {
            Operation = MmsReadOnlyOperation.Read,
            Target = "IED1LD0/XCBR1.Pos.stVal"
        });
        var dataSet = session.Handle(new MmsReadOnlyServerRequest
        {
            Operation = MmsReadOnlyOperation.ReadDataSet,
            Target = "IED1LD0/LLN0.dsStatus"
        });

        Assert.True(point.IsSuccess);
        Assert.Single(point.Values);
        Assert.Equal("closed", point.Values[0].Value);
        Assert.True(dataSet.IsSuccess);
        Assert.Equal(5, dataSet.Values.Count);
    }

    [Fact]
    public void Session_Rejects_Write_In_ReadOnly_Mode()
    {
        var profile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var session = new MmsReadOnlyServerSession(profile);

        var response = session.Handle(new MmsReadOnlyServerRequest
        {
            Operation = MmsReadOnlyOperation.Write,
            Target = "IED1LD0/XCBR1.Pos.stVal",
            Value = "open"
        });

        Assert.False(response.IsSuccess);
        Assert.Contains("read-only", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Builder_Reports_Missing_DataSet_Members()
    {
        var baseProfile = IedSimulatorProfile.CreateDefaultFeederProfile();
        var broken = baseProfile with
        {
            DataSets = new[]
            {
                new IedSimulatorDataSet
                {
                    Reference = "IED1LD0/LLN0.dsBroken",
                    Members = new[] { "IED1LD0/MISSING1.Pos.stVal" }
                }
            }
        };

        var profile = new MmsReadOnlyServerModelBuilder().Build(broken);

        Assert.False(profile.IsReady);
        Assert.Contains(profile.Diagnostics, x => x.Code == "DATASET_MEMBER_MISSING");
        Assert.Contains(profile.DataSets, x => x.Status.StartsWith("Missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Markdown_Contains_Server_Evidence()
    {
        var profile = new MmsReadOnlyServerModelBuilder().Build(IedSimulatorProfile.CreateDefaultFeederProfile());
        var markdown = profile.ToMarkdown();

        Assert.Contains("# MMS Read-Only Server Profile", markdown);
        Assert.Contains("read-only virtual IED model", markdown);
        Assert.Contains("IED1LD0/LLN0.dsStatus", markdown);
        Assert.Contains("Write", markdown);
    }
}
