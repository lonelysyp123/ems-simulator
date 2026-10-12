using EssSimulator.DataExchange.Adapters;
using EssSimulator.DataExchange.Catalog;
using EssSimulator.DataExchange.Config;
using EssSimulator.DataExchange.Pipeline;
using EssSimulator.DataExchange.Plugins;
using EssSimulator.Protocol.Modbus;

namespace EssSimulator.Tests.DataExchange;

/// <summary>EMU 故障字设备插件层：组字规则、注册表、目录加载与遥测管线接入。</summary>
public class TelemetryPluginTests
{
    private const string Root = "emu1.PcsList[0]";

    [Fact]
    public void Compute_WarningWord1_CombinesSupportedFaults()
    {
        var sim = new FakeSimulation
        {
            [$"{Root}.InsulationAlarm"] = true,
            [$"{Root}.DcSurgeProtectorAbnormal"] = true
        };
        var plugin = new TrinaEmuFaultWordPlugin();

        // Bit0/Bit1 总线绝缘低（同源 InsulationAlarm）+ Bit10 DC SPD
        Assert.Equal((1 << 0) | (1 << 1) | (1 << 10), plugin.Compute("ModuleWarningWord1", Root, sim));
    }

    [Fact]
    public void Compute_WarningWord1_DriveAndSpdBits()
    {
        var sim = new FakeSimulation
        {
            [$"{Root}.InternalOverTemp"] = true,
            [$"{Root}.DriveFault"] = true,
            [$"{Root}.AcSurgeProtectorAbnormal"] = true
        };
        var plugin = new TrinaEmuFaultWordPlugin();

        // Bit5 J板过温 + Bit6/7/8 驱动线松动 + Bit12 AC SPD
        Assert.Equal((1 << 5) | (1 << 6) | (1 << 7) | (1 << 8) | (1 << 12),
            plugin.Compute("ModuleWarningWord1", Root, sim));
    }

    [Fact]
    public void Compute_WarningWord1_NoFaultsAndUnsupportedBits_ReturnsZero()
    {
        var sim = new FakeSimulation();
        var plugin = new TrinaEmuFaultWordPlugin();

        Assert.Equal(0, plugin.Compute("ModuleWarningWord1", Root, sim));

        // 置位无协议位对应的仿真故障（湿度等位仿真不支持），字值仍为 0
        sim[$"{Root}.GridOverVoltage"] = true;
        Assert.Equal(0, plugin.Compute("ModuleWarningWord1", Root, sim));
    }

    [Fact]
    public void Compute_WarningWord2_CbcAndFanBits()
    {
        var plugin = new TrinaEmuFaultWordPlugin();

        var sim = new FakeSimulation { [$"{Root}.InverterSoftwareOverCurrent"] = true };
        Assert.Equal(0b00111, plugin.Compute("ModuleWarningWord2", Root, sim)); // Bit0/1/2 CBC

        sim = new FakeSimulation { [$"{Root}.AcFanAbnormal"] = true };
        Assert.Equal(1 << 4, plugin.Compute("ModuleWarningWord2", Root, sim));  // Bit4 风扇欠流
    }

    [Fact]
    public void Compute_MissingPath_TreatedAsZero()
    {
        // deviceRoot 指向不存在的设备路径：全部按 0 处理，不抛异常
        var plugin = new TrinaEmuFaultWordPlugin();
        Assert.Equal(0, plugin.Compute("ModuleWarningWord1", "emu9.PcsList[0]", new FakeSimulation()));
    }

    [Fact]
    public void CanHandle_UnknownWordKey_ReturnsNull()
    {
        var plugin = new TrinaEmuFaultWordPlugin();
        Assert.False(plugin.CanHandle("SystemFaultWord1"));
        Assert.Null(plugin.Compute("SystemFaultWord1", Root, new FakeSimulation()));
    }

