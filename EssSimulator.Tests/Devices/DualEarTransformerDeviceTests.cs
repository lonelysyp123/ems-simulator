using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class DualEarTransformerDeviceTests
{
    [Fact]
    public void Step_sums_ear_power_onto_primary_and_keeps_ears_independent()
    {
        var xf = Create();
        xf.Primary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 35000,
            FrequencyHz = 50
        });
        xf.SecondaryLeft.Input = ElectricalPortSnapshot.FromAc(
            AcQuantityConverter.FromLineVoltageAndPower(690, -800, -50, ThreePhaseConnection.Star, 50));
        xf.SecondaryRight.Input = ElectricalPortSnapshot.FromAc(
            AcQuantityConverter.FromLineVoltageAndPower(690, -400, 20, ThreePhaseConnection.Star, 50));

        xf.Step(new DeviceStepContext(), TimeSpan.FromMilliseconds(100));

        Assert.Equal(-800, xf.ActivePowerKwLeft, 3);
        Assert.Equal(-400, xf.ActivePowerKwRight, 3);
        Assert.Equal(-50, xf.ReactiveKvarLeft, 3);
        Assert.Equal(20, xf.ReactiveKvarRight, 3);

        var pri = xf.Primary.Output.Ac!.Internal;
        Assert.InRange(pri.ActivePowerKw, -1280, -1160);

        var leftV = xf.SecondaryLeft.Output.Ac!.Internal.LineVoltageV;
        var rightV = xf.SecondaryRight.Output.Ac!.Internal.LineVoltageV;
        Assert.True(leftV > 600);
        Assert.Equal(leftV, rightV, 3);
        Assert.InRange(xf.SecondaryLeft.Output.Ac.Internal.ActivePowerKw, -805, -795);
        Assert.InRange(xf.SecondaryRight.Output.Ac.Internal.ActivePowerKw, -405, -395);
        Assert.True(xf.CirculatingCurrentA > 0);
    }

    [Fact]
    public void Through_device_id_matches_dual_ear_id()
    {
        var xf = Create();
        Assert.Equal("unit_xf", xf.DeviceId);
        Assert.Equal("unit_xf", xf.Through.DeviceId);
        Assert.Equal(3, xf.Ports.Count);
    }

    private static DualEarTransformerDevice Create() =>
        new("unit_xf", new DualEarTransformerConfig
        {
            RatedPowerKva = 6300,
            PrimaryNominalLineVoltageV = 35000,
            SecondaryNominalLineVoltageV = 690,
            MagnetizingInrushEnabled = false
        });
}
