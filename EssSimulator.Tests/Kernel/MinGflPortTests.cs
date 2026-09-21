using System.Numerics;
using EssSimulator.EssDeviceSimModel.Kernel;

namespace EssSimulator.Tests.Kernel;

public class MinGflPortTests
{
    [Fact]
    public void InjectionCurrentAt_UnityPf_DeliversSetActivePower()
    {
        var port = new MinGflPort("gfl", "bus") { ActivePowerW = 100_000, ReactivePowerVAr = 0 };
        Complex vbus = 690;

        Complex i = port.InjectionCurrentAt(vbus);
        Complex s = vbus * Complex.Conjugate(i);

        Assert.Equal(100_000.0 / 690.0, i.Magnitude, 6);
        Assert.Equal(0, i.Phase, 6);
        Assert.Equal(100_000, s.Real, 3);
        Assert.Equal(0, s.Imaginary, 3);
    }

    [Fact]
    public void InjectionCurrentAt_WithReactive_DeliversSetComplexPower()
    {
        var port = new MinGflPort("gfl", "bus") { ActivePowerW = 100_000, ReactivePowerVAr = 50_000 };
        Complex vbus = 690;

        Complex s = vbus * Complex.Conjugate(port.InjectionCurrentAt(vbus));

        Assert.Equal(100_000, s.Real, 3);
        Assert.Equal(50_000, s.Imaginary, 3);
    }

    [Fact]
    public void InjectionCurrentAt_AngledBus_StillDeliversSetComplexPower()
    {
        var port = new MinGflPort("gfl", "bus") { ActivePowerW = 80_000, ReactivePowerVAr = 30_000 };
        Complex vbus = new(690 * Math.Cos(Math.PI / 6), 690 * Math.Sin(Math.PI / 6));

        Complex s = vbus * Complex.Conjugate(port.InjectionCurrentAt(vbus));

        Assert.Equal(80_000, s.Real, 3);
        Assert.Equal(30_000, s.Imaginary, 3);
    }

    [Fact]
    public void CurrentLimit_CapsMagnitude_KeepsAngle_ReducesDeliveredPower()
    {
        var port = new MinGflPort("gfl", "bus")
        {
            ActivePowerW = 200_000,
            ReactivePowerVAr = 0,
            CurrentLimitA = 100
        };
        Complex vbus = 690;

        Complex i = port.InjectionCurrentAt(vbus);
        Complex s = vbus * Complex.Conjugate(i);

        Assert.Equal(100, i.Magnitude, 6);
        Assert.Equal(0, i.Phase, 6);
        Assert.Equal(69_000, s.Real, 3);
        Assert.True(s.Real < port.ActivePowerW, "限流后交付功率低于设定，不得假装仍达设定");
    }

    [Fact]
    public void CurrentLimit_KeepsAngle_WithReactiveSetpoint()
    {
        var port = new MinGflPort("gfl", "bus")
        {
            ActivePowerW = 100_000,
            ReactivePowerVAr = 100_000,
            CurrentLimitA = 50
        };

        Complex i = port.InjectionCurrentAt(690);

        Assert.Equal(50, i.Magnitude, 6);
        Assert.Equal(-Math.PI / 4, i.Phase, 6);
    }

    [Fact]
    public void InjectionCurrentAt_BelowThresholdVoltage_NoInjection()
    {
        var port = new MinGflPort("gfl", "bus") { ActivePowerW = 100_000 };

        Assert.Equal(Complex.Zero, port.InjectionCurrentAt(Complex.Zero));
        Assert.Equal(Complex.Zero, port.InjectionCurrentAt(0.5));
    }

    [Fact]
    public void Stamp_RecordsCurrent_AndInjectsIntoNetwork()
    {
        var net = new PhasorNetwork();
        net.SetGround("gnd");
        net.AddNode("bus");
        net.AddImpedance("bus", "gnd", new Complex(2, 0));
        var port = new MinGflPort("gfl", "bus") { ActivePowerW = 1000 };

        port.Stamp(net, 100);
        var sol = net.Solve();

        Assert.Equal(10, port.InjectedCurrentA.Real, 6);
        Assert.Equal(20, sol.Voltage("bus").Real, 6);
    }

    [Fact]
    public void Ctor_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => new MinGflPort("", "bus"));
        Assert.Throws<ArgumentException>(() => new MinGflPort("gfl", " "));
    }
}
