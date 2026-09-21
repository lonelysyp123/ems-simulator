using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;

namespace EssSimulator.Tests.Kernel;

public class IslandGflTests
{
    private static readonly Complex Zout = MinPcsPort.DefaultOutputImpedanceOhm;

    [Fact]
    public void GflIntoStiffGrid_DeliversSetComplexPower()
    {
        var island = new IsolatedAcIsland("bus690");
        island.SetThevenin(690, new Complex(0.02, 0.12));
        island.AddGfl(new MinGflPort("gfl1", "bus690") { ActivePowerW = 100_000, ReactivePowerVAr = 30_000 });

        var r = island.Solve();
        Complex vbus = r.Solution.Voltage("bus690");
        Complex s = vbus * Complex.Conjugate(r.GflCurrents[0]);

        Assert.Equal(100_000, s.Real, 3);
        Assert.Equal(30_000, s.Imaginary, 3);
        Assert.True((r.PcsCurrentSum + r.GflCurrentSum + r.TheveninCurrentIntoBus - r.LoadCurrent).Magnitude < 1e-6,
            "母线 KCL：跟网注入 + 戴维南 = 负荷（无构网机）");
    }

    [Fact]
    public void GflCurrentLimit_CapsCurrent_ReducesDeliveredPower()
    {
        var island = new IsolatedAcIsland("bus690");
        island.SetThevenin(690, new Complex(0.02, 0.12));
        island.AddGfl(new MinGflPort("gfl1", "bus690")
        {
            ActivePowerW = 1_000_000_000,
            ReactivePowerVAr = 0,
            CurrentLimitA = 100
        });

        var r = island.Solve();
        Complex vbus = r.Solution.Voltage("bus690");
        Complex s = vbus * Complex.Conjugate(r.GflCurrents[0]);

        Assert.Equal(100, r.GflCurrents[0].Magnitude, 6);
        Assert.True(s.Real < 1_000_000_000, "限流后交付功率远低于设定");
        Assert.True((r.GflCurrentSum + r.TheveninCurrentIntoBus).Magnitude < 1e-6);
    }

    [Fact]
    public void GflCoexistsWithGfm_IslandedKclHolds()
    {
        var island = new IsolatedAcIsland("bus690");
        island.AddPcs(new MinPcsPort("gfm1", "bus690", Zout) { InternalVoltageV = 690 });
        island.AddGfl(new MinGflPort("gfl1", "bus690") { ActivePowerW = 50_000 });
        island.SetLoadImpedance(new Complex(20, 5));

        var r = island.Solve();
        Complex vbus = r.Solution.Voltage("bus690");

        Assert.True(vbus.Magnitude > 600, "构网机应把母线定在额定附近");
        Complex s = vbus * Complex.Conjugate(r.GflCurrents[0]);
        Assert.Equal(50_000, s.Real, 2);
        Assert.True((r.PcsCurrentSum + r.GflCurrentSum - r.LoadCurrent).Magnitude < 1e-6,
            "母线 KCL：构网 + 跟网 = 负荷（无戴维南）");
    }

    [Fact]
    public void GflNoSetpoint_NoInjection()
    {
        var island = new IsolatedAcIsland("bus690");
        island.SetThevenin(690, new Complex(0.02, 0.12));
        island.AddGfl(new MinGflPort("gfl1", "bus690"));

        var r = island.Solve();

        Assert.True(r.GflCurrents[0].Magnitude < 1e-9);
        Assert.Equal(690, r.Solution.Voltage("bus690").Magnitude, 6);
    }
}
