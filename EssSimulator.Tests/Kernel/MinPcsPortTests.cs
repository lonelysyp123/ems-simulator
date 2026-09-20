using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;

namespace EssSimulator.Tests.Kernel;

public class MinPcsPortTests
{
    private static readonly Complex Zout = new(0.04, 0.18);

    [Fact]
    public void IsolatedIsland_TwoPorts_KclAndPowerBalance()
    {
        var island = new IsolatedAcIsland("bus690");
        var a = new MinPcsPort("pcs1", "bus690", Zout) { InternalVoltageV = 690 };
        var b = new MinPcsPort("pcs2", "bus690", Zout) { InternalVoltageV = 655 };
        island.AddPcs(a);
        island.AddPcs(b);
        island.SetLoadImpedance(new Complex(20, 5));

        var r = island.Solve();
        Complex v = r.Solution.Voltage("bus690");
        Assert.True((r.PcsCurrentSum - r.LoadCurrent).Magnitude < 1e-6, "KCL: sum of PCS currents equals load current");

        double pGen = 0;
        for (int i = 0; i < island.PcsPorts.Count; i++)
        {
            Complex iPcs = r.PcsCurrents[i];
            pGen += (v * Complex.Conjugate(iPcs)).Real;
        }

        double pLoad = (v * Complex.Conjugate(r.LoadCurrent)).Real;
        Assert.True(Math.Abs(pGen - pLoad) < 1e-3, $"Power balance gen={pGen:F4} load={pLoad:F4}");
        Assert.True(r.PcsCurrents[0].Magnitude > 10);
        Assert.True(r.PcsCurrents[1].Magnitude > 10);
    }

    [Fact]
    public void MinPcsPort_RejectsZeroImpedance()
    {
        Assert.Throws<NetworkSolveException>(() => new MinPcsPort("pcs", "bus", Complex.Zero));
    }

    [Fact]
    public void TerminalMustMatchIslandBus()
    {
        var island = new IsolatedAcIsland("bus690");
        var pcs = new MinPcsPort("pcs1", "other", Zout) { InternalVoltageV = 690 };
        Assert.Throws<ArgumentException>(() => island.AddPcs(pcs));
    }
}
