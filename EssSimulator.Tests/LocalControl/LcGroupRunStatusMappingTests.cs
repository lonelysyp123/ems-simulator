using EssSimulator.LocalControl;
using log4net;

namespace EssSimulator.Tests.LocalControl;

/// <summary>
/// group/lc.csv 的 pcs 运行状态应对齐 emu yc44（1停机 2待机 4充电 5放电 6故障），
/// 不能用黑启动直流通道的 0关机/1运行/2待机。
/// </summary>
public class LcGroupRunStatusMappingTests
{
    private sealed class Probe : StandardLcRuntime
    {
        public Probe() : base(LogManager.GetLogger(typeof(Probe))) { }

        public object Map(object emuStatus, bool isOn, double p, double q) =>
            MapRunStatus(emuStatus, isOn, p, q);
    }

    [Fact]
    public void StoppedPcs_PassesThroughEmuYc44OffCode()
    {
        var mapped = new Probe().Map(emuStatus: 1, isOn: false, p: 0, q: 0);
        Assert.Equal(1, Convert.ToInt32(mapped));
        Assert.NotEqual(DcChannelRunStatus.Off, Convert.ToInt32(mapped));
    }

    [Fact]
    public void DischargingPcs_PassesThroughEmuYc44NotDcChannelRunning()
    {
        var mapped = new Probe().Map(emuStatus: 5, isOn: true, p: 100, q: 0);
        Assert.Equal(5, Convert.ToInt32(mapped));
        Assert.NotEqual(DcChannelRunStatus.Running, Convert.ToInt32(mapped));
    }

    [Fact]
    public void StandbyPcs_MatchesEmuYc44Standby()
    {
        var mapped = new Probe().Map(emuStatus: 2, isOn: true, p: 0, q: 0);
        Assert.Equal(2, Convert.ToInt32(mapped));
    }
}
