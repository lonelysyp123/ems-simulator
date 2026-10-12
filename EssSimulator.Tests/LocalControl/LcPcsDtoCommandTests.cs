using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi;
using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

/// <summary>
/// LC 组片段在协议从站离线时回退写 DTO；必须走命令链才能驱动物理 PCS。
/// </summary>
public class LcPcsDtoCommandTests : SimulatorHostTestBase
{
    [Fact]
    public void WriteAndApply_StartStop_ThirdPcs_LeavesOff()
    {
        var (ess, emu) = BuildFourPcsUnit();
        using (ess)
        {
            var pcs3 = ess._pcsList[2];
            ess.SetMainBreakerClosed(true);
            ess.SetUnitBreakerClosed(0, true);
            foreach (var bms in ess._bmsRackDevices)
                bms.IsLinked = true;

            var simTime = DateTime.UtcNow;
            var step = TimeSpan.FromMilliseconds(200);
            for (int i = 0; i < 3; i++)
            {
                simTime += step;
                ess.PlantEngine.Step(simTime, step, step);
            }

            Assert.True(LcPcsDtoCommand.WriteAndApply(1, 2, "pcsOnOffSwitch", true));
            Assert.True(emu.PcsList[2].pcsOnOffSwitch);
            Assert.NotEqual(OperationMode.Off, pcs3.GetCurrentState().Mode);
        }
    }

    [Fact]
    public void WriteDtoAlone_ThirdPcs_DoesNotStartDevice()
    {
        var (ess, emu) = BuildFourPcsUnit();
        using (ess)
        {
            var pcs3 = ess._pcsList[2];
            ess.SetMainBreakerClosed(true);
            ess.SetUnitBreakerClosed(0, true);
            foreach (var bms in ess._bmsRackDevices)
                bms.IsLinked = true;

            Assert.True(SimServer.SetExtIfVariableVal("emu1.PcsList[2].pcsOnOffSwitch", true));
            Assert.True(emu.PcsList[2].pcsOnOffSwitch);
            Assert.Equal(OperationMode.Off, pcs3.GetCurrentState().Mode);
        }
    }

    private static (EnergyStorageSystem ess, EssSimulator.EssSimModelApi.EnergyManagementSystem.EnergyManagementData emu)
        BuildFourPcsUnit()
    {
        var cfg = new SimulatorConfig
        {
            Devices =
            {
                new EssUnitConfig
                {
                    Groups =
                    {
                        new EmuGroupConfig
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
}
