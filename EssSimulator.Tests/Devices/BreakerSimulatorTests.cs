using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;

namespace EssSimulator.Tests.Devices;

public class BreakerSimulatorTests
{
    [Fact]
    public void ReferToRated_Deenergized_DoesNotFabricateRatedVoltage()
    {
        var breaker = new BreakerSimulator("unit_breaker", new BreakerBranchConfig
        {
            RatedVoltageKv = 35,
            InitialClosed = false
        });

        var referred = breaker.ReferToRated(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 0,
            LineCurrentA = 0,
            FrequencyHz = 0
        });

        Assert.True(referred.LineVoltageV < 2, $"失电入口不应折成额定电压，实际 {referred.LineVoltageV:F1} V");
        Assert.Equal(0, referred.LineCurrentA);
    }

    [Fact]
    public void OpenBreaker_BlocksCurrent_ButKeepsSecondaryVoltage()
    {
        var breaker = new BreakerSimulator("main_breaker", new BreakerBranchConfig { InitialClosed = false });

        breaker.Primary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 220_000,
            FrequencyHz = 50
        });
        breaker.Secondary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 35_000,
            LineCurrentA = 500,
            FrequencyHz = 50
        });

        breaker.Step(new DeviceStepContext(), TimeSpan.FromMilliseconds(200));

        Assert.Equal(0, breaker.Primary.Output.Ac!.Internal.LineCurrentA);
        Assert.Equal(0, breaker.Secondary.Output.Ac!.Internal.LineCurrentA);
        Assert.Equal(35_000, breaker.Secondary.Output.Ac.Internal.LineVoltageV);
    }

    [Fact]
    public void ClosedBreaker_PassesVoltageAndCurrent()
    {
        var breaker = new BreakerSimulator("main_breaker", new BreakerBranchConfig { InitialClosed = true });

        breaker.Primary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 220_000,
            FrequencyHz = 50
        });
        breaker.Secondary.Input = ElectricalPortSnapshot.FromAc(
            AcQuantityConverter.FromLineVoltageAndPower(
                220_000,
                50_000,
                0,
                ThreePhaseConnection.Star,
                50));

        breaker.Step(new DeviceStepContext(), TimeSpan.FromMilliseconds(200));

        Assert.Equal(220_000, breaker.Secondary.Output.Ac!.Internal.LineVoltageV);
        Assert.Equal(131.2, breaker.Secondary.Output.Ac.Internal.LineCurrentA, 0.5);
    }

    [Fact]
    public void UnitFaultThreshold_DefaultIs3500A()
    {
        Assert.Equal(3500, new BreakerConfig().Unit.FaultThresholdA);
        Assert.Equal(35, new BreakerConfig().Unit.RatedVoltageKv);
    }

    [Fact]
    public void UnitBreaker_690VInrushCurrent_ReferredTo35kV_DoesNotTrip()
    {
        var cfg = new BreakerBranchConfig
        {
            InitialClosed = true,
            RatedVoltageKv = 35,
            RatedCurrentA = 3000,
            FaultThresholdA = 3500
        };
        Assert.Equal(3500, cfg.FaultThresholdA);

        var breaker = new BreakerSimulator("unit_breaker", cfg);
        breaker.Primary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 35_000,
            FrequencyHz = 50
        });
        breaker.Secondary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 690,
            LineCurrentA = 63_000,
            FrequencyHz = 50
        });

        breaker.Step(new DeviceStepContext(), TimeSpan.FromMilliseconds(200));

        Assert.False(breaker.SwitchState.IsTripped);
        Assert.True(breaker.SwitchState.IsClosed);
        double i35 = breaker.Secondary.Output.Ac!.Internal.LineCurrentA;
        Assert.InRange(i35, 1_240, 1_250);
        Assert.True(i35 < cfg.FaultThresholdA);
        Assert.True(63_000 > cfg.FaultThresholdA, "未折算的 690 V 电流应超过定值，作为对照");
    }

    [Fact]
    public void UnitBreaker_35kVOvercurrent_StillTrips()
    {
        var breaker = new BreakerSimulator("unit_breaker", new BreakerBranchConfig
        {
            InitialClosed = true,
            RatedVoltageKv = 35,
            RatedCurrentA = 3000,
            FaultThresholdA = 3500
        });
        breaker.Primary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 35_000,
            FrequencyHz = 50
        });
        breaker.Secondary.Input = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
        {
            Connection = ThreePhaseConnection.Star,
            LineVoltageV = 35_000,
            LineCurrentA = 4_000,
            FrequencyHz = 50
        });

        breaker.Step(new DeviceStepContext(), TimeSpan.FromMilliseconds(200));

        Assert.True(breaker.SwitchState.IsTripped);
        Assert.False(breaker.SwitchState.IsClosed);
    }
}
