using EssSimulator.Configuration;
using EssSimulator.DataExchange.Catalog;

namespace EssSimulator.Tests.DataExchange;

/// <summary>EMU 点表绑定按机组构成门控：只能指向机组内实际存在的设备模型。</summary>
public class EmuDeviceCatalogFilterTests
{
    private static EssUnitConfig Unit(int pcsCount, bool breaker, bool meter)
    {
        var u = new EssUnitConfig { HasUnitBreaker = breaker, HasUnitMeter = meter };
        for (int i = 0; i < pcsCount; i++)
            u.Pcs.Add(new PcsDeviceConfig());
        return u;
    }

    /// <summary>分组机组：组0 含 2 台 PCS + 组断路器 + 2 台组电表；组1 含 1 台 PCS 无断路器。</summary>
    private static EssUnitConfig GroupedUnit()
    {
        var u = new EssUnitConfig { HasUnitBreaker = false, HasUnitMeter = false };
        u.Groups.Add(new EmuGroupConfig
        {
            Name = "G1",
            Pcs = { new PcsDeviceConfig(), new PcsDeviceConfig() },
            BreakerName = "组断路器-1",
            MeterNames = { "组电表-1", "组电表-2" }
        });
        u.Groups.Add(new EmuGroupConfig { Name = "G2", Pcs = { new PcsDeviceConfig() } });
        return u;
    }

    [Fact]
    public void Create_ActivatesOnlyForEmuLikeDevicesWithUnits()
    {
        var units = new[] { Unit(2, true, true) };
        Assert.NotNull(EmuDeviceCatalogFilter.Create("simEmu1", units));
        Assert.NotNull(EmuDeviceCatalogFilter.Create("simLc1", units));
        // 非 EMU 同构设备不门控
        Assert.Null(EmuDeviceCatalogFilter.Create("simBms1", units));
        Assert.Null(EmuDeviceCatalogFilter.Create("simEm", units));
        // legacy 配置（无机组构成）不门控，保持现状
        Assert.Null(EmuDeviceCatalogFilter.Create("simEmu1", null));
        Assert.Null(EmuDeviceCatalogFilter.Create("simEmu1", Array.Empty<EssUnitConfig>()));
    }

    [Fact]
    public void Allows_GatesPathsByUnitComposition()
    {
        var filter = EmuDeviceCatalogFilter.Create("simEmu1", new[]
        {
            Unit(pcsCount: 2, breaker: true, meter: false),
            Unit(pcsCount: 1, breaker: false, meter: true)
        })!;

        // 根必须是 emuK 机组根路径
        Assert.False(filter.Allows("bms1.Soc"));
        Assert.False(filter.Allows("emu3.PcsList[0].X"));
        Assert.False(filter.Allows(null));

        // PcsList[i] 要求 i 小于该机组 PCS 台数
        Assert.True(filter.Allows("emu1.PcsList[0].pcsOnOffSwitch"));
        Assert.True(filter.Allows("emu1.PcsList[1].pcsOnOffSwitch"));
        Assert.False(filter.Allows("emu1.PcsList[2].pcsOnOffSwitch"));
        Assert.False(filter.Allows("emu2.PcsList[1].pcsOnOffSwitch"));

        // ElectricityMeter 要求该机组下属存在电表（直绑单元或任一分组）
        Assert.False(filter.Allows("emu1.ElectricityMeter.Uab"));
        Assert.True(filter.Allows("emu2.ElectricityMeter.Uab"));

        // Emu.PowerOnOff（单元高压断路器开合）要求该机组绑定断路器
        Assert.True(filter.Allows("emu1.Emu.PowerOnOff"));
        Assert.False(filter.Allows("emu2.Emu.PowerOnOff"));

        // 其余 Emu.* 为单元虚拟模型，恒允许
        Assert.True(filter.Allows("emu2.Emu.TargetActivePower"));
        Assert.True(filter.Allows("emu1.Emu.SystemOperation"));
    }

