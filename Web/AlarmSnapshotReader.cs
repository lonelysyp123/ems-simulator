using System.Collections.Concurrent;
using System.Reflection;
using EssSimulator.Core;
using EssSimulator.Display;
using EssSimulator.EssDeviceSimModel.Bms;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;

namespace EssSimulator.Web
{
    public sealed class AlarmFlagDto
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        /// <summary>protection / alarm / fault / other</summary>
        public string Kind { get; set; } = "other";
        public bool Active { get; set; }
    }

    public sealed class AlarmDeviceDto
    {
        public string DeviceType { get; set; } = ""; // bms-rack | bms-stack | pcs
        public string DeviceId { get; set; } = "";   // simBms1.r0 / simBms1.stack / simEmu1.pcs0
        public string Title { get; set; } = "";
        public int UnitNumber { get; set; }
        public int? RackIndex { get; set; }
        public int ActiveCount { get; set; }
        public int TotalCount { get; set; }
        public List<AlarmFlagDto> Flags { get; set; } = new();
    }

    public sealed class AlarmSnapshotDto
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public int UnitCount { get; set; }
        public int ActiveDeviceCount { get; set; }
        public int ActiveFlagCount { get; set; }
        public List<AlarmDeviceDto> Devices { get; set; } = new();
    }

    /// <summary>BMS 簇/堆 + PCS 告警/故障位快照（绿=未触发，红=触发）。</summary>
    public static class AlarmSnapshotReader
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> BoolPropsCache = new();

        private static readonly Dictionary<string, string> ClusterLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MildAlarm"] = "轻微告警",
            ["ModerateAlarm"] = "中等告警",
            ["SevereAlarm"] = "严重告警",
            ["UndervoltageAlarm"] = "簇欠压报警",
            ["UndervoltageFault"] = "簇欠压故障",
            ["UndervoltageProtection"] = "簇欠压保护",
            ["OvervoltageAlarm"] = "簇过压报警",
            ["OvervoltageFault"] = "簇过压故障",
            ["OvervoltageProtection"] = "簇过压保护",
            ["ChargeOvercurrentAlarm"] = "充电过流报警",
            ["ChargeOvercurrentFault"] = "充电过流故障",
            ["ChargeOvercurrentProtection"] = "充电过流保护",
            ["DischargeOvercurrentAlarm"] = "放电过流报警",
            ["DischargeOvercurrentFault"] = "放电过流故障",
            ["DischargeOvercurrentProtection"] = "放电过流保护",
            ["InsulationAlarm"] = "绝缘报警",
            ["InsulationFault"] = "绝缘故障",
            ["InsulationProtection"] = "绝缘保护",
            ["CellUnderVoltageAlarm"] = "单体欠压报警",
            ["CellUnderVoltageFault"] = "单体欠压故障",
            ["CellUnderVoltageProtection"] = "单体欠压保护",
            ["CellOverVoltageAlarm"] = "单体过压报警",
            ["CellOverVoltageFault"] = "单体过压故障",
            ["CellOverVoltageProtection"] = "单体过压保护",
            ["VoltageDifferenceAlarm"] = "单体压差报警",
            ["VoltageDifferenceFault"] = "单体压差故障",
            ["VoltageDifferenceProtection"] = "单体压差保护",
            ["TempDifferenceAlarm"] = "单体温差报警",
            ["TempDifferenceFault"] = "单体温差故障",
            ["TempDifferenceProtection"] = "单体温差保护",
            ["LowSOCAlarm"] = "SOC过低报警",
            ["LowSOCFault"] = "SOC过低故障",
            ["LowSOCProtection"] = "SOC过低保护",
            ["TerminalHighTempAlarm"] = "端子高温报警",
            ["TerminalHighTempFault"] = "端子高温故障",
            ["TerminalHighTempProtection"] = "端子高温保护",
            ["CellChargeLowTempAlarm"] = "充电低温报警",
            ["CellChargeLowTempFault"] = "充电低温故障",
            ["CellChargeLowTempProtection"] = "充电低温保护",
            ["CellChargeHighTempAlarm"] = "充电高温报警",
            ["CellChargeHighTempFault"] = "充电高温故障",
            ["CellChargeHighTempProtection"] = "充电高温保护",
            ["CellDischargeLowTempAlarm"] = "放电低温报警",
            ["CellDischargeLowTempFault"] = "放电低温故障",
            ["CellDischargeLowTempProtection"] = "放电低温保护",
            ["CellDischargeHighTempAlarm"] = "放电高温报警",
            ["CellDischargeHighTempFault"] = "放电高温故障",
            ["CellDischargeHighTempProtection"] = "放电高温保护",
            ["HVBHighTempAlarm"] = "高压箱连接器高温报警",
            ["HVBHighTempFault"] = "高压箱连接器高温故障",
            ["HVBHighTempProtection"] = "高压箱连接器高温保护",
            ["TempRiseAlarm"] = "温升报警",
            ["TempRiseFault"] = "温升故障",
            ["TempRiseProtection"] = "温升保护",
            ["TempSamplingFault"] = "温度采样故障",
            ["VoltageSamplingFault"] = "电压采样故障",
            ["MasterCommFault"] = "主机通信故障",
            ["SlaveCommFault"] = "从机通信故障",
            ["ChargeProhibited"] = "禁止充电",
            ["DischargeProhibited"] = "禁止放电",
            ["BatteryBoxOvervoltageProtection"] = "电池箱过压保护",
            ["BatteryBoxOvervoltageAlarm"] = "电池箱过压报警",
            ["BatteryBoxOvervoltageFault"] = "电池箱过压故障",
            ["BatteryBoxUndervoltageProtection"] = "电池箱欠压保护",
            ["BatteryBoxUndervoltageAlarm"] = "电池箱欠压报警",
            ["BatteryBoxUndervoltageFault"] = "电池箱欠压故障",
            ["BatteryBoxTempDifferenceAlarm"] = "电池箱温差报警",
            ["BatteryBoxTempDifferenceFault"] = "电池箱温差故障",
            ["BatteryBoxTempDifferenceProtection"] = "电池箱温差保护",
            ["BatteryBoxPositivePoleTempDifferenceAlarm"] = "电池箱正极柱温差报警",
            ["BatteryBoxPositivePoleTempDifferenceFault"] = "电池箱正极柱温差故障",
            ["BatteryBoxPositivePoleTempDifferenceProtection"] = "电池箱正极柱温差保护",
            ["BatteryBoxNegativePoleTempDifferenceAlarm"] = "电池箱负极柱温差报警",
            ["BatteryBoxNegativePoleTempDifferenceFault"] = "电池箱负极柱温差故障",
            ["BatteryBoxNegativePoleTempDifferenceProtection"] = "电池箱负极柱温差保护",
            ["BatteryBoxBusbarHighTempAlarm"] = "电池箱铜排高温报警",
            ["BatteryBoxBusbarHighTempFault"] = "电池箱铜排高温故障",
            ["BatteryBoxBusbarHighTempProtection"] = "电池箱铜排高温保护",
            ["BatteryBoxVoltageExtremaDifferenceAlarm"] = "电池箱电压极差报警",
            ["BatteryBoxVoltageExtremaDifferenceFault"] = "电池箱电压极差故障",
            ["BatteryBoxVoltageExtremaDifferenceProtection"] = "电池箱电压极差保护",
        };

        private static readonly Dictionary<string, string> PcsLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["InsulationAlarm"] = "绝缘阻抗异常告警",
            ["LeakageCurrentAbnormal"] = "漏电流异常",
            ["DcOverVoltage"] = "直流过压",
            ["GridOverVoltage"] = "电网过压异常",
            ["GridUnderVoltage"] = "电网欠压异常",
            ["GridOverFrequency"] = "电网过频异常",
            ["GridUnderFrequency"] = "电网欠频异常",
            ["PowerModuleOverTemp"] = "功率模块过温",
            ["GridPhaseSequenceAbnormal"] = "电网相序异常",
            ["InverterSoftwareOverCurrent"] = "逆变软件过流",
            ["DcSoftStartAbnormal"] = "直流软启动异常",
            ["AcFanAbnormal"] = "交流风机异常",
            ["AcMainSwitchAbnormal"] = "交流主开关异常",
            ["InternalAbnormal"] = "内部异常",
            ["InternalOverTemp"] = "机内过温",
            ["AcSoftStartAbnormal"] = "交流软启动异常",
            ["HeatExchangerFault"] = "热交换机故障",
            ["AcSurgeProtectorAbnormal"] = "交流防雷器异常",
            ["InternalEmergencyStopFault"] = "内部急停故障",
            ["ExternalEmergencyStopFault"] = "外部急停故障",
            ["BusVoltageNotReady"] = "母线电压不符合开机条件",
            ["BusOverCurrent"] = "母线电流过流",
            ["DoorAlarm"] = "门禁告警",
            ["PllAbnormal"] = "锁相异常",
            ["DcSurgeProtectorAbnormal"] = "直流防雷器异常",
            ["InverterHardwareOverCurrent"] = "逆变硬件过流",
            ["DriveFault"] = "驱动故障",
            ["IdConflict"] = "ID 冲突",
            ["MainsUnbalance"] = "市电不平衡",
            ["SmokeAlarm"] = "烟雾告警",
            ["ParallelCanCommFault"] = "并机 CAN 通讯异常",
            ["HmiCanCommFault"] = "HMI CAN 通讯异常",
            ["ModelSettingError"] = "机型设置错误",
            ["Hmi485CommFault"] = "HMI 485 通讯异常",
            ["RemoteCommFault"] = "远程通讯故障",
            ["DcFanAbnormal"] = "直流风机异常",
            ["HeatsinkTempSwitchAbnormal"] = "散热器温度开关异常",
            ["ExternalTempSwitchAbnormal"] = "外部温度开关异常",
            ["AuxTransformerTempSwitchAbnormal"] = "辅源变压器温度开关异常",
            ["InductorTempSwitchAbnormal"] = "电感温度开关异常",
            ["PositiveGroundAbnormal"] = "正极接地异常",
            ["NegativeGroundAbnormal"] = "负极接地异常",
            ["AcGroundAbnormal"] = "交流接地异常",
            ["GridGroundAbnormal"] = "并网接地异常",
            ["InductorFanAbnormal"] = "电感风机异常",
            ["BatteryLowVoltageWarning"] = "电池低压告警",
            ["OverloadWarning"] = "过载告警",
            ["BatteryOverVoltage"] = "电池过压",
            ["BatteryUnderVoltage"] = "电池欠压",
            ["DcOverCurrent"] = "直流过流",
            ["OutputOverVoltage"] = "输出电压异常",
            ["OutputVoltageNotReadyForGrid"] = "输出电压不符合离网条件",
            ["OverloadProtection"] = "过载保护",
            ["ShortCircuitProtection"] = "短路保护",
            ["DcFuseAbnormal"] = "直流保险丝异常",
            ["BatteryHeavyLoadUnderVoltage"] = "电池重载欠压",
            ["BatteryReverseConnection"] = "电池反接",
            ["BatteryVoltageNotReadyForCharge"] = "电池电压不符合充电条件",
            ["BmsSystemFault"] = "BMS 系统故障",
            ["BmsCommFault"] = "BMS 通信异常",
            ["BmsDryContactAbnormal"] = "BMS 干接点异常",
            ["BmsChargeProhibit"] = "BMS 禁充",
            ["BmsDischargeProhibit"] = "BMS 禁放",
            ["BmsStandby"] = "BMS 待机",
            ["BmsAlarm"] = "BMS 告警",
            ["AntiPidModuleAbnormal"] = "防 PID 模块异常",
            ["PhaseSyncAbnormal"] = "相位同步异常",
            ["DcPathConfigAbnormal"] = "直流路径数配置异常",
            ["AntiIslandingAbnormal"] = "防孤岛异常",
            ["GroundLoopAbnormal"] = "接地回路异常",
            ["MidpointContactAbnormal"] = "中点接触器异常",
            ["AcSurgeSuppressionAbnormal"] = "交流缓冲异常",
        };

        private static readonly HashSet<string> PcsExcluded = new(StringComparer.OrdinalIgnoreCase)
        {
            "gridOnOffSwitch", "BlackStartEnabled", "pcsOnOffSwitch", "PcsOnOffSwitch",
            "LvrtRunning", "HvrtRunning"
        };

        public static AlarmSnapshotDto ReadAll()
        {
            int unitCount = Math.Max(1, GuiSimDataAccess.GetEssUnitCount());
            var devices = new List<AlarmDeviceDto>();

            for (int u = 1; u <= unitCount; u++)
            {
                if (!SimulatorHost.Instance.Contains($"simBms{u}") &&
                    !SimulatorHost.Instance.Contains($"bms{u}"))
                    continue;

                devices.Add(ReadBmsStack(u));
                int clusters = ResolveClusterCount(u);
                for (int r = 0; r < clusters; r++)
                    devices.Add(ReadBmsRack(u, r));
            }

            for (int u = 1; u <= unitCount; u++)
            {
                if (!SimulatorHost.Instance.Contains($"emu{u}"))
                    continue;
                int pcsCount = ResolvePcsCount(u);
                for (int p = 0; p < pcsCount; p++)
                {
                    var pcs = ReadPcs(u, p);
                    if (pcs != null)
                        devices.Add(pcs);
                }
            }

            return new AlarmSnapshotDto
            {
                UnitCount = unitCount,
                Devices = devices,
                ActiveDeviceCount = devices.Count(d => d.ActiveCount > 0),
                ActiveFlagCount = devices.Sum(d => d.ActiveCount)
            };
        }

        public static AlarmSnapshotDto ReadBmsUnit(int unitNumber, int? rackIndex)
        {
            var devices = new List<AlarmDeviceDto>();
            devices.Add(ReadBmsStack(unitNumber));
            int clusters = ResolveClusterCount(unitNumber);
            if (rackIndex.HasValue)
            {
                int r = Math.Clamp(rackIndex.Value, 0, Math.Max(0, clusters - 1));
                devices.Add(ReadBmsRack(unitNumber, r));
            }
            else
            {
                for (int r = 0; r < clusters; r++)
                    devices.Add(ReadBmsRack(unitNumber, r));
            }

            return new AlarmSnapshotDto
            {
                UnitCount = 1,
                Devices = devices,
                ActiveDeviceCount = devices.Count(d => d.ActiveCount > 0),
                ActiveFlagCount = devices.Sum(d => d.ActiveCount)
            };
        }

        private static AlarmDeviceDto ReadBmsStack(int unitNumber)
        {
            string basePath = $"bms{unitNumber}.BatteryStacks[0].SystemAlarms";
            var flags = ReadBoolFlags(
                basePath,
                typeof(BatteryStack.StackSystemAlarms),
                ClusterLabels,
                includeAllBools: true);

            return BuildDevice("bms-stack", $"simBms{unitNumber}.stack",
                $"BMS{unitNumber} 堆 SystemAlarms", unitNumber, null, flags);
        }

        private static AlarmDeviceDto ReadBmsRack(int unitNumber, int rackIndex)
        {
            string basePath = $"bms{unitNumber}.BatteryStacks[0].Cluseter[{rackIndex}].Alarms";
            var flags = ReadBoolFlags(
                basePath,
                typeof(ClusterAlarms),
                ClusterLabels,
                includeAllBools: false);

            return BuildDevice("bms-rack", $"simBms{unitNumber}.r{rackIndex}",
                $"BMS{unitNumber} 簇{rackIndex}", unitNumber, rackIndex, flags);
        }

        private static AlarmDeviceDto? ReadPcs(int unitNumber, int pcsIndex)
        {
            string probe = $"emu{unitNumber}.PcsList[{pcsIndex}].DriveFault";
            try
            {
                var o = SimServer.GetExtIfVariableVal(probe);
                if (o == null && pcsIndex > 0)
                {
                    // 探测 PcsList 是否存在该索引
                    var list = SimServer.GetExtIfVariableVal($"emu{unitNumber}.PcsList");
                    if (list is System.Collections.ICollection c && pcsIndex >= c.Count)
                        return null;
                    if (list == null)
                        return null;
                }
            }
            catch
            {
                return null;
            }

            // PcsData 是嵌套类，用实例路径反射不可直接 typeof；按已知标签集读取
            var flags = new List<AlarmFlagDto>();
            foreach (var kv in PcsLabels)
            {
                if (PcsExcluded.Contains(kv.Key))
                    continue;
                bool active = GuiSimDataAccess.SafeGetBool($"emu{unitNumber}.PcsList[{pcsIndex}].{kv.Key}");
                flags.Add(new AlarmFlagDto
                {
                    Name = kv.Key,
                    Label = kv.Value,
                    Kind = ClassifyKind(kv.Key),
                    Active = active
                });
            }

            // 若全部读失败且设备不存在，跳过
            if (flags.Count == 0)
                return null;

            return BuildDevice("pcs", $"simEmu{unitNumber}.pcs{pcsIndex}",
                $"EMU{unitNumber} PCS{pcsIndex}", unitNumber, null, flags);
        }

        private static List<AlarmFlagDto> ReadBoolFlags(
            string basePath,
            Type type,
            IReadOnlyDictionary<string, string> labels,
            bool includeAllBools)
        {
            var props = BoolPropsCache.GetOrAdd(type, t =>
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p =>
                    {
                        var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                        if (pt != typeof(bool))
                            return false;
                        if (p.Name.StartsWith("resv", StringComparison.OrdinalIgnoreCase))
                            return false;
                        if (p.Name.Contains("Summary", StringComparison.OrdinalIgnoreCase))
                            return false;
                        if (p.Name is "MainPositiveContactor" or "MainNegativeContactor")
                            return false;
                        if (!includeAllBools &&
                            !labels.ContainsKey(p.Name) &&
                            !p.Name.EndsWith("Alarm", StringComparison.OrdinalIgnoreCase) &&
                            !p.Name.EndsWith("Fault", StringComparison.OrdinalIgnoreCase) &&
                            !p.Name.EndsWith("Protection", StringComparison.OrdinalIgnoreCase) &&
                            !p.Name.EndsWith("Prohibited", StringComparison.OrdinalIgnoreCase))
                            return false;
                        return p.CanRead;
                    })
                    .OrderBy(p => KindOrder(ClassifyKind(p.Name)))
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

            var flags = new List<AlarmFlagDto>(props.Length);
            foreach (var prop in props)
            {
                bool active = GuiSimDataAccess.SafeGetBool($"{basePath}.{prop.Name}");
                flags.Add(new AlarmFlagDto
                {
                    Name = prop.Name,
                    Label = labels.TryGetValue(prop.Name, out var zh) ? zh : prop.Name,
                    Kind = ClassifyKind(prop.Name),
                    Active = active
                });
            }
            return flags;
        }

        private static AlarmDeviceDto BuildDevice(
            string type, string id, string title, int unit, int? rack, List<AlarmFlagDto> flags)
        {
            int active = flags.Count(f => f.Active);
            return new AlarmDeviceDto
            {
                DeviceType = type,
                DeviceId = id,
                Title = title,
                UnitNumber = unit,
                RackIndex = rack,
                Flags = flags,
                ActiveCount = active,
                TotalCount = flags.Count
            };
        }

        private static string ClassifyKind(string name)
        {
            if (name.EndsWith("Protection", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("Prohibited", StringComparison.OrdinalIgnoreCase))
                return "protection";
            if (name.EndsWith("Fault", StringComparison.OrdinalIgnoreCase))
                return "fault";
            if (name.EndsWith("Alarm", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("Warning", StringComparison.OrdinalIgnoreCase))
                return "alarm";
            if (name.Contains("Fault", StringComparison.OrdinalIgnoreCase))
                return "fault";
            if (name.Contains("Alarm", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Warning", StringComparison.OrdinalIgnoreCase))
                return "alarm";
            return "other";
        }

        private static int KindOrder(string kind) => kind switch
        {
            "fault" => 0,
            "alarm" => 1,
            "protection" => 2,
            _ => 3
        };

        private static int ResolvePcsCount(int unitNumber)
        {
            try
            {
                var list = SimServer.GetExtIfVariableVal($"emu{unitNumber}.PcsList");
                if (list is System.Collections.ICollection c)
                    return Math.Max(0, c.Count);
            }
            catch { /* fall through */ }
            return 2;
        }

        private static int ResolveClusterCount(int unitNumber)
        {
            try
            {
                var v = SimServer.GetExtIfVariableVal($"bms{unitNumber}.BatteryStacks[0].Cluseter.Count");
                if (v is int i) return Math.Max(1, i);
                if (v is long l) return (int)Math.Max(1, Math.Min(int.MaxValue, l));
                if (v != null && int.TryParse(v.ToString(), out int p)) return Math.Max(1, p);
            }
            catch { /* fall through */ }
            return GuiSimDataAccess.GetClusterCount();
        }
    }
}
