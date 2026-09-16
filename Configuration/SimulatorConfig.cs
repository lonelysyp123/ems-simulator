using System.Collections.Generic;
using System.Linq;

namespace EssSimulator.Configuration
{
    public class RuntimeConfig
    {
        /// <summary>
        /// 积分步长倍数（无量纲）：仅放大 SOC、电能等积分量的 dt（基于真实回调间隔），不改变瞬时值动力学 dt。
        /// </summary>
        public double IntegrationStepMultiplier { get; set; } = 1.0;

        /// <summary>PCS 功率爬坡参数</summary>
        public PcsRampConfig PcsRamp { get; set; } = new();

        /// <summary>协议与仿真就绪后，向各 PCS 启停线圈写入 1 并驱动并网（默认合闸工况）。</summary>
        public bool AutoStartPcsOnStartup { get; set; } = true;

        /// <summary>电压源（电网）激活性传播周期（ms）。</summary>
        public int PropagationIntervalMs { get; set; } = 100;

        /// <summary>设备 Step 后 Q-U 与电压传播的最大迭代轮数（≥1，首轮为 Phase3–5 主路径）。</summary>
        public int PropagationQuvMaxIterations { get; set; } = 3;

        /// <summary>Q-U/V 迭代相对电压收敛阈值（pu，相对 PCC 额定线电压）。</summary>
        public double PropagationVoltageTolerancePu { get; set; } = 0.001;

        /// <summary>电站热网络 / 气候（阶段 2：外温 + BMS 柜体空气）。</summary>
        public ThermalRuntimeConfig Thermal { get; set; } = new();
    }

    /// <summary>热仿真运行时配置（Simulator.Runtime.Thermal）。</summary>
    public class ThermalRuntimeConfig
    {
        /// <summary>为 false 时 BMS 环境温回退到 <see cref="ClimateConfig.FixedCelsius"/>（默认 25）。</summary>
        public bool Enabled { get; set; } = true;

        public ClimateConfig Climate { get; set; } = new();
        public BmsCabinetThermalConfig Cabinet { get; set; } = new();

        /// <summary>多点温度探针偏置（°C）；挂点见 <see cref="ThermalProbeBiasesConfig"/>。</summary>
        public ThermalProbeBiasesConfig ProbeBiases { get; set; } = new();

        /// <summary>温度反馈：降额与寿命加速（阶段 5）。</summary>
        public ThermalFeedbackConfig Feedback { get; set; } = new();
    }

    /// <summary>电–热反馈：高温降额、温度加速日历老化。</summary>
    public class ThermalFeedbackConfig
    {
        public bool DeratingEnabled { get; set; } = true;

        /// <summary>开始线性降额的温度（°C）。</summary>
        public double DerateStartCelsius { get; set; } = 40;

        /// <summary>降额至下限的温度（°C）。</summary>
        public double DerateFullCelsius { get; set; } = 55;

        /// <summary>满降额时仍保留的功率比例（0–1）。</summary>
        public double MinPowerFactor { get; set; } = 0.2;

        public bool TemperatureAgingEnabled { get; set; } = true;

        public double AgingReferenceCelsius { get; set; } = 25;

        /// <summary>Arrhenius 系数 B≈Ea/R（K）；约 5000 时 45°C 相对 25°C 加速约 2–3 倍。</summary>
        public double AgingArrheniusB { get; set; } = 5000;

        /// <summary>参考温度下日历老化速率（每年 Age 增量，0–1）。</summary>
        public double CalendarAgingPerYearAtRef { get; set; } = 0.02;
    }

    /// <summary>各测点相对混合温度的固定偏置（模拟传感器安装位置误差）。</summary>
    public class ThermalProbeBiasesConfig
    {
        public double CabinetAirCelsius { get; set; }
        public double CoilCelsius { get; set; } = -1.5;
        public double CondenserCelsius { get; set; } = 2.0;
        public double LiquidSupplyCelsius { get; set; } = -2.0;
        public double LiquidReturnCelsius { get; set; } = 1.0;
        public double DehumidifierTopCelsius { get; set; } = 0.8;
        public double DehumidifierMidCelsius { get; set; }
        public double DehumidifierBottomCelsius { get; set; } = -0.8;
    }

