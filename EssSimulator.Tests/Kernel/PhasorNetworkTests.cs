using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;

namespace EssSimulator.Tests.Kernel;

public class PhasorNetworkTests
{
    private static readonly Complex Zsrc = new(0.05, 0.20);

    [Fact]
    public void SeriesResistance_MatchesOhmsLaw()
    {
        var net = new PhasorNetwork();
        net.SetGround("gnd");
        net.AddVoltageBehindImpedance("bus", 690, 0.01);
        net.AddImpedance("bus", "gnd", 10.0);

        var sol = net.Solve();
        Complex iSrc = net.VoltageBehindZCurrent(sol, "bus", 690, 0.01);
        Complex iLoad = net.BranchCurrent(sol, "bus", "gnd", 10);

        Assert.True((iSrc - iLoad).Magnitude < 1e-6);
        Assert.InRange(sol.Voltage("bus").Magnitude, 680, 690);
    }

    [Fact]
    public void TwoFormingSources_UnequalE_ProduceCirculatingCurrent()
    {
        Complex e1 = 690;
        Complex e2 = 660;
        var net = new PhasorNetwork();
        net.SetGround("gnd");
        net.AddVoltageBehindImpedance("bus", e1, Zsrc);
        net.AddVoltageBehindImpedance("bus", e2, Zsrc);

        var sol = net.Solve();
        Complex i1 = net.VoltageBehindZCurrent(sol, "bus", e1, Zsrc);
        Complex i2 = net.VoltageBehindZCurrent(sol, "bus", e2, Zsrc);

        Complex vExpected = 0.5 * (e1 + e2);
        Complex iCirc = (e1 - e2) / (2.0 * Zsrc);

        Assert.True((sol.Voltage("bus") - vExpected).Magnitude < 1e-6);
        Assert.True((i1 - iCirc).Magnitude < 1e-6);
        Assert.True((i1 + i2).Magnitude < 1e-8, "KCL at bus: source currents must sum to 0 with no load");
        Assert.True(i1.Magnitude > 50, "Voltage mismatch must create circulating current, not an averaged zero-current bus");
    }

    [Fact]
    public void ZeroImpedanceVoltageSource_IsRejected()
    {
        var net = new PhasorNetwork();
        net.SetGround("gnd");
        var ex = Assert.Throws<NetworkSolveException>(() =>
            net.AddVoltageBehindImpedance("bus", 690, Complex.Zero));
        Assert.Contains("finite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StampOrder_DoesNotChangeVoltages()
    {
        Complex e1 = 690;
        Complex e2 = 670 + new Complex(0, 5);
        var zLoad = new Complex(8, 2);

        Complex vA = SolveTwoSource(e1, e2, zLoad, swap: false);
        Complex vB = SolveTwoSource(e1, e2, zLoad, swap: true);
        Assert.True((vA - vB).Magnitude < 1e-9);
    }

    [Fact]
    public void OpenSwitch_CarriesNoCurrent()
    {
        var net = new PhasorNetwork();
        net.SetGround("gnd");
        net.AddVoltageBehindImpedance("a", 690, 0.1);
        net.AddSwitch("a", "b", closed: false);
        net.AddImpedance("b", "gnd", 10.0);

        var sol = net.Solve();
        Assert.True(sol.Voltage("b").Magnitude < 1e-9);
        Assert.InRange(sol.Voltage("a").Magnitude, 680, 690);
    }

    private static Complex SolveTwoSource(Complex e1, Complex e2, Complex zLoad, bool swap)
    {
        var net = new PhasorNetwork();
        net.SetGround("gnd");
        if (!swap)
        {
            net.AddVoltageBehindImpedance("bus", e1, Zsrc);
            net.AddVoltageBehindImpedance("bus", e2, Zsrc);
        }
        else
        {
            net.AddVoltageBehindImpedance("bus", e2, Zsrc);
            net.AddVoltageBehindImpedance("bus", e1, Zsrc);
        }

        net.AddImpedance("bus", "gnd", zLoad);
        return net.Solve().Voltage("bus");
    }
}