    [Fact]
    public void Allows_GatesGroupAndMirrorPathsByComposition()
    {
        // 机组1：分组构成（2 组）；机组2：扁平构成（无 Groups）
        var filter = EmuDeviceCatalogFilter.Create("simEmu1", new[] { GroupedUnit(), Unit(2, breaker: true, meter: true) })!;

        // Groups[g].PcsList[i]：g 须有效且 i 小于组内 PCS 台数；与扁平视图互不影响
        Assert.True(filter.Allows("emu1.Groups[0].PcsList[0].pcsOnOffSwitch"));
        Assert.True(filter.Allows("emu1.Groups[0].PcsList[1].P"));
        Assert.False(filter.Allows("emu1.Groups[0].PcsList[2].P"));
        Assert.True(filter.Allows("emu1.Groups[1].PcsList[0].P"));
        Assert.False(filter.Allows("emu1.Groups[1].PcsList[1].P"));
        Assert.False(filter.Allows("emu1.Groups[2].PcsList[0].P"));
        Assert.True(filter.Allows("emu1.PcsList[2].P")); // 扁平视图按机组总数 3 门控

        // Groups[g].Breaker：仅绑定组断路器的组允许
        Assert.True(filter.Allows("emu1.Groups[0].Breaker.Closed"));
        Assert.False(filter.Allows("emu1.Groups[1].Breaker.Closed"));

        // 组聚合遥测：组索引有效即允许
        Assert.True(filter.Allows("emu1.Groups[0].TotalActivePower"));
        Assert.True(filter.Allows("emu1.Groups[1].TotalActivePower"));
        Assert.False(filter.Allows("emu1.Groups[2].TotalActivePower"));

        // Transformers[k]：本期仅 k=0（单元变）
        Assert.True(filter.Allows("emu1.Transformers[0].LoadFraction"));
        Assert.False(filter.Allows("emu1.Transformers[1].LoadFraction"));
        Assert.True(filter.Allows("emu2.Transformers[0].OilTemperatureC"));

        // EMU 级 Breaker.*：机组下属存在断路器即允许（emu1 组断路器、emu2 直绑）
        Assert.True(filter.Allows("emu1.Breaker.Closed"));
        Assert.True(filter.Allows("emu2.Breaker.Closed"));

        // ElectricityMeter：机组未直绑单元电表但组0 绑定了组电表 → 视为单元下属电表，允许
        Assert.True(filter.Allows("emu1.ElectricityMeter.LineVoltageAB"));
        Assert.True(filter.Allows("emu2.ElectricityMeter.LineVoltageAB"));

        // Groups[g].Meters[k]：按组内绑定电表台数门控（组0 两台，组1 零台）
        Assert.True(filter.Allows("emu1.Groups[0].Meters[0].PhaseACurrent"));
        Assert.True(filter.Allows("emu1.Groups[0].Meters[1].TotalActivePower"));
        Assert.False(filter.Allows("emu1.Groups[0].Meters[2].PhaseACurrent"));
        Assert.False(filter.Allows("emu1.Groups[1].Meters[0].PhaseACurrent"));

        // 扁平机组：隐式组 0（与 PcsDataServer 镜像一致），组 1 仍拒绝
        Assert.True(filter.Allows("emu2.Groups[0].PcsList[0].P"));
        Assert.True(filter.Allows("emu2.Groups[0].TotalActivePower"));
        Assert.False(filter.Allows("emu2.Groups[1].PcsList[0].P"));
    }

    [Fact]
    public void FilterPaths_DropsWholePointWhenFewerThanTwoPathsRemain()
    {
        var filter = EmuDeviceCatalogFilter.Create("simEmu1", new[] { Unit(1, breaker: true, meter: false) })!;

        // 过滤后仅剩 1 条：sum/max 非法绑定，整点剔除
        Assert.Null(filter.FilterPaths(new[] { "emu1.PcsList[0].P", "emu1.PcsList[1].P" }));
        // 全部无效：整点剔除
        Assert.Null(filter.FilterPaths(new[] { "emu1.ElectricityMeter.A", "emu1.ElectricityMeter.B" }));
        // 保留两条及以上有效路径
        var kept = filter.FilterPaths(new[]
        {
            "emu1.PcsList[0].ActivePower",
            "emu1.PcsList[1].ActivePower",
            "emu1.Emu.TargetActivePower"
        });
        Assert.NotNull(kept);
        Assert.Equal(new[] { "emu1.PcsList[0].ActivePower", "emu1.Emu.TargetActivePower" }, kept);
    }
}
