using EssSimulator.Configuration;
using EssSimulator.Core;
using EssSimulator.Display;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;
using EssSimulator.Web;
using Xunit;

namespace EssSimulator.Tests.Web;

public class MainLineEnricherTests : SimulatorHostTestBase
{
    [Fact]
    public void ResolveEssChannelCount_zero_when_pv_only()
    {
        Assert.Equal(0, MainLineEnricher.ResolveEssChannelCount(pcsCount: 0, pvCount: 2));
        Assert.Equal(0, MainLineEnricher.ResolveEssUnitCount(0));
    }

    [Fact]
    public void ResolveEssChannelCount_keeps_pcs_when_mixed_plant()
    {
        Assert.Equal(4, MainLineEnricher.ResolveEssChannelCount(pcsCount: 4, pvCount: 2));
        Assert.Equal(2, MainLineEnricher.ResolveEssUnitCount(4));
    }

    [Fact]
    public void ResolveEssChannelCount_falls_back_to_one_channel_when_plant_empty()
    {
        Assert.Equal(1, MainLineEnricher.ResolveEssChannelCount(pcsCount: 0, pvCount: 0));
        Assert.Equal(1, MainLineEnricher.ResolveEssUnitCount(1));
    }

    [Fact]
    public void EnrichUnit_builds_one_channel_per_slot_beyond_two()
    {
        // 单机组 4 PCS：每个槽位都应产出运行时通道，不再被 A/B 双通道截断
        var cfg = new SimulatorConfig
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
        using var ess = new EnergyStorageSystem(
            cfg,
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
        SimulatorHost.Instance.RegisterEss(ess);

        var channels = new List<PcsChannelSnapshot?>();
        for (int s = 0; s < 4; s++)
            channels.Add(new PcsChannelSnapshot(s, 0, s, default, 0, 0, 690, 0, 50, "GridConnected", "Inactive", false));
        var snap = new MainLineSnapshot
        {
            Units = new List<UnitBranchSnapshot>
            {
                new UnitBranchSnapshot(0, true, false, default, default, null, channels)
            }
        };

        var vm = MainLineEnricher.Build(snap, channelCountOverride: 4);

        var unit = Assert.Single(vm.Units);
        Assert.Equal(4, unit.Channels.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, unit.Channels.Select(c => c.PcsNumber).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, unit.Channels.Select(c => c.EmuUnitNumber).ToArray());
        Assert.All(unit.Channels, c =>
        {
            Assert.Equal("yt0", c.ActivePowerYtPoint);
            Assert.Equal("yt1", c.ReactivePowerYtPoint);
        });
        Assert.Equal(new[] { 0, 1, 2, 3 }, unit.Channels.Select(c => c.SlotInUnit).ToArray());
        Assert.Equal(4, unit.PcsChannels.Count);
    }

    [Fact]
    public void EnrichUnit_copies_emu_meter_and_transformer_mirrors()
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
        using var ess = new EnergyStorageSystem(
            cfg,
            new PcsPhysicalConfig { AcVoltageNominal = 690 },
            new TransformerConfig(),
            new UnitTransformerConfig(),
            new LoadConfig(),
            new PccConfig(),
            new MeterConfig());
        SimulatorHost.Instance.RegisterEss(ess);

        var emu = new EnergyManagementData();
        emu.ElectricityMeter.LineVoltageAB = 690f;
        emu.ElectricityMeter.LineVoltageBC = 691f;
        emu.ElectricityMeter.LineVoltageCA = 692f;
        emu.ElectricityMeter.PhaseACurrent = 10f;
        emu.ElectricityMeter.PhaseBCurrent = 11f;
        emu.ElectricityMeter.PhaseCCurrent = 12f;
        emu.ElectricityMeter.TotalActivePower = 200f;
        emu.ElectricityMeter.TotalReactivePower = 30f;
        emu.ElectricityMeter.PowerFactor = 0.98f;
        emu.ElectricityMeter.Frequency = 50f;
        emu.Transformers.Add(new TransformerMirrorData
        {
            LoadFraction = 0.4f,
            OilTemperatureC = 48f,
            ActivePowerKw = 210f,
            ReactivePowerKvar = 15f
        });
        SimulatorHost.Instance.RegisterEmu(1, emu);

        var channels = new List<PcsChannelSnapshot?>
        {
            new PcsChannelSnapshot(0, 0, 0, default, 0, 0, 690, 0, 50, "GridConnected", "Inactive", false),
            new PcsChannelSnapshot(1, 0, 1, default, 0, 0, 690, 0, 50, "GridConnected", "Inactive", false)
        };
        var snap = new MainLineSnapshot
        {
            Units = new List<UnitBranchSnapshot>
            {
                new UnitBranchSnapshot(0, true, false, default, default, null, channels)
            }
        };

        var vm = MainLineEnricher.Build(snap, channelCountOverride: 2);
        var unit = Assert.Single(vm.Units);
        Assert.Equal(690, unit.UnitMeterThreePhase.LineVoltageAB, 3);
        Assert.Equal(691, unit.UnitMeterThreePhase.LineVoltageBC, 3);
        Assert.Equal(10, unit.UnitMeterThreePhase.PhaseACurrent, 3);
        Assert.Equal(200, unit.UnitMeterActivePowerKw, 3);
        Assert.Equal(30, unit.UnitMeterReactivePowerKvar, 3);
        Assert.Equal(0.98, unit.UnitMeterPowerFactor, 3);
        Assert.Equal(50, unit.UnitMeterFrequencyHz, 3);
        Assert.Equal(0.4, unit.UnitTransformerLoadFraction, 3);
        Assert.Equal(48, unit.UnitTransformerOilTemperatureC, 3);
        Assert.Equal(210, unit.UnitTransformerActivePowerKw, 3);
        Assert.Equal(15, unit.UnitTransformerReactivePowerKvar, 3);
    }

    [Fact]
    public void Build_copies_svg_card_fields()
    {
        var vm = MainLineEnricher.Build(new MainLineSnapshot
        {
            SvgPresent = true,
            SvgRunState = 1,
            SvgRunCommand = 1,
            SvgReactivePowerKvar = 5000,
            SvgReactiveSetpointKvar = 4800,
            SvgAvailableReactiveUpperKvar = 9000,
            SvgRatedCapacityKvar = 10000
        }, channelCountOverride: 0);

        Assert.True(vm.SvgPresent);
        Assert.Equal(1, vm.SvgRunState);
        Assert.Equal(1, vm.SvgRunCommand);
        Assert.Equal(5000, vm.SvgReactivePowerKvar);
        Assert.Equal(4800, vm.SvgReactiveSetpointKvar);
        Assert.Equal(9000, vm.SvgAvailableReactiveUpperKvar);
        Assert.Equal(10000, vm.SvgRatedCapacityKvar);
    }
}
