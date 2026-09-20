using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;

namespace EssSimulator.Tests.Kernel;

/// <summary>
/// 阶段 3：限流是本拍离散模式事件（回步骤 4 重装配再解），不是求解失败、不进同一次线性求解。
/// 判据 |I_want|=(E−Vbus)/Z 超限 → 该 PCS 由「E 串 Z」切成「注入钳位电流」。
/// </summary>
public class IslandCurrentLimitTests
{
    private static readonly Complex Zout = MinPcsPort.DefaultOutputImpedanceOhm;

    [Fact]
    public void OverLimit_ClampedToLimitAndKclHolds()
    {
        var island = new IsolatedAcIsland("bus690");
        var pcs = new MinPcsPort("pcs1", "bus690", Zout)
        {
            InternalVoltageV = 690,
            CurrentLimitA = 20
        };
        island.AddPcs(pcs);
        island.SetLoadImpedance(new Complex(20, 5));

        var r = island.Solve();

        Assert.True(r.PcsCurrentLimited[0], "自然电流约 33A，超过 20A 限幅应进入限流模式");
        Assert.Equal(20.0, r.PcsCurrents[0].Magnitude, 6);

        Complex vbus = r.Solution.Voltage("bus690");
        Complex voltageModeCurrent = (690 - vbus) / Zout;
        Assert.True(voltageModeCurrent.Magnitude > 20.0,
            "若仍按电压源模式本应推更大电流，证明限流确实改了贴片而非沿用 E 串 Z");

        Assert.True((r.PcsCurrentSum - r.LoadCurrent).Magnitude < 1e-6, "限流后母线 KCL 仍成立");
        Assert.True(vbus.Magnitude > 0 && vbus.Magnitude < 690);
    }

    [Fact]
    public void LimitNotExceeded_StaysVoltageMode()
    {
        var island = new IsolatedAcIsland("bus690");
        var pcs = new MinPcsPort("pcs1", "bus690", Zout)
        {
            InternalVoltageV = 690,
            CurrentLimitA = 1000
        };
        island.AddPcs(pcs);
        island.SetLoadImpedance(new Complex(20, 5));

        var r = island.Solve();

        Assert.False(r.PcsCurrentLimited[0], "限幅远高于自然电流，不应进入限流");
        Complex vbus = r.Solution.Voltage("bus690");
        Complex expected = (690 - vbus) / Zout;
        Assert.True((r.PcsCurrents[0] - expected).Magnitude < 1e-9, "未限流时电流就是 E 串 Z 的支路电流");
        Assert.True(r.PcsCurrents[0].Magnitude < 1000);
    }

    [Fact]
    public void NoLimitSet_AllFalse_MatchesPureVoltageSolve()
    {
        var island = new IsolatedAcIsland("bus690");
        island.AddPcs(new MinPcsPort("pcs1", "bus690", Zout) { InternalVoltageV = 690 });
        island.AddPcs(new MinPcsPort("pcs2", "bus690", Zout) { InternalVoltageV = 655 });
        island.SetLoadImpedance(new Complex(20, 5));

        var r = island.Solve();

        Assert.All(r.PcsCurrentLimited, limited => Assert.False(limited));
        Assert.True((r.PcsCurrentSum - r.LoadCurrent).Magnitude < 1e-6, "无限幅时退化为阶段 2 单次线性求解");
    }

    [Fact]
    public void TwoParallel_OnlyOverLimitOneClamped_OtherStaysVoltage()
    {
        var island = new IsolatedAcIsland("bus690");
        var a = new MinPcsPort("pcs1", "bus690", Zout) { InternalVoltageV = 690, CurrentLimitA = 8 };
        var b = new MinPcsPort("pcs2", "bus690", Zout) { InternalVoltageV = 655 };
        island.AddPcs(a);
        island.AddPcs(b);
        island.SetLoadImpedance(new Complex(20, 5));

        var r = island.Solve();

        Assert.True(r.PcsCurrentLimited[0], "pcs1 自然电流 >10A，限幅 8A 应限流");
        Assert.False(r.PcsCurrentLimited[1], "pcs2 无限幅，保持电压源模式");
        Assert.Equal(8.0, r.PcsCurrents[0].Magnitude, 6);

        Complex vbus = r.Solution.Voltage("bus690");
        Complex expectedB = (655 - vbus) / Zout;
        Assert.True((r.PcsCurrents[1] - expectedB).Magnitude < 1e-6, "未限流机仍按 E 串 Z 解出");
        Assert.True((r.PcsCurrentSum - r.LoadCurrent).Magnitude < 1e-6, "两机 + 负荷 KCL 成立");
    }

    [Fact]
    public void GridConnected_LimitedPcs_KclWithThevenin()
    {
        var island = new IsolatedAcIsland("bus690");
        island.SetThevenin(690, new Complex(0.5, 2.0));
        var pcs = new MinPcsPort("pcs1", "bus690", Zout)
        {
            InternalVoltageV = 720,
            CurrentLimitA = 5
        };
        island.AddPcs(pcs);
        island.SetLoadImpedance(new Complex(20, 5));

        var r = island.Solve();

        Assert.True(r.PcsCurrentLimited[0], "内电势高于电网，自然电流约 13A，限幅 5A 应限流");
        Assert.Equal(5.0, r.PcsCurrents[0].Magnitude, 6);

        Complex kcl = r.PcsCurrentSum + r.TheveninCurrentIntoBus - r.LoadCurrent;
        Assert.True(kcl.Magnitude < 1e-6, $"并网岛含限流时 KCL: 残差 {kcl.Magnitude:G3}");
    }

    [Fact]
    public void CurrentLimit_IsInStepEvent_NotSolveFailure()
    {
        var island = new IsolatedAcIsland("bus690");
        island.AddPcs(new MinPcsPort("pcs1", "bus690", Zout) { InternalVoltageV = 690, CurrentLimitA = 20 });
        island.SetLoadImpedance(new Complex(20, 5));

        var r = Record.Exception(() => island.Solve());

        Assert.Null(r);
        Assert.True(island.Solve().PcsCurrentLimited[0]);
    }
}
