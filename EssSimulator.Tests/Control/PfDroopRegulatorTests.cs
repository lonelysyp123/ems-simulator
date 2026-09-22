using EssSimulator.EssDeviceSimModel.Control;

namespace EssSimulator.Tests.Control;

public class PfDroopRegulatorTests
{
    [Fact]
    public void Compute_HigherP_LowersFrequency()
    {
        var droop = new PfDroopRegulator(mp: 0.5 / 1725, p0Kw: 0, deadbandKw: 0, fMinHz: 49, fMaxHz: 50.5);
        double fLow = droop.Compute(50, 0);
        double fHigh = droop.Compute(50, 1725);
        Assert.Equal(50, fLow, 6);
        Assert.True(fHigh < fLow);
        Assert.Equal(49.5, fHigh, 3);
    }

    [Fact]
    public void Compute_InsideDeadband_KeepsF0()
    {
        var droop = new PfDroopRegulator(mp: 0.01, p0Kw: 0, deadbandKw: 50, fMinHz: 49, fMaxHz: 50.5);
        Assert.Equal(50, droop.Compute(50, 20), 9);
    }

    [Fact]
    public void Compute_ZeroMp_KeepsF0()
    {
        var droop = new PfDroopRegulator(mp: 0, p0Kw: 0, deadbandKw: 0, fMinHz: 49, fMaxHz: 50.5);
        Assert.Equal(50, droop.Compute(50, 2000), 9);
    }

    [Fact]
    public void ResolveMp_DefaultIsHalfHzAtRatedPower()
    {
        double mp = PfDroopRegulator.ResolveMp(mp: 0, ratedKw: 1725);
        Assert.Equal(0.5 / 1725, mp, 9);
    }

    [Fact]
    public void Compute_ClampsToBand()
    {
        var droop = new PfDroopRegulator(mp: 10, p0Kw: 0, deadbandKw: 0, fMinHz: 49, fMaxHz: 50.5);
        Assert.Equal(49, droop.Compute(50, 1000), 9);
    }
}

public class PhaseIntegratorTests
{
    [Fact]
    public void Step_FiftyHz_OneSecond_AdvancesOneHundredPi()
    {
        var phase = new PhaseIntegrator();
        double unwrapped = 0;
        double prev = phase.ThetaRad;
        const double dt = 0.001;
        for (int i = 0; i < 1000; i++)
        {
            phase.Step(50, dt);
            unwrapped += PhaseIntegrator.AngleErrorRad(phase.ThetaRad, prev);
            prev = phase.ThetaRad;
        }

        Assert.InRange(unwrapped, 100 * Math.PI - 0.05, 100 * Math.PI + 0.05);
    }

    [Fact]
    public void Step_NonPositiveDt_DoesNotAdvance()
    {
        var phase = new PhaseIntegrator();
        phase.Step(50, 0.1);
        double th = phase.ThetaRad;
        phase.Step(50, 0);
        phase.Step(50, -0.01);
        Assert.Equal(th, phase.ThetaRad);
    }
}

public class PllTrackerTests
{
    [Fact]
    public void Step_BusZero_DoesNotLock()
    {
        var pll = new PllTracker();
        pll.Step(busV: 0, busF: 50, busTheta: 0.4, enableV: 138, dt: 0.01);
        Assert.False(pll.Enabled);
    }

    [Fact]
    public void Step_BusStepToNominal_LocksWithin200ms()
    {
        var pll = new PllTracker(tauSec: 0.1);
        double enable = 0.20 * 690;
        pll.Step(690, 50, 0.3, enable, 0.01);
        Assert.True(pll.Enabled);

        for (int i = 0; i < 20; i++)
            pll.Step(690, 50, 0.3, enable, 0.01);

        Assert.InRange(pll.FrequencyHz, 49.8, 50.2);
        Assert.True(Math.Abs(PhaseIntegrator.AngleErrorRad(pll.ThetaRad, 0.3)) < 10 * Math.PI / 180);
    }
}

public class PreSyncSupervisorTests
{
    [Fact]
    public void IsReadyToCutIn_RequiresBlackStartAndAllThreeWindows()
    {
        var pre = new PreSyncSupervisor();
        Assert.False(pre.IsReadyToCutIn(690, 50, 0.05, vBus: 690, fBus: 50, thetaBus: 0, vNom: 690,
            blackStartEnabled: false));
        Assert.False(pre.IsReadyToCutIn(690, 50, 0, vBus: 100, fBus: 50, thetaBus: 0, vNom: 690,
            blackStartEnabled: true));
        Assert.False(pre.IsReadyToCutIn(600, 50, 0, vBus: 690, fBus: 50, thetaBus: 0, vNom: 690,
            blackStartEnabled: true));
        Assert.False(pre.IsReadyToCutIn(690, 50.5, 0, vBus: 690, fBus: 50, thetaBus: 0, vNom: 690,
            blackStartEnabled: true));
        Assert.False(pre.IsReadyToCutIn(690, 50, 0.3, vBus: 690, fBus: 50, thetaBus: 0, vNom: 690,
            blackStartEnabled: true));
        Assert.True(pre.IsReadyToCutIn(690, 50, 0.05, vBus: 690, fBus: 50, thetaBus: 0, vNom: 690,
            blackStartEnabled: true));
        Assert.True(pre.IsReadyToCutIn(138, 50, 0.05, vBus: 138, fBus: 50, thetaBus: 0, vNom: 690,
            blackStartEnabled: true));
    }
}
