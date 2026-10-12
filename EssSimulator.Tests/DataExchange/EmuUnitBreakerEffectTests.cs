using EssSimulator;
using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Effects;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.Tests.DataExchange;

public class EmuUnitBreakerEffectTests : SimulatorHostTestBase
{
    [Fact]
    public void TryResolveEmuUnit_PrefersBindingRootOverServerName()
    {
        var ctx = new ControlEffectContext
        {
            ServerName = "simEmu5",
            AppliedValue = 1,
            PreviousValue = 0,
            Binding = new PointBinding
            {
                Entry = new MapEntry { Address = 1003, FunctionCode = 5, ParamName = "yk3", Size = 1, Type = "bool" },
                ParamName = "yk3",
                Target = new DataTarget { RootKey = "emu1", PropertyPath = "PcsList[4].pcsOnOffSwitch" },
                Effect = ControlEffectId.PcsApplyCommands
            }
        };

        Assert.True(EmuPcsControlEffect.TryResolveEmuUnit(ctx, out int unit));
        Assert.Equal(1, unit);
    }

    [Fact]
    public void OnControlChanged_BreakerClosed_DrivesUnitBreaker_AndSyncsPowerOnOff()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            emu.Breaker.Closed = 0;
            new EmuUnitBreakerEffect().OnControlChanged(Context("Breaker.Closed"));

            Assert.False(ess.IsUnitBreakerClosed(0));
            Assert.Equal(0, emu.Emu.PowerOnOff);
            Assert.Equal(0, emu.Breaker.Closed);
        }
    }

    [Fact]
    public void OnControlChanged_PowerOnOff_StillDrivesUnitBreaker_AndSyncsBreakerClosed()
    {
        var (ess, emu) = Build();
        using (ess)
        {
            emu.Emu.PowerOnOff = 0;
            new EmuUnitBreakerEffect().OnControlChanged(Context("Emu.PowerOnOff"));

            Assert.False(ess.IsUnitBreakerClosed(0));
            Assert.Equal(0, emu.Emu.PowerOnOff);
            Assert.Equal(0, emu.Breaker.Closed);
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
            cfg,
            pcsPhy,
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
        var emu = PcsDataServer.BuildEmuMirror(cfg.Devices[0], pcsPhy);
        SimulatorHost.Instance.RegisterEss(ess);
        SimulatorHost.Instance.RegisterEmu(1, emu);
        return (ess, emu);
    }

    private static ControlEffectContext Context(string propertyPath) => new()
    {
        ServerName = "simEmu1",
        AppliedValue = 0,
        PreviousValue = 1,
        Binding = new PointBinding
        {
            Entry = new MapEntry { Address = 1000, FunctionCode = 5, ParamName = "yx0", Size = 1, Type = "bool" },
            ParamName = "yx0",
            Target = new DataTarget { RootKey = "emu1", PropertyPath = propertyPath },
            Effect = ControlEffectId.UnitHighVoltageBreaker
        }
    };
}