    /// <summary>室外气温模型参数。</summary>
    public class ClimateConfig
    {
        /// <summary>若有值则忽略日变化，始终返回该温度（°C）。</summary>
        public double? FixedCelsius { get; set; }

        public double MinCelsius { get; set; } = 15;
        public double MaxCelsius { get; set; } = 35;

        /// <summary>日最高温出现的小时（0–24，可小数）。</summary>
        public double PeakHour { get; set; } = 14;
    }

    /// <summary>单个 BMS 柜体集总热网络参数。</summary>
    public class BmsCabinetThermalConfig
    {
        public double AirThermalCapacityJPerK { get; set; } = 80_000;
        public double ShellThermalCapacityJPerK { get; set; } = 200_000;
        public double BatteryThermalCapacityJPerK { get; set; } = 800_000;

        /// <summary>室外→外壳热阻（K/W）。</summary>
        public double OutdoorToShellResistanceKPerW { get; set; } = 0.04;

        /// <summary>外壳→柜内空气热阻（K/W）。</summary>
        public double ShellToAirResistanceKPerW { get; set; } = 0.015;

        /// <summary>电池等效热容节点→柜内空气热阻（K/W）。</summary>
        public double BatteryToAirResistanceKPerW { get; set; } = 0.008;

        /// <summary>空调制冷功率（W）；0 表示无空调。</summary>
        public double HvacCoolingPowerW { get; set; } = 0;

        /// <summary>空调目标柜温（°C）。</summary>
        public double HvacSetpointCelsius { get; set; } = 25;

        /// <summary>运行时是否允许空调工作（仍需 HvacCoolingPowerW&gt;0）。</summary>
        public bool HvacEnabled { get; set; } = true;

        /// <summary>制冷回差（°C）：高于设定+回差开启，低于设定关闭。</summary>
        public double HvacHysteresisCelsius { get; set; } = 2;

        /// <summary>比例制冷增益（W/°C），与额定功率取小。</summary>
        public double HvacProportionalGainWPerK { get; set; } = 800;
    }

    public class PcsRampConfig
    {
        /// <summary>功率变化斜率（默认与历史行为一致）</summary>
        public double Slope { get; set; } = 1;

        /// <summary>每一级功率变化时间间隔（ms）</summary>
        public int IntervalMs { get; set; } = 100;

        /// <summary>新设定值生效前初始延时（ms）</summary>
        public int DelayMs { get; set; } = 0;
    }

    public class ProtocolConfig
    {
        /// <summary>BMS Modbus TCP 基础端口（后续按步长递增）</summary>
        public int BaseBmsModbusPort { get; set; } = 1502;

        /// <summary>BMS 端口步长</summary>
        public int BmsPortStep { get; set; } = 10;

        /// <summary>EMU Modbus TCP 基础端口（每台 PCS 一个从站；与 appsettings 默认 1601 对齐以免与 BMS 1502 冲突）</summary>
        public int BaseEmuModbusPort { get; set; } = 1601;

        /// <summary>EMU 端口步长（单位：端口号）</summary>
        public int EmuPortStep { get; set; } = 1;

        /// <summary>EMU IEC 61850 MMS 基础端口（避开需特权的 102）。</summary>
        public int BaseEmuIec61850Port { get; set; } = 8102;

        /// <summary>EMU IEC 61850 端口步长。</summary>
        public int EmuIec61850PortStep { get; set; } = 1;

        /// <summary>
        /// GOOSE 网卡名。空=按 OS 默认（macOS en0 / Windows 0）；<c>none</c> 不收二层 GOOSE。
        /// </summary>
        public string? EmuIec61850GooseInterface { get; set; }

        /// <summary>是否订阅入向 GOOSE（外部 IED 遥控 yk/yt）。</summary>
        public bool EmuIec61850GooseSubscribe { get; set; } = true;

        /// <summary>入向 GOOSE AppID 基数，PCS N 使用 base+N（默认 0x2000）。</summary>
        public int EmuIec61850GooseSubscribeAppIdBase { get; set; } = 0x2000;

        /// <summary>
        /// 入向 GoCbRef。空=按台使用 <c>EMS_PCSxxPCS/LLN0$GO$GoCB1</c>（与 ems_goose.icd 一致）。
        /// 勿依赖「仅 AppID」：macOS 上空 GoCbRef/observer 路径不稳定。
        /// </summary>
        public string? EmuIec61850GooseSubscribeGoCbRef { get; set; }