    [Fact]
    public void Compute_SystemFaultSummary_AnyModuleAlarm_ReturnsOne()
    {
        var sim = new FakeSimulation
        {
            ["emu1.PcsList.Count"] = 2,
            ["emu1.PcsList[0].AlarmSummary1"] = (ushort)0,
            ["emu1.PcsList[0].AlarmSummary2"] = (ushort)0,
            ["emu1.PcsList[1].AlarmSummary1"] = (ushort)0,
            ["emu1.PcsList[1].AlarmSummary2"] = (ushort)0,
            ["emu2.PcsList.Count"] = 2,
            ["emu2.PcsList[0].AlarmSummary1"] = (ushort)0,
            ["emu2.PcsList[0].AlarmSummary2"] = (ushort)0,
            ["emu2.PcsList[1].AlarmSummary1"] = (ushort)0x0004,
            ["emu2.PcsList[1].AlarmSummary2"] = (ushort)0
        };
        var plugin = new TrinaEmuFaultWordPlugin();

        // emu2 模块2 告警汇总1 非零 → 故障总=1
        Assert.Equal(1, plugin.Compute("SystemFaultSummary", "emu1", sim));

        // 全部模块告警清零 → 故障总=0
        sim["emu2.PcsList[1].AlarmSummary1"] = (ushort)0;
        Assert.Equal(0, plugin.Compute("SystemFaultSummary", "emu1", sim));
    }

    [Fact]
    public void Compute_SystemFaultSummary_ProbesUnitsUntilMissing()
    {
        // 仅 emu1 存在：emu2 缺失时停止探测，emu1 两模块无告警 → 0
        var sim = new FakeSimulation
        {
            ["emu1.PcsList.Count"] = 2,
            ["emu1.PcsList[0].AlarmSummary1"] = (ushort)0,
            ["emu1.PcsList[0].AlarmSummary2"] = (ushort)0,
            ["emu1.PcsList[1].AlarmSummary1"] = (ushort)0,
            ["emu1.PcsList[1].AlarmSummary2"] = (ushort)0
        };
        var plugin = new TrinaEmuFaultWordPlugin();
        Assert.Equal(0, plugin.Compute("SystemFaultSummary", "emu1", sim));

        // 机组根路径无编号后缀：不探测，输出 0
        Assert.Equal(0, plugin.Compute("SystemFaultSummary", "emu", sim));
        Assert.True(plugin.CanHandle("SystemFaultSummary"));
    }

    [Fact]
    public void Compute_SystemRunStateSummary_FaultAlarmRunningStoppedPriority()
    {
        var plugin = new TrinaEmuFaultWordPlugin();

        // 全停机（OperationStatus=1，无告警）→ 1
        var sim = new FakeSimulation
        {
            ["emu1.PcsList.Count"] = 2,
            ["emu1.PcsList[0].OperationStatus"] = 1,
            ["emu1.PcsList[0].AlarmSummary1"] = (ushort)0,
            ["emu1.PcsList[0].AlarmSummary2"] = (ushort)0,
            ["emu1.PcsList[1].OperationStatus"] = 1,
            ["emu1.PcsList[1].AlarmSummary1"] = (ushort)0,
            ["emu1.PcsList[1].AlarmSummary2"] = (ushort)0
        };
        Assert.Equal(1, plugin.Compute("SystemRunStateSummary", "emu1", sim));

        // 模块1 待机（仿真 2 → 协议 3 运行中）
        sim["emu1.PcsList[0].OperationStatus"] = 2;
        Assert.Equal(3, plugin.Compute("SystemRunStateSummary", "emu1", sim));

        // 模块2 放电运行仍为 3；叠加告警 → 6
        sim["emu1.PcsList[1].OperationStatus"] = 5;
        Assert.Equal(3, plugin.Compute("SystemRunStateSummary", "emu1", sim));
        sim["emu1.PcsList[1].AlarmSummary1"] = (ushort)0x0001;
        Assert.Equal(6, plugin.Compute("SystemRunStateSummary", "emu1", sim));

        // 模块1 故障（仿真 6 → 协议 5）优先级最高，覆盖告警
        sim["emu1.PcsList[0].OperationStatus"] = 6;
        Assert.Equal(5, plugin.Compute("SystemRunStateSummary", "emu1", sim));
    }

    [Fact]
    public void Compute_SystemRunStateSummary_AggregatesAcrossUnits()
    {
        var plugin = new TrinaEmuFaultWordPlugin();

        // emu1 停机，emu2 充电运行（仿真 4 → 3）
        var sim = new FakeSimulation
        {
            ["emu1.PcsList.Count"] = 2,
            ["emu1.PcsList[0].OperationStatus"] = 1,
            ["emu1.PcsList[1].OperationStatus"] = 1,
            ["emu2.PcsList.Count"] = 2,
            ["emu2.PcsList[0].OperationStatus"] = 4,
            ["emu2.PcsList[1].OperationStatus"] = 1
        };
        Assert.Equal(3, plugin.Compute("SystemRunStateSummary", "emu1", sim));

        // 无任何机组可探测 → 停机 1
        Assert.Equal(1, plugin.Compute("SystemRunStateSummary", "emu9", new FakeSimulation()));
        Assert.True(plugin.CanHandle("SystemRunStateSummary"));
    }

