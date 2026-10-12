using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.Tests.Emu;

public class PcsEmuSynchronizerTests : SimulatorHostTestBase
{
    private static (EnergyStorageSystem ess, EnergyManagementData emu) Build()
    {
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Pcs =
                    {
                        new EssSimulator.Configuration.PcsDeviceConfig(),
                        new EssSimulator.Configuration.PcsDeviceConfig()
                    }
                }
            }
        };
        var pcsPhy = new PcsPhysicalConfig { AcVoltageNominal = 690 };
        var ess = new EnergyStorageSystem(
            cfg, pcsPhy,
            new TransformerConfig(), new UnitTransformerConfig(),
            new LoadConfig(), new PccConfig(), new MeterConfig());
        var emu = PcsDataServer.BuildEmuMirror(cfg.Devices[0], pcsPhy);
        SimulatorHost.Instance.RegisterEss(ess);
        SimulatorHost.Instance.RegisterEmu(1, emu);
        return (ess, emu);
    }

    [Fact]
    public void SyncUnit_SecondTickWithoutControlChange_DoesNotOverwritePendingSetpoint()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            ess.SetMainBreakerClosed(true);
            ess.SetUnitBreakerClosed(0, true);
            var pcs = ess._pcsList[0];
            pcs.SetGridAvailable(true);

            Assert.True(DeviceControlFacade.TrySetPcsRun(1, true, out _));
            Assert.True(DeviceControlFacade.TrySetPcsPower(1, 500, 0, out _));
            Assert.Equal(500, pcs.PendingActiveSetpoint, 3);

            PcsEmuSynchronizer.SyncUnit(ess, emu, 0, 0);
            pcs.SetPowerCommand(111, 0);
            Assert.Equal(111, pcs.PendingActiveSetpoint, 3);

            PcsEmuSynchronizer.SyncUnit(ess, emu, 0, 0);
            Assert.Equal(111, pcs.PendingActiveSetpoint, 3);
        }
    }

    [Fact]
    public void SyncUnit_PowerSettingChange_ReappliesCommand()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            ess.SetMainBreakerClosed(true);
            ess.SetUnitBreakerClosed(0, true);
            var pcs = ess._pcsList[0];
            pcs.SetGridAvailable(true);

            Assert.True(DeviceControlFacade.TrySetPcsRun(1, true, out _));
            Assert.True(DeviceControlFacade.TrySetPcsPower(1, 500, 0, out _));
            Assert.Equal(500, pcs.PendingActiveSetpoint, 3);

            emu.PcsList[0].PCSActivePowerSetting = 600;
            PcsEmuSynchronizer.SyncUnit(ess, emu, 0, 0);
            Assert.Equal(600, pcs.PendingActiveSetpoint, 3);
        }
    }

    [Fact]
    public void PlantEngine_Step_ThenEmuDtoMatchesDeviceActivePower()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            var hook = new SyncUnitAfterPlantStep(emu);
            AfterPlantStep.Current = hook;
            try
            {
                ess.SetMainBreakerClosed(true);
                ess.SetUnitBreakerClosed(0, true);
                var pcs = ess._pcsList[0];
                pcs.SetGridAvailable(true);
                Assert.True(DeviceControlFacade.TrySetPcsRun(1, true, out _));
                Assert.True(DeviceControlFacade.TrySetPcsPower(1, 500, 0, out _));

                ess.PlantEngine.Step(DateTime.UtcNow, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));

                Assert.Equal(pcs.GetCurrentState().ActivePower, emu.PcsList[0].ActivePower, 3);
            }
            finally
            {
                hook.Detach();
            }
        }
    }

    private sealed class SyncUnitAfterPlantStep : IAfterPlantStep
    {
        private readonly EnergyManagementData _emu;
        public SyncUnitAfterPlantStep(EnergyManagementData emu) => _emu = emu;
        public void AfterPlantStep(EnergyStorageSystem ess, DateTime simTime, TimeSpan elapsed) =>
            PcsEmuSynchronizer.SyncUnit(ess, _emu, 0, 0);

        public void Detach()
        {
            if (ReferenceEquals(EssDeviceSimModel.AfterPlantStep.Current, this))
                EssDeviceSimModel.AfterPlantStep.Reset();
        }
    }
}