        /// <summary>电表 Modbus TCP 端口</summary>
        public int EmModbusPort { get; set; } = 1500;

        /// <summary>是否启用 LocalControl 聚合协议（每个储能单元一路 simLc）。</summary>
        public bool EnableLocalControl { get; set; } = false;

        /// <summary>LocalControl Modbus TCP 基础端口。</summary>
        public int BaseLocalControlModbusPort { get; set; } = 1700;

        /// <summary>LocalControl 端口步长（单位：端口号）。</summary>
        public int LocalControlPortStep { get; set; } = 1;

        /// <summary>每路 LocalControl 聚合的 EMU 数（已废弃：simLc 按储能单元 1:1 创建）。</summary>
        public int LocalControlEmuPerGroup { get; set; } = 4;

        /// <summary>光伏 Logger Modbus TCP 基础端口。</summary>
        public int BasePvLoggerModbusPort { get; set; } = 1801;

        /// <summary>光伏 Logger 端口步长。</summary>
        public int PvLoggerPortStep { get; set; } = 1;

        /// <summary>光伏低压电表 Modbus TCP 基础端口。</summary>
        public int BasePvMeterModbusPort { get; set; } = 1901;

        /// <summary>光伏低压电表端口步长。</summary>
        public int PvMeterPortStep { get; set; } = 1;
    }

    public class PcsDeviceConfig
    {
        public string Name { get; set; } = "PCS";
        /// <summary>
        /// PCS 爬坡参数覆盖项；为空时回退到 Runtime.PcsRamp。
        /// </summary>
        public PcsRampConfig? PcsRamp { get; set; }
    }

    public class BmsDeviceConfig
    {
        public string Name { get; set; } = "BMS";
        public int ClusterCount { get; set; } = 12;
        public int PackCount { get; set; } = 4;
        public int CellSeriesCount { get; set; } = 104;
        public int CellParallelCount { get; set; } = 1;
        public double CellNominalVoltage { get; set; } = 3.2;
        public double CellNominalCapacity { get; set; } = 314;
        public double CellInitialSoc { get; set; } = 0.5;
        /// <summary>已废弃：初始 SOC 不再施加随机扰动，保留仅兼容旧配置 JSON。</summary>
        public double CellInitialSocRandomRange { get; set; } = 0;
        public double PackInternalResistance { get; set; } = 0.05;
        public double ClusterInternalResistance { get; set; } = 0.1;
        public double RackInternalResistance { get; set; } = 0.02;

        /// <summary>堆内簇间被动均衡；缺省启用并采用内置建议值。</summary>
        public BmsPassiveBalanceConfig PassiveBalance { get; set; } = new();
    }

    /// <summary>BMS 堆内簇间被动均衡（appsettings / BmsDeviceConfig）。</summary>
    public class BmsPassiveBalanceConfig
    {
        public bool Enabled { get; set; } = true;
        /// <summary>启动阈值：簇间 SOC 差（0~1），建议 0.02。</summary>
        public double StartSocDelta { get; set; } = 0.02;
        /// <summary>停止阈值（回滞），建议 0.01。</summary>
        public double StopSocDelta { get; set; } = 0.01;
        /// <summary>泄放倍率，建议 0.02C。</summary>
        public double BalanceCRate { get; set; } = 0.02;
        /// <summary>仅小电流/静置时均衡。</summary>
        public bool IdleOnly { get; set; } = true;
        /// <summary>IdleOnly 堆电流门槛 (A)，建议 10。</summary>
        public double IdleCurrentThresholdA { get; set; } = 10.0;
        /// <summary>高于最低簇 SOC 多少才泄放，建议 0.01。</summary>
        public double BleedAboveMinMargin { get; set; } = 0.01;
    }