    [Fact]
    public void Registry_ResolvesFirstMatchingPlugin()
    {
        var registry = new TelemetryPluginRegistry().Register(new TrinaEmuFaultWordPlugin());

        Assert.NotNull(registry.Resolve("ModuleWarningWord1"));
        Assert.NotNull(registry.Resolve("ModuleWarningWord2"));
        Assert.Null(registry.Resolve("UnknownWord"));
        Assert.Null(registry.Resolve(""));
    }

    [Fact]
    public void FromPointMap_Trina10MW_HasPluginPointsForModuleWarningWords()
    {
        var catalog = LoadCatalog("trina_10MW");

        // 模块警告字：8 台 PCS × 警告字1/2 = 16；另点表新增 SYSTEM 级插件点 7 个（故障/运行汇总、黑启动、PCS 台数统计）
        Assert.Equal(23, catalog.PluginPoints.Count);
        var warningPoints = catalog.PluginPoints
            .Where(p => p.WordKey is "ModuleWarningWord1" or "ModuleWarningWord2")
            .ToList();
        Assert.Equal(16, warningPoints.Count);

        // 设备根路径与机组/模块层级一致：单机组 emu1 内 8 台 PCS 按扁平序 PcsList[0..7]
        var roots = warningPoints.Select(p => p.DeviceRoot).Distinct().OrderBy(r => r).ToArray();
        Assert.Equal(Enumerable.Range(0, 8).Select(i => $"emu1.PcsList[{i}]").ToArray(), roots);

        // SYSTEM 级插件点：按单元替换 emuDeviceId 根或固定 emu1 根
        var systemKeys = new[]
        {
            "SystemFaultSummary", "SystemRunStateSummary", "UnitBlackStartStatus",
            "UnitPcsTotalCount", "UnitPcsRunningCount", "UnitPcsAlarmCount", "UnitPcsFaultCount"
        };
        Assert.All(systemKeys, k => Assert.Contains(catalog.PluginPoints, p => p.WordKey == k));

        // 插件点不进普通反射遥测绑定
        var pluginParams = catalog.PluginPoints.Select(p => p.ParamName).ToHashSet();
        Assert.DoesNotContain(catalog.TelemetryPoints, t => pluginParams.Contains(t.ParamName));
    }

    [Fact]
    public void FromPointMap_Trina10MW_SystemControls_BoundToEmuVirtualModel()
    {
        // SYSTEM 控制点：绑定 emu1 虚拟模型，并自动路由 PcsApplyCommands
        var catalog = LoadCatalog("trina_10MW");

        var expected = new Dictionary<string, string>
        {
            ["syst4"] = "emu1.Emu.RemoteControlEnable",
            ["syst5"] = "emu1.Emu.RemoteControlMode",
            ["syst6"] = "emu1.Emu.SystemOperation",
            ["syst7"] = "emu1.Emu.BlackStartModeWrite",
            ["syst1010"] = "emu1.Emu.TargetActivePower",
            ["syst1011"] = "emu1.Emu.TargetReactivePower"
        };

        foreach (var (paramName, fullPath) in expected)
        {
            var binding = catalog.FindControl(paramName);
            Assert.NotNull(binding);
            Assert.Equal(fullPath, binding!.Target.FullPath);
            Assert.Equal(ControlEffectId.PcsApplyCommands, binding.Effect);
        }
    }

