using AR.Iec61850.Scl;
using AR.Iec61850.Simulation;
using AR.Iec61850.Tests.Scl;

namespace AR.Iec61850.Tests.Simulation;

public sealed class IedSimulatorProfileBuilderTests
{
    [Fact]
    public void FromScl_Builds_Logical_Devices_Nodes_And_Points()
    {
        var result = Build();
        var profile = result.Profile;

        Assert.Equal("MU01", result.SelectedIedName);
        Assert.Equal("MU01", profile.Name);
        Assert.Single(profile.LogicalDevices);

        var device = profile.LogicalDevices.Single();
        Assert.Equal("MU01LD0", device.Name);
        Assert.Equal(2, device.LogicalNodes.Count);
        Assert.Contains(device.LogicalNodes, n => n.Name == "TCTR1" && n.LnClass == "TCTR");
        Assert.Contains(device.LogicalNodes, n => n.Name == "XCBR1" && n.LnClass == "XCBR");

        // dsSV: instMag.i (+q), dsGO: stVal (+q, +t) = 5 unique points with q/t included.
        Assert.Equal(5, profile.PointCount);
    }

    [Fact]
    public void FromScl_Classifies_Measurement_Status_And_Companion_Points()
    {
        var profile = Build().Profile;
        var points = profile.LogicalDevices.SelectMany(d => d.LogicalNodes).SelectMany(n => n.Points).ToList();

        var current = points.Single(p => p.Reference == "TCTR1.Amp.instMag.i");
        Assert.Equal("measurement", current.Kind);
        Assert.Equal("A", current.Unit);
        Assert.Equal("MX", current.FunctionalConstraint);

        var breaker = points.Single(p => p.Reference == "XCBR1.Pos.stVal");
        Assert.Equal("status", breaker.Kind);
        Assert.Equal("closed", breaker.InitialValue);

        Assert.Contains(points, p => p.Reference == "XCBR1.Pos.q" && p.Kind == "quality" && p.InitialValue == "valid");
        Assert.Contains(points, p => p.Reference == "XCBR1.Pos.t" && p.Kind == "timestamp");
    }

    [Fact]
    public void FromScl_Maps_DataSets_And_Report_Control_Blocks()
    {
        var profile = Build().Profile;

        Assert.Equal(2, profile.DataSets.Count);
        Assert.All(profile.DataSets, ds => Assert.NotEmpty(ds.Members));

        var rcb = Assert.Single(profile.ReportControlBlocks);
        Assert.False(rcb.Buffered);
        Assert.Equal("URCB", rcb.Mode);
        Assert.Equal(1, rcb.ConfRev);
    }

    [Fact]
    public void FromScl_DataSet_Members_Resolve_Against_ReadOnly_Server_Model()
    {
        var profile = Build().Profile;

        var serverProfile = new MmsReadOnlyServerModelBuilder().Build(profile);

        // Every derived DataSet member must resolve to a readable point: no missing-member gaps.
        Assert.DoesNotContain(serverProfile.Diagnostics, d => d.Code == "DATASET_MEMBER_MISSING");
        Assert.True(serverProfile.IsReady);

        var session = new MmsReadOnlyServerSession(serverProfile);
        var read = session.Handle(new MmsReadOnlyServerRequest
        {
            Operation = MmsReadOnlyOperation.Read,
            Target = "MU01LD0/TCTR1.Amp.instMag.i"
        });

        Assert.True(read.IsSuccess);
        Assert.Contains(read.Values, v => v.Reference == "MU01LD0/TCTR1.Amp.instMag.i");
    }

    [Fact]
    public void FromScl_Can_Exclude_Quality_And_Timestamp_Points()
    {
        var result = new IedSimulatorProfileBuilder().FromScl(Document(), new IedSimulatorProfileFromSclOptions
        {
            IncludeQualityAndTimestampPoints = false
        });

        var points = result.Profile.LogicalDevices.SelectMany(d => d.LogicalNodes).SelectMany(n => n.Points).ToList();
        Assert.Equal(2, points.Count); // only instMag.i and Pos.stVal remain
        Assert.DoesNotContain(points, p => p.Kind == "quality" || p.Kind == "timestamp");
    }

    [Fact]
    public void FromScl_Derived_Profile_Steps_Without_Error()
    {
        var profile = Build().Profile;
        var engine = new IedSimulatorEngine(profile);
        var start = DateTimeOffset.UtcNow;

        for (var i = 0; i < 10; i++)
            engine.Step(start.AddMilliseconds(i * 20));

        var snapshot = engine.CreateSnapshot(start.AddSeconds(1));
        Assert.Equal(profile.PointCount, snapshot.Points.Count);
    }

    private static IedSimulatorProfileFromSclResult Build()
        => new IedSimulatorProfileBuilder().FromScl(Document());

    private static SclDocument Document()
        => new SclParser().Load(SclParserTests.MinimalStationPath());
}
