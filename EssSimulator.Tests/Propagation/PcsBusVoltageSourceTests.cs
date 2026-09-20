using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;
using Xunit;

namespace EssSimulator.Tests.Propagation;

public class PcsBusVoltageSourceTests
{
    private sealed class FixedVoltageSource : IBusVoltageSource
    {
        public string SourceId => "fixed";
        public bool IsInjecting(DeviceStepContext context) => true;
        public (double LineVoltageV, double FrequencyHz) GetInjection(DeviceStepContext context) => (400, 48);
    }

    [Fact]
    public void ApplyLocalVoltageSources_overlays_pcs_injection_on_bus690()
    {
        var bus = new ElectricalBusNode("BUS_690_U0", 690) { LineVoltageV = 0 };
        bus.RegisterVoltageSource(new FixedVoltageSource());

        var sweep = new PropagationSweepContext
        {
            DeviceContext = new DeviceStepContext(),
            Step = TimeSpan.FromMilliseconds(100),
            Bus35 = new ElectricalBusNode("BUS_35", 35000),
            PcsCfg = new Configuration.PcsPhysicalConfig(),
            SystemFrequencyHz = 50,
            LastBus35LineVoltageV = 0,
            StationBusNominalLineVoltageV = 35000,
            MainBreakerClosed = false,
            OpenCircuitBus35LineVoltageV = 0
        };

        Assert.True(bus.ApplyLocalVoltageSources(sweep));
        Assert.Equal(400, bus.LineVoltageV, 0);
        Assert.Equal(48, bus.FrequencyHz, 0);
    }

    [Fact]
    public void ApplyLocalVoltageSources_averages_parallel_forming_injections()
    {
        var bus = new ElectricalBusNode("BUS_690_U0", 690) { LineVoltageV = 0 };
        bus.RegisterVoltageSource(new FixedVoltageSource());
        bus.RegisterVoltageSource(new DualVoltageSource());

        var sweep = new PropagationSweepContext
        {
            DeviceContext = new DeviceStepContext(),
            Step = TimeSpan.FromMilliseconds(100),
            Bus35 = new ElectricalBusNode("BUS_35", 35000),
            PcsCfg = new Configuration.PcsPhysicalConfig(),
            SystemFrequencyHz = 50,
            LastBus35LineVoltageV = 0,
            StationBusNominalLineVoltageV = 35000,
            MainBreakerClosed = false,
            OpenCircuitBus35LineVoltageV = 0
        };

        Assert.True(bus.ApplyLocalVoltageSources(sweep));
        Assert.Equal(500, bus.LineVoltageV, 0);
        Assert.Equal(50, bus.FrequencyHz, 0);
    }

    [Fact]
    public void ApplyLocalVoltageSources_KernelOwned_DoesNotAverage()
    {
        var bus = new ElectricalBusNode("BUS_690_U0", 690)
        {
            LineVoltageV = 123,
            FrequencyHz = 47,
            KernelOwned = true
        };
        bus.RegisterVoltageSource(new FixedVoltageSource());
        bus.RegisterVoltageSource(new DualVoltageSource());

        var sweep = new PropagationSweepContext
        {
            DeviceContext = new DeviceStepContext(),
            Step = TimeSpan.FromMilliseconds(100),
            Bus35 = new ElectricalBusNode("BUS_35", 35000),
            PcsCfg = new Configuration.PcsPhysicalConfig(),
            SystemFrequencyHz = 50,
            LastBus35LineVoltageV = 0,
            StationBusNominalLineVoltageV = 35000,
            MainBreakerClosed = false,
            OpenCircuitBus35LineVoltageV = 0
        };

        Assert.False(bus.ApplyLocalVoltageSources(sweep));
        Assert.Equal(123, bus.LineVoltageV, 0);
        Assert.Equal(47, bus.FrequencyHz, 0);
    }

    private sealed class DualVoltageSource : IBusVoltageSource
    {
        public string SourceId => "dual";
        public bool IsInjecting(DeviceStepContext context) => true;
        public (double LineVoltageV, double FrequencyHz) GetInjection(DeviceStepContext context) => (600, 52);
    }
}
