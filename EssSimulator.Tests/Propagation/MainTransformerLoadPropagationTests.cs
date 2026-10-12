using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Propagation;

public class MainTransformerLoadPropagationTests
{
    [Fact]
    public void Step_with_load_secondary_intent_reflects_active_power_on_primary()
    {
        var xf = new TransformerDevice("main", new TransformerDeviceConfig
        {
            RatedPowerKva = 31_500,
            PrimaryNominalLineVoltageV = 220_000,
            SecondaryNominalLineVoltageV = 35_000,
            NoLoadLossKw = 0.1,
            NoLoadCurrentPercent = 2
        });

        var loadIntent = AcQuantityConverter.FromLineVoltageAndPower(
            35_000, -500, 0, ThreePhaseConnection.Star, 50);

        xf.Primary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 220_000,
            FrequencyHz = 50
        });
        xf.Secondary.Input = ElectricalPortSnapshot.FromAc(loadIntent);

        xf.Step(new DeviceStepContext(), TimeSpan.FromMilliseconds(100));

        var pri = xf.Primary.Output.Ac!.Internal;
        Assert.InRange(pri.ActivePowerKw, -520, -480);
    }

    [Fact]
    public void GridConnected_noSecondaryLoad_ports_show_no_magnetizing_reactive()
    {
        var xf = new TransformerDevice("main", new TransformerDeviceConfig
        {
            RatedPowerKva = 31_500,
            PrimaryNominalLineVoltageV = 220_000,
            SecondaryNominalLineVoltageV = 35_000,
            NoLoadLossKw = 0.1,
            NoLoadCurrentPercent = 2,
            MagnetizingInrushEnabled = false
        });

        var gridCtx = new DeviceStepContext
        {
            MainBreakerClosed = true,
            UtilityGridAvailable = true
        };

        xf.Primary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 220_000,
            FrequencyHz = 50
        });
        xf.Secondary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 35_000,
            FrequencyHz = 50
        });

        xf.Step(gridCtx, TimeSpan.FromMilliseconds(100));

        var pri = xf.Primary.Output.Ac!.Internal;
        Assert.True(Math.Abs(pri.ReactivePowerKvar) < 1.0);
        Assert.True(Math.Abs(pri.ActivePowerKw) < 1.0);
        Assert.True(xf.GetSecondaryMagnetizingReactiveKvar() > 50,
            "内部仍计算励磁，但不向端口输出");
    }

    [Fact]
    public void Islanded_ports_include_magnetizing_reactive()
    {
        var xf = new TransformerDevice("main", new TransformerDeviceConfig
        {
            RatedPowerKva = 6300,
            PrimaryNominalLineVoltageV = 35_000,
            SecondaryNominalLineVoltageV = 690,
            NoLoadCurrentPercent = 2,
            MagnetizingInrushEnabled = false
        });

        var islandCtx = new DeviceStepContext
        {
            MainBreakerClosed = false,
            UtilityGridAvailable = false
        };

        xf.Primary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 35_000,
            FrequencyHz = 50
        });
        xf.Secondary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 690,
            FrequencyHz = 50
        });

        xf.Step(islandCtx, TimeSpan.FromMilliseconds(100));

        var pri = xf.Primary.Output.Ac!.Internal;
        Assert.True(pri.ReactivePowerKvar > 10, "离网时端口应体现励磁无功");
    }

    [Fact]
    public void Step_positive_reactive_raises_secondary_voltage()
    {
        var xf = CreateMainXf();
        StepWithSecondaryPower(xf, activeKw: 0, reactiveKvar: 0);
        double v0 = xf.Secondary.Output.Ac!.Internal.LineVoltageV;

        StepWithSecondaryPower(xf, activeKw: 0, reactiveKvar: 12_600);
        double vQ = xf.Secondary.Output.Ac!.Internal.LineVoltageV;

        Assert.InRange(v0, 34_500, 35_500);
        Assert.True(vQ > v0 + 200, $"感性支撑应抬高二次电压，空载 {v0:F0} V，Q>0 时 {vQ:F0} V");
    }

    [Fact]
    public void Step_negative_reactive_lowers_secondary_voltage()
    {
        var xf = CreateMainXf();
        StepWithSecondaryPower(xf, activeKw: 0, reactiveKvar: 0);
        double v0 = xf.Secondary.Output.Ac!.Internal.LineVoltageV;

        StepWithSecondaryPower(xf, activeKw: 0, reactiveKvar: -12_600);
        double vQ = xf.Secondary.Output.Ac!.Internal.LineVoltageV;

        Assert.True(vQ < v0 - 200, $"感性吸收应降低二次电压，空载 {v0:F0} V，Q<0 时 {vQ:F0} V");
    }

    private static TransformerDevice CreateMainXf() =>
        new("main", new TransformerDeviceConfig
        {
            RatedPowerKva = 31_500,
            PrimaryNominalLineVoltageV = 220_000,
            SecondaryNominalLineVoltageV = 35_000,
            ImpedancePercent = 4,
            ReactiveVoltageInfluenceCoefficient = 1.0,
            MagnetizingInrushEnabled = false
        });

    private static void StepWithSecondaryPower(TransformerDevice xf, double activeKw, double reactiveKvar)
    {
        xf.Primary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 220_000,
            FrequencyHz = 50
        });
        xf.Secondary.Input = ElectricalPortSnapshot.FromAc(
            AcQuantityConverter.FromLineVoltageAndPower(
                35_000, activeKw, reactiveKvar, ThreePhaseConnection.Star, 50));
        xf.Step(new DeviceStepContext(), TimeSpan.FromMilliseconds(100));
    }
}