    /// <summary>
    /// EMU 内协议分组（EMU → group → PCS 支路）：纯协议聚合层，不产生电气动作。
    /// 组内 PCS/BMS 按位对齐；组级断路器/电表仅作为协议镜像存在。
    /// </summary>
    public class EmuGroupConfig
    {
        public string Name { get; set; } = "Group";
        /// <summary>本组 PCS 配置。</summary>
        public List<PcsDeviceConfig> Pcs { get; set; } = new();
        /// <summary>本组 BMS 配置；与组内 PCS 按位对齐，缺位用默认配置补齐。</summary>
        public List<BmsDeviceConfig> Bms { get; set; } = new();
        /// <summary>组态绑定到本组的断路器节点名称（可选，协议镜像）。</summary>
        public string? BreakerName { get; set; }
        /// <summary>组态绑定到本组的电表节点名称列表（可选，协议镜像；允许多台）。</summary>
        public List<string> MeterNames { get; set; } = new();

        /// <summary>本组 PCS 台数。</summary>
        public int PcsCount => Pcs?.Count ?? 0;
    }

    public class EssUnitConfig
    {
        public string Name { get; set; } = "Unit";
        /// <summary>本单元 PCS 配置；为空时默认 2 台（向后兼容）。</summary>
        public List<PcsDeviceConfig> Pcs { get; set; } = new();
        /// <summary>本单元 BMS 配置；与 PCS 通道 1:1，为空时默认 2 台。</summary>
        public List<BmsDeviceConfig> Bms { get; set; } = new();

        /// <summary>本单元是否绑定单元高压断路器（组态断路器节点归入 EMU）；缺省 true 兼容既有配置。</summary>
        public bool HasUnitBreaker { get; set; } = true;
        /// <summary>组态绑定到本单元的断路器节点名称（未绑定时为 null）。</summary>
        public string? UnitBreakerName { get; set; }
        /// <summary>本单元是否绑定单元电表（组态电表节点归入 EMU）；缺省 true 兼容既有配置。</summary>
        public bool HasUnitMeter { get; set; } = true;
        /// <summary>组态绑定到本单元的电表节点名称（未绑定时为 null）。</summary>
        public string? UnitMeterName { get; set; }
        /// <summary>单元电表所接运行时母线 Id；空则仍按 PCS 交流母线镜像。</summary>
        public string? UnitMeterSourceBusId { get; set; }
        /// <summary>组态绑定到本单元的单元变节点名称（可选，仅镜像展示）。</summary>
        public string? UnitTransformerName { get; set; }

        /// <summary>本单元绑定的双耳变压器（1～2 台，按画布 Y/X）；空则走两绕组隐式单元变。</summary>
        public List<SplitTransformerRuntimeConfig> SplitTransformers { get; set; } = new();

        /// <summary>第一台双耳箱变；兼容只写单对象的旧 overlay。</summary>
        public SplitTransformerRuntimeConfig? SplitTransformer { get; set; }

        /// <summary>解析本单元生效的双耳箱变列表（优先 <see cref="SplitTransformers"/>）。</summary>
        public IReadOnlyList<SplitTransformerRuntimeConfig> ResolveSplitTransformers()
        {
            if (SplitTransformers is { Count: > 0 })
                return SplitTransformers.Where(s => s is { Present: true }).Take(2).ToList();
            if (SplitTransformer is { Present: true })
                return new[] { SplitTransformer };
            return Array.Empty<SplitTransformerRuntimeConfig>();
        }

        /// <summary>本单元下属 EMU 分组（EMU → group → PCS 支路）；为空时保持扁平构成（向后兼容）。</summary>
        public List<EmuGroupConfig> Groups { get; set; } = new();

        /// <summary>本单元是否按 group 分层构成。</summary>
        public bool HasGroups => Groups is { Count: > 0 };

        /// <summary>本单元下属 PCS 台数；分组构成时为各组之和，未配置 Pcs 时回退 2。</summary>
        public int PcsCount
        {
            get
            {
                if (HasGroups)
                {
                    int sum = Groups.Sum(g => g.PcsCount);
                    if (sum > 0) return sum;
                }
                return Pcs is { Count: > 0 } ? Pcs.Count : 2;
            }
        }
    }