    [Fact]
    public void Compute_UnitPcsCounts_ByOperationStatusAndAlarms()
    {
        var plugin = new TrinaEmuFaultWordPlugin();
        var sim = new FakeSimulation
        {
            ["emu1.PcsList.Count"] = 2,
            ["emu1.PcsList[0].OperationStatus"] = 5,   // 放电运行
            ["emu1.PcsList[0].AlarmSummary1"] = (ushort)0,
            ["emu1.PcsList[0].AlarmSummary2"] = (ushort)0,
            ["emu1.PcsList[1].OperationStatus"] = 1,   // 停机
            ["emu1.PcsList[1].AlarmSummary1"] = (ushort)0x0001,
            ["emu1.PcsList[1].AlarmSummary2"] = (ushort)0
        };

        Assert.Equal(2, plugin.Compute("UnitPcsTotalCount", "emu1", sim));
        Assert.Equal(1, plugin.Compute("UnitPcsRunningCount", "emu1", sim));
        Assert.Equal(1, plugin.Compute("UnitPcsAlarmCount", "emu1", sim));
        Assert.Equal(0, plugin.Compute("UnitPcsFaultCount", "emu1", sim));

        // 模块1 故障：不计运行/告警，计入故障
        sim["emu1.PcsList[0].OperationStatus"] = 6;
        sim["emu1.PcsList[0].AlarmSummary1"] = (ushort)0x0002;
        Assert.Equal(0, plugin.Compute("UnitPcsRunningCount", "emu1", sim));
        Assert.Equal(1, plugin.Compute("UnitPcsAlarmCount", "emu1", sim)); // 仅模块2
        Assert.Equal(1, plugin.Compute("UnitPcsFaultCount", "emu1", sim));

        // 待机（2）按运行计入（模块2 仍停机，运行台数为 1）
        sim["emu1.PcsList[0].OperationStatus"] = 2;
        sim["emu1.PcsList[0].AlarmSummary1"] = (ushort)0;
        Assert.Equal(1, plugin.Compute("UnitPcsRunningCount", "emu1", sim));

        // 机组缺失：全部输出 0
        Assert.Equal(0, plugin.Compute("UnitPcsTotalCount", "emu9", new FakeSimulation()));
        Assert.True(plugin.CanHandle("UnitPcsTotalCount"));
        Assert.True(plugin.CanHandle("UnitPcsRunningCount"));
        Assert.True(plugin.CanHandle("UnitPcsAlarmCount"));
        Assert.True(plugin.CanHandle("UnitPcsFaultCount"));
    }

    [Fact]
    public void Compute_UnitBlackStartStatus_AllModulesEnabledOnly()
    {
        var plugin = new TrinaEmuFaultWordPlugin();
        var sim = new FakeSimulation
        {
            ["emu1.PcsList.Count"] = 2,
            ["emu1.PcsList[0].BlackStartEnabled"] = true,
            ["emu1.PcsList[1].BlackStartEnabled"] = false
        };

        // 任一模块未开启 → 0
        Assert.Equal(0, plugin.Compute("UnitBlackStartStatus", "emu1", sim));

        // 全部开启 → 1
        sim["emu1.PcsList[1].BlackStartEnabled"] = true;
        Assert.Equal(1, plugin.Compute("UnitBlackStartStatus", "emu1", sim));

        // 机组缺失或无 PCS → 0
        Assert.Equal(0, plugin.Compute("UnitBlackStartStatus", "emu9", new FakeSimulation()));
        Assert.True(plugin.CanHandle("UnitBlackStartStatus"));
    }

