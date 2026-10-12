using EssSimulator.Configuration;

namespace EssSimulator.Tests;

public class SimulatorConfigTests
{
    [Fact]
    public void EmptyDevices_WithoutPv_FallsBackToOneEssUnit()
    {
        var cfg = new SimulatorConfig();
        Assert.Equal(1, cfg.EffectiveEssUnitCount);
        Assert.Equal(2, cfg.UnitCount);
        Assert.Equal(2, cfg.GetBmsDeviceConfigs().Count);
        Assert.Equal(2, cfg.GetPcsDeviceConfigs().Count);
    }

    [Fact]
    public void PvOnly_DoesNotFabricateEssUnits()
    {
        var cfg = new SimulatorConfig
        {
            PvUnits = { new PvUnitRuntimeConfig { Name = "光伏单元-1" } }
        };
        Assert.Equal(0, cfg.EffectiveEssUnitCount);
        Assert.Equal(0, cfg.UnitCount);
        Assert.Equal(1, cfg.PvUnitCount);
        Assert.Empty(cfg.GetBmsDeviceConfigs());
        Assert.Empty(cfg.GetPcsDeviceConfigs());
    }

    [Fact]
    public void MixedPlant_KeepsEssAndPvCounts()
    {
        var cfg = new SimulatorConfig
        {
            Devices = { new EssUnitConfig { Name = "Unit-A" } },
            PvUnits = { new PvUnitRuntimeConfig { Name = "光伏单元-1" } }
        };
        Assert.Equal(1, cfg.EffectiveEssUnitCount);
        Assert.Equal(2, cfg.UnitCount);
        Assert.Equal(1, cfg.PvUnitCount);
        Assert.Equal(2, cfg.GetBmsDeviceConfigs().Count);
    }

    /// <summary>分组构成：PcsCount 为各组之和，PCS/BMS 按 group 顺序展平，BMS 组内按位对齐补齐。</summary>
    [Fact]
    public void GroupedUnit_FlattensByGroupOrder()
    {
        var unit = new EssUnitConfig
        {
            Name = "EMU-1",
            Groups =
            {
                new EmuGroupConfig
                {
                    Name = "G1",
                    Pcs = { new PcsDeviceConfig { Name = "P1" }, new PcsDeviceConfig { Name = "P2" } },
                    Bms = { new BmsDeviceConfig { ClusterCount = 8 } }
                },
                new EmuGroupConfig
                {
                    Name = "G2",
                    Pcs = { new PcsDeviceConfig { Name = "P3" } }
                }
            }
        };
        var cfg = new SimulatorConfig { Devices = { unit } };

        Assert.True(unit.HasGroups);
        Assert.Equal(3, unit.PcsCount);
        Assert.Equal(new[] { 3 }, cfg.GetPcsCountsPerUnit().ToArray());
        Assert.Equal(new[] { "P1", "P2", "P3" },
            cfg.GetPcsDeviceConfigs().Select(p => p.Name).ToArray());
        // BMS 与组内 PCS 按位对齐，缺位用默认配置补齐
        var bms = cfg.GetBmsDeviceConfigs();
        Assert.Equal(3, bms.Count);
        Assert.Equal(8, bms[0].ClusterCount);
    }

    /// <summary>扁平构成（无 Groups）保持现状：未配置 Pcs 时回退 2 台。</summary>
    [Fact]
    public void FlatUnit_KeepsLegacyFlatten()
    {
        var unit = new EssUnitConfig { Name = "EMU-1" };
        var cfg = new SimulatorConfig { Devices = { unit } };

        Assert.False(unit.HasGroups);
        Assert.Equal(2, unit.PcsCount);
        Assert.Equal(2, cfg.GetPcsDeviceConfigs().Count);
        Assert.Equal(2, cfg.GetBmsDeviceConfigs().Count);

        // 显式 Pcs 列表优先于默认 2
        unit.Pcs.Add(new PcsDeviceConfig());
        Assert.Equal(1, unit.PcsCount);
        Assert.Single(cfg.GetPcsDeviceConfigs());
    }
}
