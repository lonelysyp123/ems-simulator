using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EmsStrategy.Adapter;
using EssSimulator.EmsStrategy.Application;
using EssSimulator.EmsStrategy.Domain;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.Web.ThirdPartyEms;

namespace EssSimulator.Tests.EmsStrategy;

public class CompositeAfterPlantStepTests
{
    [Fact]
    public void InvokesInOrder()
    {
        var log = new List<string>();
        var composite = new CompositeAfterPlantStep(
            new RecordingStep("a", log),
            new RecordingStep("b", log));
        composite.AfterPlantStep(null!, default, TimeSpan.Zero);
        Assert.Equal(new[] { "a", "b" }, log);
    }

    private sealed class RecordingStep : IAfterPlantStep
    {
        private readonly string _name;
        private readonly List<string> _log;
        public RecordingStep(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public void AfterPlantStep(EnergyStorageSystem ess, DateTime simTime, TimeSpan elapsed) =>
            _log.Add(_name);
    }
}

public class ExternalControlGateOwnerTests : SimulatorHostTestBase
{
    [Fact]
    public void StrategyOccupy_BlocksThirdPartyAndDispatch()
    {
        Assert.True(ExternalControlGate.TryOccupy(ExternalControlOwner.EmsStrategy, out _));
        Assert.Equal(ExternalControlGate.StrategyBlockedMessage, ExternalControlGate.CurrentMessage);
        Assert.False(ExternalControlGate.TryAllow(out var msg));
        Assert.Equal(ExternalControlGate.StrategyBlockedMessage, msg);

        using var session = new ThirdPartyEmsSession(() => new[]
        {
            new ThirdPartyEmsTarget { Name = "emu1", UnitIndex = 1 }
        });
        SimulatorHost.Instance.RegisterEmu(1, new EnergyManagementData());
        Assert.False(session.TryConnect("emu1", out var error));
        Assert.Equal(ExternalControlGate.StrategyBlockedMessage, error);

        var emu = new EnergyManagementData();
        emu.PcsList.Add(new PcsData());
        emu.Emu.RemoteControlEnable = 1;
        emu.Emu.RemoteControlMode = 1;
        emu.Emu.TargetActivePower = 100;
        EmuPowerDispatcher.Dispatch(emu);
        Assert.Equal(0, emu.PcsList[0].PCSActivePowerSetting);
    }

    [Fact]
    public void ThirdPartyOccupy_BlocksStrategyEnable()
    {
        ExternalControlGate.SetBlocked(true);
        var runtime = new EmsStrategyRuntime(new EmsStrategyEngine(), EmsStrategyConfig.CreateDefault());
        Assert.False(runtime.TrySetEnabled(true, out var error));
        Assert.Equal(ExternalControlGate.ThirdPartyBlockedMessage, error);
    }
}

public class EmsStrategyPlantAdapterTests : SimulatorHostTestBase
{
    [Fact]
    public void Disabled_DoesNotWritePcsSetpoints()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            emu.PcsList[0].PCSActivePowerSetting = 12;
            var runtime = new EmsStrategyRuntime(new EmsStrategyEngine(), EmsStrategyConfig.CreateDefault());
            var adapter = new EmsStrategyPlantAdapter(runtime);
            adapter.AfterPlantStep(ess, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));
            Assert.Equal(12, emu.PcsList[0].PCSActivePowerSetting);
            Assert.False(ExternalControlGate.IsBlocked);
        }
    }

    [Fact]
    public void Enabled_WritesBranchSetpoints_OnFrequencyDeviation()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            ess._pcsList[0].SyncExternalRunCommand(true);
            ess._pcsList[1].SyncExternalRunCommand(true);
            ess.ElectricalNetwork.Grid.SetNominalFrequency(49.7);
            ess.PlantEngine.Step(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
            ess._pcsList[0].SyncExternalRunCommand(true);
            ess._pcsList[1].SyncExternalRunCommand(true);

            var cfg = EmsStrategyConfig.CreateDefault();
            cfg.Enabled = true;
            cfg.ActiveMode = ActiveMode.OpenLoopFixed;
            cfg.Slope.Enabled = false;
            cfg.LocalActiveSetKw = 200;
            var runtime = new EmsStrategyRuntime(new EmsStrategyEngine(), cfg);
            Assert.True(ExternalControlGate.IsBlocked);
            var adapter = new EmsStrategyPlantAdapter(runtime);
            adapter.AfterPlantStep(ess, DateTime.UtcNow, TimeSpan.FromMilliseconds(200));
            Assert.Equal(200, emu.PcsList.Sum(p => p.PCSActivePowerSetting), 1);
        }
    }

    private static (EnergyStorageSystem ess, EnergyManagementData emu) Build()
    {
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs = { new EssSimulator.Configuration.PcsDeviceConfig(), new EssSimulator.Configuration.PcsDeviceConfig() }
                }
            }
        };
        var pcsPhy = new PcsPhysicalConfig { AcVoltageNominal = 690 };
        var ess = new EnergyStorageSystem(
            cfg, pcsPhy, new TransformerConfig(), new UnitTransformerConfig(),
            new LoadConfig(), new PccConfig(), new MeterConfig());
        var emu = PcsDataServer.BuildEmuMirror(cfg.Devices[0], pcsPhy);
        SimulatorHost.Instance.RegisterEss(ess);
        SimulatorHost.Instance.RegisterEmu(1, emu);
        return (ess, emu);
    }
}