    [Fact]
    public void FromPointMap_Trina10MW_SystemLimits_BoundToModel()
    {
        // 系统级遥测随撤销剥离回到 10MW EMU 点表，绑定全部收敛至单元内部设备（不再映射 ess/em/bms）
        var catalog = LoadCatalog("trina_10MW");

        // 系统 SOC 绑定 EMU 虚拟模型平均 SOC
        var soc = catalog.FindTelemetry("sysyc104");
        Assert.NotNull(soc);
        Assert.Equal("emu1.Emu.AverageBatterySoc", soc!.Target.FullPath);

        // 容量/SOH/允许能量/累计电量类无单元内部模型：解绑为固定默认值
        foreach (var paramName in new[] { "sysyc105", "sysyc109", "sysyc111", "sysyc141", "sysyc143", "sysyc149", "sysyc151" })
            Assert.Contains(paramName, catalog.DefaultValues.Keys);

        // 交流侧聚合：有功/无功绑定 EMU 单元聚合，视在功率按 8 模块求和
        Assert.Equal("emu1.Emu.OutputActivePower", catalog.FindTelemetry("sysyc125")!.Target.FullPath);
        Assert.Equal("emu1.Emu.OutputReactivePower", catalog.FindTelemetry("sysyc127")!.Target.FullPath);

        // 高压开关状态绑定断路器协议状态码（0xAA 合 / 0xEE 分）
        Assert.Equal("emu1.Breaker.ClosedAaEe", catalog.FindTelemetry("sysyc171")!.Target.FullPath);
        var apparent = catalog.SumPoints.FirstOrDefault(p => p.ParamName == "sysyc123");
        Assert.NotNull(apparent);
        Assert.Equal(8, apparent!.Paths.Count);

        // 允许充/放电功率：单机组直接绑定 EMU 虚拟模型限值（EMU 聚合已含全部 8 台 PCS）
        var chargePower = catalog.FindTelemetry("sysyc113");
        Assert.NotNull(chargePower);
        Assert.Equal("emu1.Emu.MaxChargePower", chargePower!.Target.FullPath);

        var dischargePower = catalog.FindTelemetry("sysyc115");
        Assert.NotNull(dischargePower);
        Assert.Equal("emu1.Emu.MaxDischargePower", dischargePower!.Target.FullPath);

        // 直流侧功率按单机组 8 模块求和，覆盖全系统
        var dcPower = catalog.SumPoints.FirstOrDefault(p => p.ParamName == "sysyc131");
        Assert.NotNull(dcPower);
        Assert.Equal(8, dcPower!.Paths.Count);
        Assert.Contains("emu1.PcsList[7].BatteryPower", dcPower.Paths);
    }

    [Fact]
    public void FromPointMap_Trina10MW_SystemAggregates_SingleUnitEightPcs()
    {
        var catalog = LoadCatalog("trina_10MW");

        // 系统级求和点按单机组 8 台 PCS 扁平结构绑定

        // 直流侧总功率：8 模块全量求和
        var dcPower = catalog.SumPoints.FirstOrDefault(p => p.ParamName == "sysyc131");
        Assert.NotNull(dcPower);
        Assert.Equal(8, dcPower!.Paths.Count);
        Assert.Contains("emu1.PcsList[3].BatteryPower", dcPower.Paths);

        // 电网电压（各 PCS 块 RS/ST/TR）绑定所属分组的组电表线电压；
        // 分组绑两台电表时，PCS2/4 块绑第二台组电表 Meters[1]
        Assert.Equal("emu1.Groups[0].Meters[0].LineVoltageAB", catalog.FindTelemetry("yc20")!.Target.FullPath);
        Assert.Equal("emu1.Groups[0].Meters[1].LineVoltageBC", catalog.FindTelemetry("yc225")!.Target.FullPath);
        Assert.Equal("emu1.Groups[1].Meters[1].LineVoltageCA", catalog.FindTelemetry("yc634")!.Target.FullPath);

        // 允许充/放电功率：单机组直接绑定 EMU 限值
        var chargePower = catalog.FindTelemetry("sysyc113");
        Assert.NotNull(chargePower);
        Assert.Equal("emu1.Emu.MaxChargePower", chargePower!.Target.FullPath);

        var dischargePower = catalog.FindTelemetry("sysyc115");
        Assert.NotNull(dischargePower);
        Assert.Equal("emu1.Emu.MaxDischargePower", dischargePower!.Target.FullPath);

        // 过温降载 NTC：按 10MW 一体机两模块配对取大（PCS4 = PcsList[6]/[7]）
        Assert.Equal(4, catalog.MaxPoints.Count);
        var ntc4 = catalog.MaxPoints.FirstOrDefault(p => p.ParamName == "yc635");
        Assert.NotNull(ntc4);
        Assert.Equal(new[] { "emu1.PcsList[6].IGBTMaxTemp", "emu1.PcsList[7].IGBTMaxTemp" }, ntc4!.Paths);

        // 状态字点位无绑定：固定默认值
        Assert.Contains("sysyc218", catalog.DefaultValues.Keys);
        Assert.Contains("sysyc228", catalog.DefaultValues.Keys);
    }