    /// <summary>组态双耳变压器运行时补丁：每 EMU 至多 2 台，两耳 PCS 按连线归类。</summary>
    public sealed class SplitTransformerRuntimeConfig
    {
        public bool Present { get; set; }
        public string? Name { get; set; }
        public double PrimaryVoltage { get; set; } = 35000;
        public double SecondaryVoltage { get; set; } = 690;
        public double RatedPowerKva { get; set; } = 6300;
        public double ImpedancePercent { get; set; } = 6;
        public double SplitImpedancePercent { get; set; } = 8;
        public double SplitRatio { get; set; } = 0.5;
        public double NoLoadLoss { get; set; } = 80;
        public double LoadLoss { get; set; } = 400;
        /// <summary>本单元内 PCS 序号（0 基）归入左耳；未出现在任一侧的默认左耳。</summary>
        public List<int> LeftEarPcsIndices { get; set; } = new();
        public List<int> RightEarPcsIndices { get; set; } = new();
    }

    /// <summary>储能单元列表（对应 appsettings.json: EssUnits 节，绑定到 <see cref="SimulatorConfig.Devices"/>）</summary>
    public static class EssUnitsConfig
    {
        public const string Section = "EssUnits";
    }

    /// <summary>光伏单元运行时配置（组态 overlay / Simulator.PvUnits）。</summary>
    public sealed class PvUnitRuntimeConfig
    {
        public string Name { get; set; } = "PV";
        public int InverterCount { get; set; } = 16;
        public int StringCount { get; set; } = 16;
        public int ModulesPerString { get; set; } = 30;
        public double InverterRatedPowerKw { get; set; } = 320;
        public double InverterMaxPowerKw { get; set; } = 352;
        public double InverterEfficiency { get; set; } = 0.99;
        public double InverterAcVoltageV { get; set; } = 690;
        public double UnitXfPrimaryV { get; set; } = 35000;
        public double UnitXfSecondaryV { get; set; } = 690;
        public double UnitXfRatedKva { get; set; } = 5120;
        public double DcVoltageMin { get; set; } = 500;
        public double DcVoltageMax { get; set; } = 1500;
        public string ModuleModel { get; set; } = "TSM-NEG21C.20Q";
        /// <summary>地面反射（0–1），作为方阵 A/B 反照率初值；0 表示关闭双面增益。</summary>
        public double GroundAlbedo { get; set; }
        /// <summary>投运年限；0 表示新产品、无衰减。</summary>
        public double OperatingYears { get; set; }
    }

    /// <summary>顶层仿真器配置（对应 appsettings.json: Simulator 节）</summary>
    public class SimulatorConfig
    {
        public const string Section = "Simulator";

        public RuntimeConfig Runtime { get; set; } = new();
        public ProtocolConfig Protocol { get; set; } = new();
        public List<EssUnitConfig> Devices { get; set; } = new();
        public List<PvUnitRuntimeConfig> PvUnits { get; set; } = new();

        public int EssUnitCount => Devices?.Count ?? 0;
        public int PvUnitCount => PvUnits?.Count ?? 0;

        /// <summary>
        /// 储能单元数。无储能且无光伏时回退 1（兼容空 Devices 的旧 appsettings）；
        /// 纯光伏工程为 0，不伪造 BMS/PCS。
        /// </summary>
        public int EffectiveEssUnitCount
        {
            get
            {
                int ess = EssUnitCount;
                if (ess > 0) return ess;
                if (PvUnitCount > 0) return 0;
                return 1;
            }
        }

        // 兼容旧代码的便捷属性（由新结构推导）
        /// <summary>PCS/BMS 通道总数 = 各储能单元 PCS 台数之和。</summary>
        public int UnitCount => GetPcsDeviceConfigs().Count;
        public int BaseModbusPort => Protocol.BaseBmsModbusPort;
        public int PcsModbusPort => Protocol.BaseEmuModbusPort;
        public int EmModbusPort => Protocol.EmModbusPort;
        public double IntegrationStepMultiplier => Runtime.IntegrationStepMultiplier;

        /// <summary>各储能单元下属 PCS 台数（未配置单元回退 2）。</summary>
        public IReadOnlyList<int> GetPcsCountsPerUnit()
        {
            var list = new List<int>();
            foreach (var unit in ResolveEssUnitsOrFallback())
                list.Add(unit.PcsCount);
            return list;
        }

