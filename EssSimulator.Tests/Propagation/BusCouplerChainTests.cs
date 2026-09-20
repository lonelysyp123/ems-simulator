using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;
using Xunit;

namespace EssSimulator.Tests.Propagation;

public class BusCouplerChainTests
{
    [Fact]
    public void SetVoltage_on_upstream_notifies_coupler_and_updates_downstream()
    {
        var upstream = new ElectricalBusNode("UP", 220000);
        var downstream = new ElectricalBusNode("DOWN", 35000);
        int callbackCount = 0;

        upstream.RegisterVoltageHandler(args =>
        {
            callbackCount++;
            downstream.SetVoltage(args.LineVoltageV * 0.16, args.FrequencyHz, args.Sweep, notifyCouplers: false);
        });

        var sweep = new PropagationSweepContext
        {
            DeviceContext = new DeviceStepContext(),
            Step = TimeSpan.FromMilliseconds(100),
            Bus35 = downstream,
            PcsCfg = new Configuration.PcsPhysicalConfig(),
            SystemFrequencyHz = 50,
            LastBus35LineVoltageV = 35000,
            StationBusNominalLineVoltageV = 35000,
            MainBreakerClosed = true,
            OpenCircuitBus35LineVoltageV = 35000
        };

        upstream.SetVoltage(220000, 50, sweep);

        Assert.Equal(1, callbackCount);
        Assert.Equal(35200, downstream.LineVoltageV, 0);
    }
}
