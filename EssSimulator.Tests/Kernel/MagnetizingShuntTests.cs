using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;

namespace EssSimulator.Tests.Kernel;

public class MagnetizingShuntTests
{
    private static Complex ShuntFor(double ratedKva, double noLoadCurrentPercent, double vNom, double ironKw)
    {
        double s = ratedKva * 1000.0;
        double q = noLoadCurrentPercent / 100.0 * s;
        double p = ironKw * 1000.0;
        Complex y = new(p / (vNom * vNom), -(q / (vNom * vNom)));
        return Complex.One / y;
    }

    [Fact]
    public void OneFormingSource_SuppliesShuntVars_AndKclHolds()
    {
        Complex zShunt = ShuntFor(2500, 2, 690, ironKw: 0);
        var island = new IsolatedAcIsland("bus");
        island.AddPcs(new MinPcsPort("a", "bus", new Complex(0.002, 0.01))
        {
            InternalVoltageV = 690
        });
        island.SetBusShunt(zShunt);

        var r = island.Solve();
        Complex v = r.Solution.Voltage("bus");
        Complex s = v * Complex.Conjugate(r.ShuntCurrent);

        Assert.InRange(s.Imaginary, 45_000, 50_000);
        Assert.True(Math.Abs(s.Real) < 1);
        Assert.True((r.PcsCurrentSum - r.ShuntCurrent).Magnitude < 1e-6);
        Complex delivered = v * Complex.Conjugate(r.PcsCurrents[0]);
        Assert.Equal(s.Imaginary, delivered.Imaginary, 3);
    }

    [Fact]
    public void TwoEqualSources_SplitMagnetizingCurrentInHalf()
    {
        Complex zShunt = ShuntFor(2500, 2, 690, ironKw: 0);
        Complex zPcs = new(0.002, 0.01);
        var island = new IsolatedAcIsland("bus");
        island.AddPcs(new MinPcsPort("a", "bus", zPcs) { InternalVoltageV = 690 });
        island.AddPcs(new MinPcsPort("b", "bus", zPcs) { InternalVoltageV = 690 });
        island.SetBusShunt(zShunt);

        var r = island.Solve();
        Assert.True((r.PcsCurrents[0] - r.PcsCurrents[1]).Magnitude < 1e-6, "等势等阻抗应均分，不靠权重");
        Assert.True((r.PcsCurrentSum - r.ShuntCurrent).Magnitude < 1e-6);
        Assert.True(r.ShuntCurrent.Magnitude > 1);
    }

    [Fact]
    public void VarsScaleWithVoltageSquared()
    {
        Complex zShunt = ShuntFor(2500, 2, 690, ironKw: 0);
        var full = SolveAt(zShunt, 690);
        var half = SolveAt(zShunt, 345);
        double qFull = (full.Voltage * Complex.Conjugate(full.Shunt)).Imaginary;
        double qHalf = (half.Voltage * Complex.Conjugate(half.Shunt)).Imaginary;
        Assert.Equal(qFull / 4.0, qHalf, 0);
    }

    [Fact]
    public void InjectedCurrent_IsSuppliedByTheFormingSource()
    {
        var island = new IsolatedAcIsland("bus");
        island.AddPcs(new MinPcsPort("a", "bus", new Complex(0.01, 0.05))
        {
            InternalVoltageV = 690
        });
        Complex injected = new(0, -20);
        island.SetBusInjectedCurrent(injected);

        var r = island.Solve();
        Assert.True((r.PcsCurrentSum + injected).Magnitude < 1e-6);
    }

    [Fact]
    public void Runner_UpdatesInjectedCurrentWithoutRebuild()
    {
        var sources = new[]
        {
            new FormingSourceSample("a", 690, new Complex(0.01, 0.05))
        };
        var runner = new IslandShadowRunner(new IslandShadowSnapshot("bus", sources));
        var first = runner.Tick();
        Assert.True(first.Island.PcsCurrentSum.Magnitude < 1e-6);

        runner.SetBusInjectedCurrent(new Complex(0, -12));
        var second = runner.Tick();
        Assert.True((second.Island.PcsCurrentSum - new Complex(0, 12)).Magnitude < 1e-6);
    }

    private static (Complex Voltage, Complex Shunt) SolveAt(Complex zShunt, double e)
    {
        var island = new IsolatedAcIsland("bus");
        island.AddPcs(new MinPcsPort("a", "bus", new Complex(1e-4, 1e-4))
        {
            InternalVoltageV = e
        });
        island.SetBusShunt(zShunt);
        var r = island.Solve();
        return (r.Solution.Voltage("bus"), r.ShuntCurrent);
    }
}
