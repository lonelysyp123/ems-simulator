using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;

namespace EssSimulator.Tests.Kernel;

public class AverageConverterPortTests
{
    private static readonly Complex Zout = MinPcsPort.DefaultOutputImpedanceOhm;
    private const double Vdc = 1200;

    [Fact]
    public void Discharge_DcSuppliesAcAndLoss()
    {
        var island = new IsolatedAcIsland("bus690");
        var conv = new AverageConverterPort("pcs1", "bus690", Zout, efficiency: 0.98)
        {
            InternalVoltageV = 700,
            DcVoltageV = Vdc
        };
        island.AddPcs(conv.Ac);
        island.SetLoadImpedance(new Complex(12, 3));

        var r = island.Solve();
        Complex eBefore = conv.InternalVoltageV;
        var dc = conv.SettleDc(r, 0);

        Assert.True(dc.PacIntoDeviceW < 0, "带负荷时应向交流网送出功率");
        Assert.True(dc.PdcIntoDeviceW > 0, "放电时直流口电流流入变流器");
        Assert.True(dc.LossW > 0);
        Assert.True(Math.Abs(dc.PdcIntoDeviceW + dc.PacIntoDeviceW - dc.LossW) < 1e-6);
        Assert.True(Math.Abs(dc.PdcIntoDeviceW * 0.98 + dc.PacIntoDeviceW) < 1e-3);
        Assert.Equal(dc.PdcIntoDeviceW / Vdc, dc.IdcIntoDeviceA, 9);
        Assert.True((conv.InternalVoltageV - eBefore).Magnitude < 1e-12, "结算不得改写控制参考");
        Assert.True((r.Solution.Voltage("bus690") - island.Solve().Solution.Voltage("bus690")).Magnitude < 1e-9);
    }

    [Fact]
    public void Charge_AcSuppliesDcAndLoss()
    {
        var island = new IsolatedAcIsland("bus690");
        island.SetThevenin(690, new Complex(0.02, 0.12));
        var conv = new AverageConverterPort("pcs1", "bus690", Zout, efficiency: 0.98)
        {
            InternalVoltageV = 650,
            DcVoltageV = Vdc
        };
        island.AddPcs(conv.Ac);

        var r = island.Solve();
        var dc = conv.SettleDc(r, 0);

        Assert.True(dc.PacIntoDeviceW > 0, "内电势低于戴维南时应从交流口吸收功率");
        Assert.True(dc.PdcIntoDeviceW < 0, "充电时直流口电流流出变流器");
        Assert.True(dc.LossW > 0);
        Assert.True(Math.Abs(dc.PdcIntoDeviceW + dc.PacIntoDeviceW - dc.LossW) < 1e-6);
        Assert.True(Math.Abs(-dc.PdcIntoDeviceW - dc.PacIntoDeviceW * 0.98) < 1e-3);
    }

    [Fact]
    public void ZeroDcVoltage_Rejected()
    {
        var island = new IsolatedAcIsland("bus690");
        var conv = new AverageConverterPort("pcs1", "bus690", Zout)
        {
            InternalVoltageV = 690,
            DcVoltageV = 0
        };
        island.AddPcs(conv.Ac);
        island.SetLoadImpedance(20);

        var r = island.Solve();
        Assert.Throws<NetworkSolveException>(() => conv.SettleDc(r, 0));
    }

    [Fact]
    public void EfficiencyOutOfRange_Rejected()
    {
        Assert.Throws<NetworkSolveException>(() =>
            new AverageConverterPort("pcs", "bus", Zout, efficiency: 0));
        Assert.Throws<NetworkSolveException>(() =>
            new AverageConverterPort("pcs", "bus", Zout, efficiency: 1.01));
    }

    [Fact]
    public void TwoConverters_DcFollowsOwnPac_NotSharedAverage()
    {
        var island = new IsolatedAcIsland("bus690");
        var a = new AverageConverterPort("a", "bus690", Zout) { InternalVoltageV = 690, DcVoltageV = Vdc };
        var b = new AverageConverterPort("b", "bus690", Zout) { InternalVoltageV = 655, DcVoltageV = Vdc };
        island.AddPcs(a.Ac);
        island.AddPcs(b.Ac);
        island.SetLoadImpedance(new Complex(20, 5));

        var r = island.Solve();
        var da = a.SettleDc(r, 0);
        var db = b.SettleDc(r, 1);

        Assert.True(Math.Abs(da.PacIntoDeviceW - db.PacIntoDeviceW) > 100, "环流使两机 Pac 不同");
        Assert.True(Math.Abs(da.IdcIntoDeviceA - db.IdcIntoDeviceA) > 0.05, "直流不得按邻机均分");
        Assert.True(da.LossW > 0 && db.LossW > 0);
    }

    [Fact]
    public void CurrentLimited_SettlesFromClampedCurrent_NotVoltageMode()
    {
        var island = new IsolatedAcIsland("bus690");
        var conv = new AverageConverterPort("pcs1", "bus690", Zout, efficiency: 0.98)
        {
            InternalVoltageV = 700,
            DcVoltageV = Vdc
        };
        conv.Ac.CurrentLimitA = 20;
        island.AddPcs(conv.Ac);
        island.SetLoadImpedance(new Complex(12, 3));

        var r = island.Solve();
        Assert.True(r.PcsCurrentLimited[0], "本拍应处于限流模式");
        Assert.Equal(20, r.PcsCurrents[0].Magnitude, 6);

        var dc = conv.SettleDc(r, 0);

        Complex vbus = r.Solution.Voltage("bus690");
        double pacFromClamped = (vbus * Complex.Conjugate(-r.PcsCurrents[0])).Real;
        double pacVoltageMode = (vbus * Complex.Conjugate(-conv.Ac.TerminalCurrent(r.Solution))).Real;
        Assert.True(Math.Abs(pacVoltageMode) > Math.Abs(pacFromClamped),
            "限流时按 (E−Vbus)/Z 重算会高送端功率");
        Assert.Equal(pacFromClamped, dc.PacIntoDeviceW, 6);
        Assert.True(Math.Abs(dc.PdcIntoDeviceW + dc.PacIntoDeviceW - dc.LossW) < 1e-6, "钳位功率下仍守恒");
        Assert.True(Math.Abs(dc.PdcIntoDeviceW * 0.98 + dc.PacIntoDeviceW) < 1e-3);
    }

    [Fact]
    public void SettleDc_PortIndexOutOfRange_Rejected()
    {
        var island = new IsolatedAcIsland("bus690");
        var conv = new AverageConverterPort("pcs1", "bus690", Zout) { InternalVoltageV = 690, DcVoltageV = Vdc };
        island.AddPcs(conv.Ac);
        island.SetLoadImpedance(20);
        var r = island.Solve();
        Assert.Throws<ArgumentOutOfRangeException>(() => conv.SettleDc(r, 1));
    }
}