    [Fact]
    public void TelemetryPipeline_ComputesPluginPointsAndDeduplicates()
    {
        var entry = new MapEntry { FunctionCode = 4, Address = 2831, ParamName = "yc210", Scale = 1, Size = 16, Type = "u16" };
        var catalog = new PointCatalog
        {
            ServerName = "simEmu1",
            TelemetryPoints = Array.Empty<PointBinding>(),
            ControlPoints = Array.Empty<PointBinding>(),
            PluginPoints = new[]
            {
                new PluginPointBinding { Entry = entry, ParamName = "yc210", WordKey = "ModuleWarningWord1", DeviceRoot = Root }
            },
            DefaultValues = new Dictionary<string, object>()
        };

        var sim = new FakeSimulation { [$"{Root}.InsulationAlarm"] = true };
        var modbus = new RecordingModbusAdapter();
        var registry = new TelemetryPluginRegistry().Register(new TrinaEmuFaultWordPlugin());
        var pipeline = new TelemetryPipeline(catalog, sim, modbus, new ShadowStore(), registry);

        pipeline.RunOnce();
        Assert.Equal(3, modbus.LastWritten["yc210"]); // Bit0|Bit1

        // 值未变化：不重复写
        modbus.LastWritten.Clear();
        pipeline.RunOnce();
        Assert.Empty(modbus.LastWritten);

        // 故障变化后重新写出
        sim[$"{Root}.AcSurgeProtectorAbnormal"] = true;
        pipeline.RunOnce();
        Assert.Equal(3 | (1 << 12), modbus.LastWritten["yc210"]);
    }

    [Fact]
    public void TelemetryPipeline_UnhandledWordKey_KeepsDefault()
    {
        var entry = new MapEntry { FunctionCode = 4, Address = 2800, ParamName = "yc200", Scale = 1, Size = 16, Type = "u16" };
        var catalog = new PointCatalog
        {
            ServerName = "simEmu1",
            TelemetryPoints = Array.Empty<PointBinding>(),
            ControlPoints = Array.Empty<PointBinding>(),
            PluginPoints = new[]
            {
                new PluginPointBinding { Entry = entry, ParamName = "yc200", WordKey = "UnknownWord", DeviceRoot = Root }
            },
            DefaultValues = new Dictionary<string, object>()
        };

        var modbus = new RecordingModbusAdapter();
        var pipeline = new TelemetryPipeline(catalog, new FakeSimulation(), modbus, new ShadowStore(),
            new TelemetryPluginRegistry().Register(new TrinaEmuFaultWordPlugin()));

        pipeline.RunOnce();
        Assert.Empty(modbus.LastWritten); // 无插件覆盖，保持默认值 0
    }

    private static PointCatalog LoadCatalog(string modelDir)
    {
        // 历史整表已迁出 models/lc（现为片段拼装）；回归插件绑定仍读 archive。
        var models = Path.Combine(FindRepoRoot(), "pointmaps", "models", "lc", modelDir, "lc.csv");
        var archive = Path.Combine(FindRepoRoot(), "pointmaps", "archive", modelDir, "lc.csv");
        var path = File.Exists(models) ? models : archive;
        Assert.True(File.Exists(path), path);
        var pointMap = new ModbusPointMap(path, "simLc1", emuDeviceIdOverride: 1);
        return PointCatalogLoader.FromPointMap(pointMap, "simLc1", new DataExchangeOptions());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EssSimulator.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("未找到仓库根目录");
    }

    /// <summary>字典式仿真数据假对象。</summary>
    private sealed class FakeSimulation : ISimulationDataAdapter
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public object? this[string path]
        {
            get => _values.TryGetValue(path, out var v) ? v : null;
            set
            {
                if (value == null) _values.Remove(path);
                else _values[path] = value;
            }
        }

        public object? Read(string fullBindingPath) => this[fullBindingPath];
        public bool Write(string fullBindingPath, object value) { this[fullBindingPath] = value; return true; }
    }

    /// <summary>记录写点调用的 Modbus 适配器假对象。</summary>
    private sealed class RecordingModbusAdapter : IModbusRegisterAdapter
    {
        public Dictionary<string, object> LastWritten { get; } = new();

        public void WriteDefaults(IReadOnlyDictionary<string, object> defaults)
        {
        }

        public void WritePoints(IReadOnlyDictionary<string, object> values, byte? slaveId = null, bool applyScale = true)
        {
            foreach (var kv in values)
                LastWritten[kv.Key] = kv.Value;
        }

        public Dictionary<string, object> ReadAllControlRaw(IReadOnlyList<string> paramNames, byte? slaveId = null) => new();
        public object? ReadParsedPoint(string paramName, byte? slaveId = null) => null;
    }
}
