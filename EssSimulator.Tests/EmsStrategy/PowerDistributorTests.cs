using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;
using EssSimulator.EmsStrategy.Domain.Algorithms;

namespace EssSimulator.Tests.EmsStrategy;

public class PowerDistributorTests
{
    private static PcsBranchState Branch(int i, double soc, double rated, bool running = true) => new()
    {
        Index = i,
        UnitIndex0 = 0,
        PcsIndexInUnit = i,
        CommOk = true,
        Running = running,
        Soc = soc,
        RatedKw = rated,
        MaxChargeKw = rated,
        MaxDischargeKw = rated
    };

    [Fact]
    public void EqualSplit_TwoBranches()
    {
        var cfg = new DistributionConfig();
        var cmds = PowerDistributor.Distribute(100, new[] { Branch(0, 0.5, 1000), Branch(1, 0.5, 1000) }, cfg);
        Assert.Equal(50, cmds[0].ActivePowerKw, 6);
        Assert.Equal(50, cmds[1].ActivePowerKw, 6);
    }

    [Fact]
    public void SocWeighted_DischargePrefersHighSoc()
    {
        var cfg = new DistributionConfig { SocBalance = true, SocMin = 0.1, SocMax = 0.9 };
        var cmds = PowerDistributor.Distribute(
            100,
            new[] { Branch(0, 0.8, 1000), Branch(1, 0.4, 1000) },
            cfg);
        Assert.True(cmds[0].ActivePowerKw > cmds[1].ActivePowerKw);
        Assert.Equal(100, cmds[0].ActivePowerKw + cmds[1].ActivePowerKw, 6);
    }

    [Fact]
    public void AllUnavailable_OutputsZero()
    {
        var cfg = new DistributionConfig();
        var cmds = PowerDistributor.Distribute(
            100,
            new[] { Branch(0, 0.5, 1000, running: false), Branch(1, 0.5, 1000, running: false) },
            cfg);
        Assert.Equal(0, cmds[0].ActivePowerKw);
        Assert.Equal(0, cmds[1].ActivePowerKw);
    }

    [Fact]
    public void SecondaryAllocation_OverflowGoesToOther()
    {
        var cfg = new DistributionConfig();
        var cmds = PowerDistributor.Distribute(
            150,
            new[] { Branch(0, 0.5, 40), Branch(1, 0.5, 200) },
            cfg);
        Assert.Equal(40, cmds[0].ActivePowerKw, 6);
        Assert.Equal(110, cmds[1].ActivePowerKw, 6);
    }

    [Fact]
    public void Reactive_WeightedByRemainingQCapacity()
    {
        var cfg = new DistributionConfig();
        var a = Branch(0, 0.5, 1000);
        a = new PcsBranchState
        {
            Index = 0,
            UnitIndex0 = 0,
            PcsIndexInUnit = 0,
            CommOk = true,
            Running = true,
            Soc = 0.5,
            RatedKw = 1000,
            MaxChargeKw = 1000,
            MaxDischargeKw = 1000,
            MeasuredActiveKw = 800
        };
        var b = Branch(1, 0.5, 1000);
        var cmds = PowerDistributor.Distribute(0, 160, new[] { a, b }, cfg);
        Assert.Equal(60, cmds[0].ReactivePowerKvar, 5);
        Assert.Equal(100, cmds[1].ReactivePowerKvar, 5);
    }

    [Fact]
    public void Reactive_EqualSplitAmongRunning()
    {
        var cfg = new DistributionConfig();
        var cmds = PowerDistributor.Distribute(
            0,
            80,
            new[] { Branch(0, 0.5, 1000), Branch(1, 0.5, 1000) },
            cfg);
        Assert.Equal(40, cmds[0].ReactivePowerKvar, 6);
        Assert.Equal(40, cmds[1].ReactivePowerKvar, 6);
    }

    [Fact]
    public void Disabled_IgnoresSocBalance()
    {
        var cfg = new DistributionConfig { Enabled = false, SocBalance = true, SocMin = 0.1, SocMax = 0.9 };
        var cmds = PowerDistributor.Distribute(
            100,
            new[] { Branch(0, 0.8, 1000), Branch(1, 0.4, 1000) },
            cfg);
        Assert.Equal(50, cmds[0].ActivePowerKw, 6);
        Assert.Equal(50, cmds[1].ActivePowerKw, 6);
    }

    [Fact]
    public void Disabled_ReactiveEqualSplit_IgnoresRemainingCapacity()
    {
        var cfg = new DistributionConfig { Enabled = false };
        var a = Branch(0, 0.5, 1000);
        a = new PcsBranchState
        {
            Index = 0,
            UnitIndex0 = 0,
            PcsIndexInUnit = 0,
            CommOk = true,
            Running = true,
            Soc = 0.5,
            RatedKw = 1000,
            MaxChargeKw = 1000,
            MaxDischargeKw = 1000,
            MeasuredActiveKw = 800
        };
        var cmds = PowerDistributor.Distribute(0, 160, new[] { a, Branch(1, 0.5, 1000) }, cfg);
        Assert.Equal(80, cmds[0].ReactivePowerKvar, 6);
        Assert.Equal(80, cmds[1].ReactivePowerKvar, 6);
    }
}