        public IReadOnlyList<BmsDeviceConfig> GetBmsDeviceConfigs()
        {
            var list = new List<BmsDeviceConfig>();
            var units = ResolveEssUnitsOrFallback();
            foreach (var unit in units)
            {
                if (unit.HasGroups)
                {
                    // 分组构成：BMS 在组内与 PCS 按位对齐
                    foreach (var group in unit.Groups)
                    {
                        var bms = group.Bms ?? new List<BmsDeviceConfig>();
                        for (int i = 0; i < group.PcsCount; i++)
                            list.Add(i < bms.Count ? bms[i] : new BmsDeviceConfig());
                    }
                    continue;
                }

                int n = unit.PcsCount;
                var unitBms = unit.Bms ?? new List<BmsDeviceConfig>();
                for (int i = 0; i < n; i++)
                    list.Add(i < unitBms.Count ? unitBms[i] : new BmsDeviceConfig());
            }
            return list;
        }

        public IReadOnlyList<PcsDeviceConfig> GetPcsDeviceConfigs()
        {
            var list = new List<PcsDeviceConfig>();
            var units = ResolveEssUnitsOrFallback();
            foreach (var unit in units)
            {
                if (unit.HasGroups)
                {
                    // 分组构成：按 group 顺序展平
                    foreach (var group in unit.Groups)
                        list.AddRange(group.Pcs ?? new List<PcsDeviceConfig>());
                    continue;
                }

                int n = unit.PcsCount;
                var pcs = unit.Pcs ?? new List<PcsDeviceConfig>();
                for (int i = 0; i < n; i++)
                    list.Add(i < pcs.Count ? pcs[i] : new PcsDeviceConfig());
            }
            return list;
        }

        /// <summary>储能单元构成（含分组）；空配置时回退默认单机组，纯光伏工程为空。</summary>
        public IReadOnlyList<EssUnitConfig> ResolveEssUnitsOrFallback()
        {
            if (Devices is { Count: > 0 })
                return Devices;
            if (PvUnitCount > 0)
                return Array.Empty<EssUnitConfig>();
            return new List<EssUnitConfig> { new() };
        }
    }

    /// <summary>PCS 物理参数配置（对应 appsettings.json: Pcs 节）</summary>
    public class PcsPhysicalConfig
    {
        public const string Section = "Pcs";

        public double RatedPower { get; set; } = 1725;
        public double MaxPower { get; set; } = 1897.5;
        public double Efficiency { get; set; } = 0.99;
        public double DcVoltageRangeMin { get; set; } = 1000;
        public double DcVoltageRangeMax { get; set; } = 1500;
        public double AcVoltageNominal { get; set; } = 690;
        public double FrequencyNominal { get; set; } = 50;
        public double MaxCurrent { get; set; } = 1588;
        /// <summary>线损相关系数（用于并网电压折算）</summary>
        public double GridLossCoefficient { get; set; } = 0.01;

        /// <summary>离网 V/f：有效电压向命令值过渡的最长仿真时间（ms），默认 100ms 内完成建压/降压。</summary>
        public double IslandVoltageRampDurationMs { get; set; } = 100;

        /// <summary>黑启动：孤岛电压设定与有效值每差 1V 对应的有功调节量（kW，正放）。</summary>
        public double BlackStartActivePowerGainKwPerVolt { get; set; } = 2.174;

        /// <summary>黑启动自动有功上限（kW）。</summary>
        public double BlackStartMaxActivePowerKw { get; set; } = 200;

        /// <summary>建压过程中按有效电压百分比附加的励磁有功（占额定功率比例，0–1）。</summary>
        public double BlackStartMagnetizingPowerFraction { get; set; } = 0.02;

        /// <summary>判定 690V 母线已带电的电压比例（相对 AcVoltageNominal，默认 0.85≈587V）。</summary>
        public double BlackStartBusEnergizedFraction { get; set; } = 0.85;

        /// <summary>黑启动准备阶段时长（ms）：DC 就绪自检，AC 保持 0。</summary>
        public double BlackStartPrechargeDelayMs { get; set; } = 300;

        /// <summary>软启动电压爬坡速率（V/s，线电压）。默认 138 保证 0→690 V 可在 5 s 内完成。</summary>
        public double BlackStartVoltageRampVs { get; set; } = 138;

        /// <summary>构网电压上升斜率（V/s）。≤0 表示未设，工厂取 max(BlackStartVoltageRampVs, Vnom/5)。</summary>
        public double VoltageRampUpVs { get; set; }

