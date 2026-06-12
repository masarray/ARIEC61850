using AR.Iec61850.Goose;

namespace AR.Iec61850.Tests.Goose;

public sealed class GooseRetransmissionScheduleTests
{
    [Fact]
    public void NextDelay_Doubles_Until_MaxTime()
    {
        var schedule = new GooseRetransmissionSchedule(minTimeMilliseconds: 4, maxTimeMilliseconds: 1000);

        Assert.Equal(4, schedule.NextDelayMilliseconds());
        Assert.Equal(8, schedule.NextDelayMilliseconds());
        Assert.Equal(16, schedule.NextDelayMilliseconds());
        Assert.Equal(32, schedule.NextDelayMilliseconds());
        Assert.Equal(64, schedule.NextDelayMilliseconds());
        Assert.Equal(128, schedule.NextDelayMilliseconds());
        Assert.Equal(256, schedule.NextDelayMilliseconds());
        Assert.Equal(512, schedule.NextDelayMilliseconds());
        Assert.Equal(1000, schedule.NextDelayMilliseconds());
        Assert.Equal(1000, schedule.NextDelayMilliseconds());
    }

    [Fact]
    public void Reset_Restarts_At_MinTime()
    {
        var schedule = new GooseRetransmissionSchedule(minTimeMilliseconds: 5, maxTimeMilliseconds: 20);

        Assert.Equal(5, schedule.NextDelayMilliseconds());
        Assert.Equal(10, schedule.NextDelayMilliseconds());
        Assert.Equal(20, schedule.NextDelayMilliseconds());

        schedule.Reset();

        Assert.Equal(5, schedule.NextDelayMilliseconds());
    }

    [Fact]
    public void Defaults_Are_Used_When_Scl_Times_Are_Missing()
    {
        var schedule = new GooseRetransmissionSchedule(minTimeMilliseconds: 0, maxTimeMilliseconds: 0);

        Assert.Equal(4, schedule.MinTimeMilliseconds);
        Assert.Equal(1000, schedule.MaxTimeMilliseconds);
    }
}
