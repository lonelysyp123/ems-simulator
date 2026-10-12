using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Propagation;
using Xunit;

namespace EssSimulator.Tests.Propagation;

public class ElectricalBusNodeTests
{
    private sealed class FixedContributor : IBusPowerContributor
    {
        private readonly BusPowerContribution _value;
        public FixedContributor(string id, BusPowerContribution value)
        {
            ContributorId = id;
            _value = value;
        }

        public string ContributorId { get; }
        public BusPowerContribution GetBusPowerContribution(DeviceStepContext context) => _value;
    }

    [Fact]
    public void Constructor_StartsDeenergized()
    {
        var bus = new ElectricalBusNode("BUS_35", 35000);

        Assert.Equal(35000, bus.NominalLineVoltageV);
        Assert.True(bus.LineVoltageV < 2, $"新建母线电压应为 0，实际 {bus.LineVoltageV:F1} V");
        Assert.Equal(0, bus.FrequencyHz);
    }

    [Fact]
    public void CollectFromContributors_sums_power_from_registered_devices()
    {
        var bus = new ElectricalBusNode("BUS_35", 35000);
        bus.RegisterContributor(new FixedContributor("load", new BusPowerContribution(-100, 20)));
        bus.RegisterContributor(new FixedContributor("pcs", new BusPowerContribution(200, -10)));

        bus.CollectFromContributors(new DeviceStepContext());

        Assert.Equal(100, bus.TotalActivePowerKw, 0.01);
        Assert.Equal(10, bus.TotalReactivePowerKvar, 0.01);
    }

    [Fact]
    public void ToCurrentIntent_uses_local_voltage_not_upstream()
    {
        var bus = new ElectricalBusNode("BUS_690", 690) { LineVoltageV = 690 };
        bus.AddPower(100, 0);

        var intent = bus.ToCurrentIntent();

        Assert.True(intent.LineCurrentA > 0);
        Assert.Equal(690, intent.LineVoltageV);
    }
}