        /// <summary>构网电压下降斜率（V/s）。≤0 表示与上升斜率相同。</summary>
        public double VoltageRampDownVs { get; set; }

        /// <summary>构网 Q-V 下垂使能。</summary>
        public bool QvDroopEnabled { get; set; } = true;

        /// <summary>下垂系数 nq（V/kvar）。≤0 表示按 4% Vnom / 额定功率 计算。</summary>
        public double QvDroopCoefficientVPerKvar { get; set; }

        /// <summary>无功死区（kvar）。≤0 表示 2% 额定功率。</summary>
        public double QvDroopDeadbandKvar { get; set; }

        /// <summary>下垂无功工作点 Q0（kvar）。</summary>
        public double QvDroopQ0Kvar { get; set; }

        /// <summary>true 时仅软起结束后才下垂，软起段 Vref=Vramp。</summary>
        public bool QvDroopEnableAfterSoftStartOnly { get; set; }

        /// <summary>Vref 上限（相对 Vnom 的标幺）。≤0 视为 1.10。</summary>
        public double QvDroopVmaxPu { get; set; } = 1.10;

        /// <summary>构网 P-f 下垂使能。单机空载时 P≈0，频率仍为额定。</summary>
        public bool PfDroopEnabled { get; set; } = true;

        /// <summary>下垂系数 mp（Hz/kW）。≤0 表示按额定功率跌 0.5 Hz。</summary>
        public double PfDroopCoefficientHzPerKw { get; set; }

        /// <summary>有功死区（kW）。≤0 表示 2% 额定功率。</summary>
        public double PfDroopDeadbandKw { get; set; }

        /// <summary>下垂有功工作点 P0（kW）。</summary>
        public double PfDroopP0Kw { get; set; }

        /// <summary>停机 PLL 使能门槛（相对 Vnom 标幺）。</summary>
        public double PllEnableVoltagePu { get; set; } = 0.20;

        /// <summary>PLL 跟踪时间常数（秒）。</summary>
        public double PllTauSec { get; set; } = 0.10;

        /// <summary>预同步使能门槛（相对 Vnom 标幺，通常看主机斜坡/母线）。</summary>
        public double PreSyncEnableVoltagePu { get; set; } = 0.70;

        /// <summary>预同步电压窗口（标幺）。</summary>
        public double PreSyncVoltageWindowPu { get; set; } = 0.05;

        /// <summary>预同步频率窗口（Hz）。</summary>
        public double PreSyncFrequencyWindowHz { get; set; } = 0.2;

        /// <summary>预同步相位窗口（度）。</summary>
        public double PreSyncPhaseWindowDeg { get; set; } = 10;

        /// <summary>软启动起始频率（Hz）。</summary>
        public double BlackStartFrequencyStartHz { get; set; } = 47;

        /// <summary>频率爬升最大速率（Hz/s）。</summary>
        public double BlackStartFrequencyRampHzPerSec { get; set; } = 12;

        /// <summary>建压期无功电压支撑（kvar/V，正=升压）。</summary>
        public double BlackStartReactiveVoltageGainKvarPerV { get; set; } = 4.0;

        /// <summary>建压期交流电流限幅（相对 MaxCurrent 比例，0–1）。</summary>
        public double BlackStartCurrentLimitFraction { get; set; } = 0.45;

        /// <summary>
        /// 黑启动稳态站用电分担（空载铁损+线损、励磁无功）：
        /// AllOnBus=已构网 PCS 按并机接手系数分摊（后机切入后斜坡接手）；
        /// LeaderOnly=仅建压机（有效电压最高者）承担，从机不重复励磁/空载。
        /// </summary>
        public string BlackStartSteadyLossShareMode { get; set; } = "AllOnBus";

        /// <summary>活母线并机后站用电/励磁无功接手斜坡时间（秒）。≤0 视为立即均分。</summary>
        public double BlackStartJoinShareRampSec { get; set; } = 5.0;

        // 暂态建模参数
        public double TransientSubStepMs { get; set; } = 10;
        public double VoltageControllerTauMs { get; set; } = 15;
        public double InrushPeakMultiplier { get; set; } = 6.0;
        public double InrushDecayTauMs { get; set; } = 300;
        public double InrushTriggerVoltageFrac { get; set; } = 0.15;
        public double DvDtTripThresholdVPerSec { get; set; } = 500;
        public double DvDtRideThroughMs { get; set; } = 100;
    }

