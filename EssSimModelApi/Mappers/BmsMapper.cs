using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssDeviceSimModel.Diagnostics;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using log4net;

namespace EssSimulator.EssSimModelApi.Mappers
{
    /// <summary>
    /// 将 ESS 物理模型数据映射到 BMS 接口数据对象，并在投影后评估保护。
    /// 保护状态机本身见 <see cref="BmsRackProtection"/>（只消费模型结构）。
    /// </summary>
    public static class BmsMapper
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(BmsMapper));

        /// <summary>堆级系统运行状态（bank yc3）：0正常 1禁充 2禁放 3待机 4停机。</summary>
        public const int StackOpNormal = 0;
        public const int StackOpChargeForbidden = 1;
        public const int StackOpDischargeForbidden = 2;
        public const int StackOpStandby = 3;
        public const int StackOpShutdown = 4;

        /// <summary>
        /// RackAlarmSummary1 中非充放电方向的二级告警位掩码：
        /// bit10 绝缘 / bit11 端子高温 / bit12 高压箱高温 / bit13 压差 / bit14 温差。
        /// </summary>
        public const ushort NonChargeDischargeAlarmMask = 0x7C00;

        // ── 运行状态 ──────────────────────────────────────────────────

        /// <summary>簇级充放电指示：0静置 1放电 2充电（与 ChargeDischargeStatus 一致）。</summary>
        public static int GetOperationStatus(float current) =>
            current > 0 ? 1 : current < 0 ? 2 : 0;

        /// <summary>
        /// 按优先级解析堆级系统运行状态：停机 &gt; 待机 &gt; 禁充/禁放 &gt; 正常。
        /// 应在保护评估完成后调用，以便使用最新的告警汇总与功率限值。
        /// </summary>
        public static int ResolveStackOperationStatus(BatteryStack stack)
        {
            if (stack == null)
                return StackOpNormal;

            // 4 停机：任意三级告警，或 BMS 已下电（与 PCS 断链）
            if (stack.BMSFaultSummary != 0 || !stack.IsPcsLinked)
                return StackOpShutdown;

            // 3 待机：存在不属于充放电方向的二级告警
            if ((stack.BMSAlarmSummary & NonChargeDischargeAlarmMask) != 0)
                return StackOpStandby;

            float maxCharge = stack.MaxChargePower ?? 0f;
            float maxDischarge = stack.MaxDischargePower ?? 0f;
            bool canCharge = maxCharge > 1e-3f;
            bool canDischarge = maxDischarge > 1e-3f;

            // 1 禁充：最大可充电功率为 0，且仍可放电
            if (!canCharge && canDischarge)
                return StackOpChargeForbidden;

            // 2 禁放：最大可放电功率为 0，且仍可充电
            if (!canDischarge && canCharge)
                return StackOpDischargeForbidden;

            // 0 正常（初始化完成后默认可充可放，或两侧功率均为 0 时仍归正常）
            return StackOpNormal;
        }

        /// <summary>堆级系统运行状态码 → 界面文案。</summary>
        public static string GetStackOperationStatusLabel(int? code) => code switch
        {
            StackOpNormal => "正常",
            StackOpChargeForbidden => "禁充",
            StackOpDischargeForbidden => "禁放",
            StackOpStandby => "待机",
            StackOpShutdown => "停机",
            _ => code.HasValue ? $"未知({code})" : "—"
        };

        /// <summary>将堆级系统运行状态写回 DTO（供 Modbus 遥测管道映射到 yc3）。</summary>
        public static void UpdateStackOperationStatus(BatteryManagementSystemData bmsData, int stackIndex = 0)
        {
            if (bmsData?.BatteryStacks == null || stackIndex < 0 || stackIndex >= bmsData.BatteryStacks.Count)
                return;

            var stack = bmsData.BatteryStacks[stackIndex];
            stack.OperationStatus = ResolveStackOperationStatus(stack);
        }

        // ── Rack → Stack 数据映射 ─────────────────────────────────────

        public static void MapRackToStack(RackState rack, BatteryManagementSystemData bmsData)
        {
            if (rack == null || bmsData == null) return;

            var stack = bmsData.BatteryStacks[0];
            // DC 侧对外电压：故障/离网下电（断链）后为 0；电芯串电压仍由物理模型保留，仅影响端口/遥测
            double dcVoltage = rack.IsPcsLinked ? rack.TotalVoltage : 0;
            double dcCurrent = rack.IsPcsLinked ? rack.TotalCurrent : 0;
            stack.TotalVoltage = (float)dcVoltage;
            stack.Current      = (float)dcCurrent;
            stack.Power        = (float)(dcVoltage * dcCurrent / 1000.0);
            stack.SOC          = (float)rack.MinClusterSOC;
            stack.SOH          = (float)rack.StateOfHealth;
            stack.Cycles       = 98;

            FindExtreme(rack, 1, out var val, out var cid, out var pid, out var eid);
            stack.MaxCellVoltage = val; stack.MaxCellVoltageClusterId = cid;
            stack.MaxCellVoltagePackId = pid; stack.MaxCellVoltageCellId = eid;

            FindExtreme(rack, 2, out val, out cid, out pid, out eid);
            stack.MinCellVoltage = val; stack.MinCellVoltageClusterId = cid;
            stack.MinCellVoltagePackId = pid; stack.MinCellVoltageCellId = eid;

            FindExtreme(rack, 3, out val, out cid, out pid, out eid);
            stack.MaxCellTemp = val; stack.MaxCellTempClusterId = cid;
            stack.MaxCellTempPackId = pid; stack.MaxCellTempCellId = eid;

            FindExtreme(rack, 4, out val, out cid, out pid, out eid);
            stack.MinCellTemp = val; stack.MinCellTempClusterId = cid;
            stack.MinCellTempPackId = pid; stack.MinCellTempCellId = eid;

            stack.AvgCellVoltage  = (float)rack.TotalVoltage / 416;
            stack.CellVoltageDiff = (float)rack.VoltageDifference;
            stack.AvgCellTemp     = (float)rack.AvgClusterTemp;
            stack.CellTempDiff    = (float)rack.MaxClusterTemp - (float)rack.MinClusterTemp;
            stack.MaxCellSOC      = (float)rack.MaxClusterSOC;
            stack.MinCellSOC      = (float)rack.MinClusterSOC;
            stack.CumulativeChargeEnergy    = (float)rack.TotalChargeEnergy;
            stack.CumulativeDischargeEnergy = (float)rack.TotalDischargeEnergy;
        }

        // ── 簇级数据映射 ──────────────────────────────────────────────

        /// <summary>
        /// 将 BatteryRackSimulator 中每个簇的状态写入 bmsData 的簇测量值和告警状态。
        /// </summary>
        public static void MapClusters(BatteryRackSimulator rackSim, BatteryManagementSystemData bmsData)
        {
            var clusterStates = rackSim.GetRackState().ClusterStates;
            var clusterConfig = rackSim.GetRackConfig().ClusterConfig;
            if (clusterStates == null || clusterConfig == null)
            {
                Log.Warn("[BmsMapper] 簇状态或簇配置缺失，跳过簇映射");
                return;
            }

            int packSerial = rackSim._clusters[0]._packs[0].GetPackConfiguration().SeriesCount;

            for (int i = 0; i < clusterStates.Count; i++)
            {
                var cs  = clusterStates[i];
                var clu = bmsData.BatteryStacks[0].Cluseter[i];

                // 单体电压/温度字典
                var voltDict = clu.ClusterCellVoltages.CellVoltages;
                var tempDict = clu.ClusterCellTemperatures.CellTemperatures;

                // 基础簇测量值
                var m = clu.Measurements;
                m.TotalVoltage    = (float)cs.TotalVoltage;
                m.Current         = (float)cs.TotalCurrent;
                m.SOC             = (float)cs.AvgPackSOC;
                m.Power           = (float)cs.TotalVoltage * (float)cs.TotalCurrent;
                m.SOH             = (float)cs.StateOfHealth;
                m.OperationStatus = (ushort)GetOperationStatus((float)cs.TotalCurrent);
                m.AvgCellTemp     = (float)cs.AvgPackTemp;
                m.MaxCellSOC      = (float)cs.MaxPackSOC;
                m.MinCellSOC      = (float)cs.MinPackSOC;

                if (cs.EquivalentCells)
                {
                    int count = clusterConfig.PackCount * packSerial;
                    float v = (float)cs.EquivalentCellVoltage;
                    float t = (float)cs.EquivalentCellTemp;
                    voltDict.Clear();
                    tempDict.Clear();
                    for (int key = 0; key < count; key++)
                    {
                        voltDict[key] = v;
                        tempDict[key] = t;
                    }

                    m.AvgCellVoltage = v;
                    m.CellVoltageSum = v * count;
                    m.MaxCellVoltage = v; m.MaxCellVoltageId = 0;
                    m.MinCellVoltage = v; m.MinCellVoltageId = 0;
                    m.MaxCellTemp = t; m.MaxCellTempId = 0;
                    m.MinCellTemp = t; m.MinCellTempId = 0;
                    continue;
                }

                for (int j = 0; j < clusterConfig.PackCount; j++)
                    for (int k = 0; k < packSerial; k++)
                    {
                        int key = j * packSerial + k;
                        voltDict[key] = (float)cs.PackStates[j].CellStates[k].Voltage;
                        tempDict[key] = (float)cs.PackStates[j].CellStates[k].Temperature;
                    }

                m.AvgCellVoltage  = (float)cs.TotalVoltage / 416;
                m.CellVoltageSum  = voltDict.Values.Sum();

                var maxV = voltDict.Aggregate((a, b) => a.Value > b.Value ? a : b);
                var minV = voltDict.Aggregate((a, b) => a.Value < b.Value ? a : b);
                m.MaxCellVoltage  = maxV.Value; m.MaxCellVoltageId = maxV.Key;
                m.MinCellVoltage  = minV.Value; m.MinCellVoltageId = minV.Key;

                var maxT = tempDict.Aggregate((a, b) => a.Value > b.Value ? a : b);
                var minT = tempDict.Aggregate((a, b) => a.Value < b.Value ? a : b);
                m.MaxCellTemp     = maxT.Value; m.MaxCellTempId = maxT.Key;
                m.MinCellTemp     = minT.Value; m.MinCellTempId = minT.Key;
            }
        }

        /// <summary>将物理量映射到 BMS DTO，评估簇级/Rack 级保护并回写 Rack 故障态。</summary>
        public static void SyncTelemetryAndProtection(BmsRackDevice device, BatteryManagementSystemData bmsData)
        {
            var rackState = device.Rack.GetRackState();
            if (rackState == null || bmsData == null)
                return;

            MapRackToStack(rackState, bmsData);
            MapClusters(device.Rack, bmsData);
            EvaluateClusters(device.Rack, bmsData);
            var snapshot = ToProtectionSnapshot(bmsData.BatteryStacks[0]);
            BmsRackProtection.ApplyRackFaultSummary(snapshot, rackState);
            UpdateStackOperationStatus(bmsData);
            BmsStateTracker.ReportProtectionChanges(device.DisplayLabel, snapshot, rackState);
            device.RefreshProtectionFault();
        }

        /// <summary>
        /// 待机时清除充放电方向故障并刷新 Rack 故障态（一次性复位，不抑制后续再触发）。
        /// </summary>
        public static bool TryClearChargeDischargeFaults(
            BatteryManagementSystemData bmsData,
            BmsRackDevice device,
            out string message)
        {
            message = string.Empty;
            var rackState = device.Rack.GetRackState();
            if (rackState == null || bmsData?.BatteryStacks == null || bmsData.BatteryStacks.Count == 0)
            {
                message = "BMS 数据或 Rack 状态不可用";
                return false;
            }

            if (BmsRackProtection.IsCharging(rackState.TotalCurrent) ||
                BmsRackProtection.IsDischarging(rackState.TotalCurrent))
            {
                message = "当前仍在充/放电，请先待机后再清除故障";
                return false;
            }

            var stack = bmsData.BatteryStacks[0];
            foreach (var cluster in stack.Cluseter)
                cluster.Alarms.ClearChargeDischargeAlarms();

            stack.IsPcsLinked = device.IsLinked;
            if (stack.GridConnectStatus == 3)
                stack.GridConnectStatus = 0;

            SyncTelemetryAndProtection(device, bmsData);
            message = stack.BMSFaultSummary == 0 && rackState.IsFault == 0
                ? "充放电方向故障已清除（一次性）；再次充/放电若仍超限将重新触发"
                : "充放电方向故障已清除；部分非方向故障仍在，可重新并网后观察";
            return true;
        }

        private static void EvaluateClusters(BatteryRackSimulator rackSim, BatteryManagementSystemData bmsData)
        {
            var clusterStates = rackSim.GetRackState().ClusterStates;
            var clusterConfig = rackSim.GetRackConfig().ClusterConfig;
            if (clusterStates == null || clusterConfig == null)
            {
                Log.Warn("[BmsMapper] 簇状态或簇配置缺失，跳过保护评估");
                return;
            }

            int packSerial = rackSim._clusters[0]._packs[0].GetPackConfiguration().SeriesCount;
            var stack = bmsData.BatteryStacks[0];

            for (int i = 0; i < clusterStates.Count; i++)
            {
                var cs = clusterStates[i];
                var clu = stack.Cluseter[i];
                BmsRackProtection.EvaluateCluster(
                    cs,
                    clusterConfig.PackCount,
                    packSerial,
                    clu.Thresholds,
                    clu.Alarms,
                    clu.Measurements.Insulation ?? 0f,
                    ResolveBusbarTempC(clu.Measurements),
                    ResolvePoleTempC(clu));
            }
        }

        private static BmsStackProtectionSnapshot ToProtectionSnapshot(BatteryStack stack) => new()
        {
            FaultSummary = stack.BMSFaultSummary,
            AlarmSummary = stack.BMSAlarmSummary,
            ProtectionSummary = stack.BMSProtectionSummary,
            IsChargeFault = stack.IsChargeFault,
            IsDischargeFault = stack.IsDischargeFault,
            Soc = stack.SOC ?? 0f,
            Soh = stack.SOH ?? 1f
        };

        private static float ResolveBusbarTempC(ClusterBasicMeasurements m)
        {
            if (m == null)
                return 26f;
            float? t1 = m.HVB1Temp;
            float? t2 = m.HVB2Temp;
            if (t1.HasValue && t2.HasValue)
                return Math.Max(t1.Value, t2.Value);
            return t1 ?? t2 ?? 26f;
        }

        private static float ResolvePoleTempC(BatteryCluster clu)
        {
            var temps = clu?.ClusterCellTemperatures;
            if (temps == null)
                return 26f;

            float max = float.NegativeInfinity;
            bool any = false;
            void consider(Dictionary<int, float?>? dict)
            {
                if (dict == null) return;
                foreach (var kv in dict)
                {
                    if (!kv.Value.HasValue) continue;
                    any = true;
                    if (kv.Value.Value > max)
                        max = kv.Value.Value;
                }
            }

            consider(temps.PositivePoleTemperatures);
            consider(temps.NegativePoleTemperatures);
            return any ? max : 26f;
        }

        /// <summary>向后兼容：委托至 <see cref="BmsRackProtection.UpdateUnder"/>。</summary>
        public static void UpdateUnder(ref bool? l1, ref bool? l2, ref bool? l3,
            float t1, float t2, float t3, float r1, float r2, float r3, double val) =>
            BmsRackProtection.UpdateUnder(ref l1, ref l2, ref l3, t1, t2, t3, r1, r2, r3, val);

        /// <summary>向后兼容：委托至 <see cref="BmsRackProtection.UpdateOver"/>。</summary>
        public static void UpdateOver(ref bool? l1, ref bool? l2, ref bool? l3,
            float t1, float t2, float t3, float r1, float r2, float r3, double val) =>
            BmsRackProtection.UpdateOver(ref l1, ref l2, ref l3, t1, t2, t3, r1, r2, r3, val);

        // ── 极值查找 ──────────────────────────────────────────────────

        private static void FindExtreme(RackState rack, int type,
            out float value, out int clusterId, out int packId, out int cellId)
        {
            value = 0; clusterId = 0; packId = 0; cellId = 0;
            if (rack?.ClusterStates == null || rack.ClusterStates.Count == 0) return;

            bool findMax = type is 1 or 3;
            Func<CellState, double> sel = type switch
            {
                1 or 2 => c => c.Voltage,
                3 or 4 => c => c.Temperature,
                _      => c => 0
            };

            double best = findMax ? double.MinValue : double.MaxValue;
            int bc = 0, bp = 0, be = 0;

            for (int i = 0; i < rack.ClusterStates.Count; i++)
            {
                var cluster = rack.ClusterStates[i];
                if (cluster?.PackStates == null) continue;
                for (int j = 0; j < cluster.PackStates.Count; j++)
                {
                    var pack = cluster.PackStates[j];
                    if (pack == null) continue;
                    if (pack.CellStates == null || pack.CellStates.Count == 0)
                    {
                        double equiv = type is 1 or 2
                            ? (findMax ? pack.MaxCellVoltage : pack.MinCellVoltage)
                            : type is 3 or 4
                                ? (findMax ? pack.MaxCellTemp : pack.MinCellTemp)
                                : 0;
                        if ((findMax && equiv > best) || (!findMax && equiv < best))
                        { best = equiv; bc = i; bp = j; be = 0; }
                        continue;
                    }
                    for (int k = 0; k < pack.CellStates.Count; k++)
                    {
                        double v = sel(pack.CellStates[k]);
                        if ((findMax && v > best) || (!findMax && v < best))
                        { best = v; bc = i; bp = j; be = k; }
                    }
                }
            }
            value = (float)best; clusterId = bc; packId = bp; cellId = be;
        }
    }
}
