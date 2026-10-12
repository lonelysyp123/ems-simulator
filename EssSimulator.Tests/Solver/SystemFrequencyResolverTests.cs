using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Solver;

namespace EssSimulator.Tests.Solver;

public class SystemFrequencyResolverTests
{
    [Fact]
    public void Resolve_MainClosedWithGridVoltage_ReturnsGridNominalFrequency()
    {
        var network = BuildNetwork(gridStepMainClosed: true, gridFreq: 49.8);
        var context = BuildContext(mainClosed: true);

        double hz = SystemFrequencyResolver.Resolve(network, context);

        Assert.Equal(49.8, hz, 3);
    }

    [Fact]
    public void Resolve_MainClosedWithoutGridVoltage_ReturnsZero()
    {
        var network = BuildNetwork(gridStepMainClosed: false, gridFreq: 50);
        var context = BuildContext(mainClosed: true);

        Assert.Equal(0, SystemFrequencyResolver.Resolve(network, context));
    }

    [Fact]
    public void Resolve_MainOpenWithoutIslandPcs_ReturnsZero()
    {
        var network = BuildNetwork(gridStepMainClosed: false, gridFreq: 50);
        var context = BuildContext(mainClosed: false);

        Assert.Equal(0, SystemFrequencyResolver.Resolve(network, context));
    }

    private static ElectricalNetwork BuildNetwork(bool gridStepMainClosed, double gridFreq)
    {
        var gridCfg = new GridConfig
        {
            NominalLineVoltageV = 220_000,
            NominalFrequencyHz = gridFreq,
            Connection = ThreePhaseConnection.Star
        };
        var grid = new GridSimulator("grid", gridCfg);
        grid.SetAggregatedReactivePowerKvar(0);
        grid.Step(new DeviceStepContext { MainBreakerClosed = gridStepMainClosed }, TimeSpan.FromMilliseconds(100));
        if (!gridStepMainClosed)
        {
            grid.Port.Output = ElectricalPortSnapshot.FromAc(new AcInternalQuantities
            {
                Connection = ThreePhaseConnection.Star,
                LineVoltageV = 0,
                FrequencyHz = 0
            });
        }

        var mainBreaker = new BreakerSimulator("main", new BreakerBranchConfig { InitialClosed = gridStepMainClosed });

        return new ElectricalNetwork
        {
            Grid = grid,
            MainBreaker = mainBreaker,
            PcsDevices = Array.Empty<PcsDevice>()
        };
    }

    private static DeviceStepContext BuildContext(bool mainClosed) =>
        new() { MainBreakerClosed = mainClosed };
}