    /// <summary>变压器参数配置（对应 appsettings.json: Transformer 节）</summary>
    public class TransformerConfig
    {
        public const string Section = "Transformer";

        public double RatedPower { get; set; } = 2500;
        public double PrimaryVoltage { get; set; } = 220000;
        public double SecondaryVoltage { get; set; } = 35000;
        public double NoLoadLoss { get; set; } = 50;
        public double LoadLoss { get; set; } = 200;
        public double ImpedancePercent { get; set; } = 4;
        /// <summary>并网点无功-电压影响系数（1.0=按阻抗标称影响）</summary>
        public double ReactiveVoltageInfluenceCoefficient { get; set; } = 1.0;
        public double NoLoadCurrentPercent { get; set; } = 2;

        public bool MagnetizingInrushEnabled { get; set; } = true;
        public double MagnetizingInrushDvDtThresholdPuPerSec { get; set; } = 0.8;
        public double MagnetizingInrushPeakExtraMultipleOfRatedPrimary { get; set; } = 4.0;
        public double MagnetizingInrushDecayTimeConstantSec { get; set; } = 0.45;
        public double MagnetizingInrushMaxExtraMultipleOfRatedPrimary { get; set; } = 12.0;
        /// <summary>工程是否包含站用主变；false 时主断下游直接接到站用母线。</summary>
        public bool Present { get; set; } = true;
    }

    /// <summary>单元升压一体机变压器参数（对应 appsettings.json: UnitTransformer 节，35kV/690V）</summary>
    public class UnitTransformerConfig
    {
        public const string Section = "UnitTransformer";

        public double RatedPower { get; set; } = 2500;
        public double PrimaryVoltage { get; set; } = 35000;
        public double SecondaryVoltage { get; set; } = 690;
        public double NoLoadLoss { get; set; } = 50;
        public double LoadLoss { get; set; } = 200;
        public double ImpedancePercent { get; set; } = 4;
        public double ReactiveVoltageInfluenceCoefficient { get; set; } = 1.0;
        public double NoLoadCurrentPercent { get; set; } = 2;

        public bool MagnetizingInrushEnabled { get; set; } = true;
        public double MagnetizingInrushDvDtThresholdPuPerSec { get; set; } = 0.8;
        public double MagnetizingInrushPeakExtraMultipleOfRatedPrimary { get; set; } = 4.0;
        public double MagnetizingInrushDecayTimeConstantSec { get; set; } = 0.45;
        public double MagnetizingInrushMaxExtraMultipleOfRatedPrimary { get; set; } = 12.0;
    }

    /// <summary>负载仿真配置（对应 appsettings.json: Load 节）</summary>
    public class LoadConfig
    {
        public const string Section = "Load";

        /// <summary>有功计划负载（kW，仅允许 ≤0：负=用电消耗，正值禁止）</summary>
        public double ActivePowerPlan { get; set; } = 0;

        /// <summary>无功计划负载（kvar，可正可负）</summary>
        public double ReactivePowerPlan { get; set; } = 0;
    }

    /// <summary>220kV 并网点（PCC）电压模型，与并网电表同侧（对应 appsettings.json: Pcc 节）</summary>
    public class PccConfig
    {
        public const string Section = "Pcc";

        /// <summary>PCC 额定线电压（V），默认 220kV</summary>
        public double NominalLineVoltage { get; set; } = 220000;

        /// <summary>并网点等效短路容量（MVA），用于 Q-V 灵敏度</summary>
        public double ShortCircuitMva { get; set; } = 750;

        /// <summary>无功引起的电压偏移限幅（±%，相对额定）</summary>
        public double MaxVoltageShiftPercent { get; set; } = 5;

        /// <summary>无功-电压影响系数（1.0=按 Q/Ssc 标称）</summary>
        public double ReactiveVoltageInfluenceCoefficient { get; set; } = 1.0;

        /// <summary>站内 35kV 母线额定线电压（V），由 PCC 电压按变比推导</summary>
        public double StationBusNominalLineVoltage { get; set; } = 35000;
    }
}
