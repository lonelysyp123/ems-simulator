using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Svg;

namespace EssSimulator.Tests.Svg;

public class SvgDeviceTests
{
    private const double NominalV = 35000;
    private const double RatedKvar = 10000;

    private static SvgDevice NewDevice() =>
        new("svg", new SvgConfig { RatedCapacityKvar = RatedKvar, NominalLineVoltageV = NominalV });

    [Fact]
    public void HundredMillisecondStep_ReachesSetpoint_FiveMillisecondStep_StillLags()
    {
        var settled = NewDevice();
        settled.RunCommand = 1;
        settled.ReactiveSetpointKvar = 5000;
        settled.ApplyMeasurement(NominalV, 50, TimeSpan.FromMilliseconds(100));
        Assert.True(settled.ReactivePowerKvar >= 0.99 * 5000);

        var lagging = NewDevice();
        lagging.RunCommand = 1;
        lagging.ReactiveSetpointKvar = 5000;
        lagging.ApplyMeasurement(NominalV, 50, TimeSpan.FromMilliseconds(5));
        Assert.InRange(lagging.ReactivePowerKvar, 500, 2500);
    }

    [Fact]
    public void SetpointAboveAvailableLimit_IsClampedOnOutputOnly()
    {
        var device = NewDevice();
        device.RunCommand = 1;
        device.ReactiveSetpointKvar = 20000;
        device.ApplyMeasurement(NominalV, 50, TimeSpan.FromSeconds(1));

        Assert.Equal(RatedKvar, device.AvailableReactiveUpperKvar, 6);
        Assert.Equal(RatedKvar, device.ReactivePowerKvar, 3);
        Assert.Equal(20000, device.ReactiveSetpointKvar);

        device.ApplyMeasurement(0.9 * NominalV, 50, TimeSpan.FromSeconds(1));
        Assert.Equal(0.9 * RatedKvar, device.AvailableReactiveUpperKvar, 6);
        Assert.Equal(0.9 * RatedKvar, device.ReactivePowerKvar, 3);
        Assert.Equal(20000, device.ReactiveSetpointKvar);
    }

    [Fact]
    public void OffAndBlocked_ClearOutputAndKeepSetpoint()
    {
        var device = NewDevice();
        device.RunCommand = 1;
        device.ReactiveSetpointKvar = 1234;
        device.ApplyMeasurement(NominalV, 50, TimeSpan.FromSeconds(1));
        Assert.Equal(1, device.RunState);
        Assert.Equal(-SvgDevice.LossFraction * RatedKvar, device.ActivePowerKw, 6);
        double expectedCurrent = 1234 * 1000 / (ElectricalConventions.Sqrt3 * NominalV);
        Assert.Equal(expectedCurrent, device.PhaseACurrentA, 6);
        Assert.Equal(device.PhaseACurrentA, device.PhaseBCurrentA);
        Assert.Equal(device.PhaseACurrentA, device.PhaseCCurrentA);
        double apparent = Math.Sqrt(Math.Pow(SvgDevice.LossFraction * RatedKvar, 2) + 1234 * 1234);
        Assert.Equal(SvgDevice.LossFraction * RatedKvar / apparent, device.PowerFactor, 6);

        device.RunCommand = 0;
        device.ApplyMeasurement(NominalV, 50, TimeSpan.FromMilliseconds(5));
        Assert.Equal(0, device.RunState);
        Assert.Equal(0, device.ReactivePowerKvar);
        Assert.Equal(0, device.PhaseACurrentA);
        Assert.Equal(0, device.ActivePowerKw);
        Assert.Equal(1, device.PowerFactor);
        Assert.Equal(1234, device.ReactiveSetpointKvar);
        Assert.Equal(NominalV, device.LineVoltageAbV);

        device.RunCommand = 1;
        device.ApplyMeasurement(1.2 * NominalV, 50, TimeSpan.FromMilliseconds(5));
        Assert.Equal(2, device.RunState);
        Assert.Equal(0, device.ReactivePowerKvar);
        Assert.Equal(0, device.AvailableReactiveUpperKvar);
        Assert.Equal(1234, device.ReactiveSetpointKvar);

        device.ApplyMeasurement(0, 50, TimeSpan.FromMilliseconds(5));
        Assert.Equal(2, device.RunState);
        device.ApplyMeasurement(NominalV, 0, TimeSpan.FromMilliseconds(5));
        Assert.Equal(2, device.RunState);
        Assert.Equal(0, device.ReactivePowerKvar);
    }
}
