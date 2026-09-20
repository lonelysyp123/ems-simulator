using EssSimulator.EssDeviceSimModel;

namespace EssSimulator.Tests.Plant;

public class SimulationClockTests
{
    [Fact]
    public void Tick_AdvancesSimTimeByFixedDt()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var dt = TimeSpan.FromMilliseconds(100);
        var clock = new SimulationClock(dt, integrationMultiplier: 1.0, startUtc: start);

        var first = clock.Tick();
        var second = clock.Tick();

        Assert.Equal(start + dt, first.SimTime);
        Assert.Equal(start + dt + dt, second.SimTime);
        Assert.Equal(dt, first.Dt);
        Assert.Equal(dt, second.Dt);
        Assert.Equal(2, clock.TickCount);
    }

    [Fact]
    public void Tick_DtDoesNotDependOnWallClock()
    {
        var clock = new SimulationClock(TimeSpan.FromMilliseconds(50), startUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var before = DateTime.UtcNow;
        var tick = clock.Tick();
        var wall = DateTime.UtcNow - before;

        Assert.Equal(TimeSpan.FromMilliseconds(50), tick.Dt);
        Assert.True(wall < TimeSpan.FromSeconds(1));
        Assert.NotEqual(tick.SimTime, DateTime.UtcNow);
    }

    [Fact]
    public void IntegrationDt_AppliesMultiplier()
    {
        var clock = new SimulationClock(TimeSpan.FromMilliseconds(100), integrationMultiplier: 2.0);
        var tick = clock.Tick();
        Assert.Equal(TimeSpan.FromMilliseconds(200), tick.IntegrationDt);
        Assert.Equal(TimeSpan.FromMilliseconds(100), tick.Dt);
    }

    [Fact]
    public void Ctor_RejectsNonPositiveDt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClock(TimeSpan.Zero));
    }
}
