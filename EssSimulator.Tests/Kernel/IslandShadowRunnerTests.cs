using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;

namespace EssSimulator.Tests.Kernel;

public class IslandShadowRunnerTests
{
    private static readonly Complex Zout = MinPcsPort.DefaultOutputImpedanceOhm;

    private static IslandShadowSnapshot Snap(params FormingSourceSample[] sources) =>
        new("bus690", sources);

    [Fact]
    public void Tick_SettlesDc_PerSource_FromCommittedAcSolution()
    {
        var snap = Snap(
            new FormingSourceSample("a", 700, Zout, dcVoltageV: 1200, efficiency: 0.98),
            new FormingSourceSample("b", 650, Zout, dcVoltageV: 1100, efficiency: 0.98));
        var runner = new IslandShadowRunner(snap);

        var tick = runner.Tick();

        Assert.Equal(2, runner.Converters.Count);
        var da = tick.DcSettlements[0];
        var db = tick.DcSettlements[1];
        Assert.NotNull(da);
        Assert.NotNull(db);

        Assert.True(da!.Value.PacIntoDeviceW < 0, "高电势源向交流网送电");
        Assert.True(da.Value.PdcIntoDeviceW > 0, "放电时直流口电流流入变流器");
        Assert.True(db!.Value.PacIntoDeviceW > 0, "低电势源从交流网吸收");
        Assert.True(db.Value.PdcIntoDeviceW < 0, "充电时直流口电流流出变流器");

        Assert.Equal(da.Value.PdcIntoDeviceW / 1200, da.Value.IdcIntoDeviceA, 9);
        Assert.Equal(db.Value.PdcIntoDeviceW / 1100, db.Value.IdcIntoDeviceA, 9);

        Assert.True(Math.Abs(da.Value.PdcIntoDeviceW + da.Value.PacIntoDeviceW - da.Value.LossW) < 1e-6);
        Assert.True(Math.Abs(db.Value.PdcIntoDeviceW + db.Value.PacIntoDeviceW - db.Value.LossW) < 1e-6);
        Assert.True(Math.Abs(da.Value.PdcIntoDeviceW * 0.98 + da.Value.PacIntoDeviceW) < 1e-3);
    }

    [Fact]
    public void Tick_NoDcSide_LeavesSettlementNull()
    {
        var snap = Snap(
            new FormingSourceSample("a", 700, Zout),
            new FormingSourceSample("b", 650, Zout));
        var runner = new IslandShadowRunner(snap);

        var tick = runner.Tick();

        Assert.Null(tick.DcSettlements[0]);
        Assert.Null(tick.DcSettlements[1]);
    }

    [Fact]
    public void SyncFormingReferences_UpdatesCommittedDcVoltage()
    {
        var snap = Snap(
            new FormingSourceSample("a", 700, Zout, dcVoltageV: 1200),
            new FormingSourceSample("b", 650, Zout, dcVoltageV: 1100));
        var runner = new IslandShadowRunner(snap);
        runner.Tick();

        var next = Snap(
            new FormingSourceSample("a", 700, Zout, dcVoltageV: 1150),
            new FormingSourceSample("b", 650, Zout, dcVoltageV: 1100));
        runner.SyncFormingReferences(next);
        var tick = runner.Tick();

        Assert.Equal(1150, runner.Converters[0].DcVoltageV);
        var da = tick.DcSettlements[0]!.Value;
        Assert.Equal(da.PdcIntoDeviceW / 1150, da.IdcIntoDeviceA, 9);
    }

    private static IslandShadowSnapshot GridSnap(params FollowingSourceSample[] following) =>
        new("bus690",
            Array.Empty<FormingSourceSample>(),
            referredGridEmf: 690,
            gridZ: new Complex(0, 0.5),
            theveninZ: new Complex(0, 0.1),
            following: following);

    [Fact]
    public void Tick_SettlesFollowingDc_FromGridSolvedPower()
    {
        var runner = new IslandShadowRunner(
            GridSnap(new FollowingSourceSample("gfl1", 100_000, 0, dcVoltageV: 1200, efficiency: 0.98)));

        var tick = runner.Tick();

        Assert.NotNull(runner.Handshake);
        var d = tick.FollowingDcSettlements[0];
        Assert.NotNull(d);
        Assert.Equal(-100_000, d!.Value.PacIntoDeviceW, 1);
        Assert.True(d.Value.PdcIntoDeviceW > 0, "并网送电时直流口放电");
        Assert.Equal(d.Value.PdcIntoDeviceW / 1200, d.Value.IdcIntoDeviceA, 9);
        Assert.True(Math.Abs(d.Value.PdcIntoDeviceW + d.Value.PacIntoDeviceW - d.Value.LossW) < 1e-6);
        Assert.True(Math.Abs(d.Value.PdcIntoDeviceW * 0.98 + d.Value.PacIntoDeviceW) < 1e-1);
    }

    [Fact]
    public void Tick_FollowingNoDcSide_NullSettlement()
    {
        var runner = new IslandShadowRunner(GridSnap(new FollowingSourceSample("gfl1", 100_000)));

        var tick = runner.Tick();

        Assert.Null(tick.FollowingDcSettlements[0]);
    }

    [Fact]
    public void Tick_FollowingCurrentLimit_SettlesFromClampedNotSetpoint()
    {
        var runner = new IslandShadowRunner(
            GridSnap(new FollowingSourceSample("gfl1", 1_000_000_000, 0, dcVoltageV: 1200, efficiency: 0.98, currentLimitA: 100)));

        var tick = runner.Tick();
        var d = tick.FollowingDcSettlements[0]!.Value;

        Assert.Equal(100, tick.Island.GflCurrents[0].Magnitude, 6);
        Assert.True(-d.PacIntoDeviceW > 0 && -d.PacIntoDeviceW < 1_000_000_000, "限流后结算功率远低于设定");
        Assert.True(Math.Abs(d.PdcIntoDeviceW + d.PacIntoDeviceW - d.LossW) < 1e-6);
    }

    [Fact]
    public void SyncFormingReferences_UpdatesFollowingSetpoint()
    {
        var runner = new IslandShadowRunner(GridSnap(new FollowingSourceSample("gfl1", 50_000, 0, dcVoltageV: 1200)));
        runner.Tick();

        runner.SyncFormingReferences(GridSnap(new FollowingSourceSample("gfl1", 80_000, 0, dcVoltageV: 1200)));
        var tick = runner.Tick();

        Assert.Equal(80_000, runner.Island.GflPorts[0].ActivePowerW);
        Assert.Equal(-80_000, tick.FollowingDcSettlements[0]!.Value.PacIntoDeviceW, 1);
    }
}
