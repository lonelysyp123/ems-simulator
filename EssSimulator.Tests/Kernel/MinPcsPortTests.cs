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
    public void FormingImpedanceOhm_AtRatedBase_MatchesPhaseTwoPlaceholder()
    {
        Complex z = MinPcsPort.FormingImpedanceOhm(2508, 690);
        Assert.Equal(0.04, z.Real, 3);
        Assert.Equal(0.18, z.Imaginary, 3);
    }

    [Fact]
    public void FormingImpedanceOhm_ScalesInverselyWithRating()
    {
        Complex full = MinPcsPort.FormingImpedanceOhm(2508, 690);
        Complex half = MinPcsPort.FormingImpedanceOhm(1254, 690);
        Assert.Equal(full.Magnitude * 2, half.Magnitude, 9);
        Assert.Equal(full.Real / full.Imaginary, half.Real / half.Imaginary, 9);
    }

    [Fact]
    public void FormingImpedanceOhm_ScalesWithVoltageSquared()
    {
        Complex v690 = MinPcsPort.FormingImpedanceOhm(2508, 690);
        Complex v400 = MinPcsPort.FormingImpedanceOhm(2508, 400);
        double ratio = (400.0 * 400.0) / (690.0 * 690.0);
        Assert.Equal(v690.Magnitude * ratio, v400.Magnitude, 9);
    }

    [Fact]
    public void TerminalMustMatchIslandBus()
    {
        var island = new IsolatedAcIsland("bus690");
        var pcs = new MinPcsPort("pcs1", "other", Zout) { InternalVoltageV = 690 };
        Assert.Throws<ArgumentException>(() => island.AddPcs(pcs));
    }
}
