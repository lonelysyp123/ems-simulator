using EssSimulator.Configuration;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Solver;

namespace EssSimulator.Tests.Solver;

public class NetworkSolverTests
{
    private static ElectricalNetwork CreateDefaultNetwork()
    {
        var simCfg = new SimulatorConfig
        {
            Devices = { new EssUnitConfig() }
        };
        var pccCfg = new PccConfig();
        var pcsCfg = new PcsPhysicalConfig();
        var network = NetworkTopologyBuilder.Build(
            simCfg,
            pcsCfg,
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            pccCfg);
        network.Solver = new NetworkSolver(network, pccCfg, pcsCfg);
        return network;
    }

    [Fact]
    public void Build_DoesNotAttachSolver()
    {
        var network = NetworkTopologyBuilder.Build(
            new SimulatorConfig { Devices = { new EssUnitConfig() } },
            new PcsPhysicalConfig(),
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig());
        Assert.Null(network.Solver);
    }

    [Fact]
    public void MainBreakerClosed_PccVoltageNearNominal()
    {
        var network = CreateDefaultNetwork();
        network.MainBreaker.ApplyCommand(new DeviceCommand { Kind = DeviceCommandKind.CloseBreaker });
        foreach (var ub in network.UnitBreakers)
            ub.ApplyCommand(new DeviceCommand { Kind = DeviceCommandKind.CloseBreaker });

        var step = TimeSpan.FromMilliseconds(200);
        network.Solver!.Step(step, step);

        Assert.InRange(network.PccLineVoltageV, 210_000, 230_000);
        Assert.InRange(network.StationBus35LineVoltageV, 33_000, 37_000);
    }

    [Fact]
    public void MainBreakerOpen_PccVoltageZero()
    {
        var network = CreateDefaultNetwork();
        network.MainBreaker.ApplyCommand(new DeviceCommand { Kind = DeviceCommandKind.OpenBreaker });

        var step = TimeSpan.FromMilliseconds(200);
        network.Solver!.Step(step, step);

        Assert.Equal(0, network.PccLineVoltageV);
    }

    [Fact]
    public void MainBreakerOpen_PccMeterReportsZeroPower()
    {
        var network = CreateDefaultNetwork();
        network.MainBreaker.ApplyCommand(new DeviceCommand { Kind = DeviceCommandKind.OpenBreaker });

        var step = TimeSpan.FromMilliseconds(200);
        network.Solver!.Step(step, step);

        Assert.Equal(0, network.PccMeter.Telemetry.Primary.ActivePowerKw);
        Assert.Equal(0, network.PccMeter.Telemetry.Primary.ReactivePowerKvar);
        Assert.Equal(0, network.PccMeter.Telemetry.Primary.LineVoltageV);
    }

    [Fact]
    public void UnitWithFourPcs_GridStateReachesAllChannels()
    {
        // 回归：修复前 SolveUnitBranches 只步进每单元通道 0/1，第三槽位及以后收不到网侧状态
        var simCfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs =
                    {
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig()
                    }
                }
            }
        };

        var pccCfg = new PccConfig();
        var pcsCfg = new PcsPhysicalConfig();
        var network = NetworkTopologyBuilder.Build(
            simCfg,
            pcsCfg,
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            pccCfg,
            pcsPerUnit: simCfg.GetPcsCountsPerUnit());
        network.Solver = new NetworkSolver(network, pccCfg, pcsCfg);

        Assert.Equal(new[] { 4 }, network.PcsPerUnit);
        Assert.Equal(4, network.PcsDevices.Count);

        network.MainBreaker.ApplyCommand(new DeviceCommand { Kind = DeviceCommandKind.CloseBreaker });
        foreach (var ub in network.UnitBreakers)
            ub.ApplyCommand(new DeviceCommand { Kind = DeviceCommandKind.CloseBreaker });

        var step = TimeSpan.FromMilliseconds(200);
        network.Solver!.Step(step, step);

        for (int i = 0; i < network.PcsDevices.Count; i++)
            Assert.True(network.PcsDevices[i].IsGridElectricallyAvailable, $"pcs{i + 1} 未收到网侧可用状态");
    }
}
