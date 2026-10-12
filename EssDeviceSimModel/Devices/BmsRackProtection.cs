using EssSimulator.EssDeviceSimModel.Bms;

namespace EssSimulator.EssDeviceSimModel.Devices
{
    /// <summary>堆级保护汇总输入（由 Mapper 从协议 DTO 抽出，模型侧不引用 Api）。</summary>
    public readonly struct BmsStackProtectionSnapshot
    {
        public ushort FaultSummary { get; init; }
        public ushort AlarmSummary { get; init; }
        public ushort ProtectionSummary { get; init; }
        public bool IsChargeFault { get; init; }
        public bool IsDischargeFault { get; init; }
        public float Soc { get; init; }
        public float Soh { get; init; }
    }

    /// <summary>
    /// BMS 保护逻辑：簇级阈值评估 + Rack 级告警汇总回写物理状态。
    /// </summary>
    public static class BmsRackProtection
    {
        public static void EvaluateCluster(
            ClusterState clusterState,
            int packCount,
            int cellsPerPack,
            ClusterThresholds thresholds,
            ClusterAlarms alarms,
            float insulationValue,
            float busbarTempC = 26.0f,
            float poleTempC = 26.0f)
        {
            var minVoltList = Enumerable.Range(0, packCount)
                .Select(j => (float)clusterState.PackStates[j].MinCellVoltage).ToList();
            var maxVoltList = Enumerable.Range(0, packCount)
                .Select(j => (float)clusterState.PackStates[j].MaxCellVoltage).ToList();
            var minTempList = Enumerable.Range(0, packCount)
                .Select(j => (float)clusterState.PackStates[j].MinCellTemp).ToList();
            var maxTempList = Enumerable.Range(0, packCount)
                .Select(j => (float)clusterState.PackStates[j].MaxCellTemp).ToList();

            // 充/放电高温与低温保护以「电芯温度」为判据（每芯独立温度，由热环境/节点温度影响散热得到）。

            var thr = thresholds;
            var alm = alarms;
            bool? l1, l2, l3;
            bool isCharging = IsCharging(clusterState.TotalCurrent);
            bool isDischarging = IsDischarging(clusterState.TotalCurrent);

            if (isDischarging)
            {
                (l1, l2, l3) = (alm.UndervoltageProtection, alm.UndervoltageAlarm, alm.UndervoltageFault);
                UpdateUnder(ref l1, ref l2, ref l3, thr.UndervoltageThreshold1!.Value, thr.UndervoltageThreshold2!.Value, thr.UndervoltageThreshold3!.Value, thr.UndervoltageRecovery1!.Value, thr.UndervoltageRecovery2!.Value, thr.UndervoltageRecovery3!.Value, (float)clusterState.TotalVoltage);
                (alm.UndervoltageProtection, alm.UndervoltageAlarm, alm.UndervoltageFault) = (l1, l2, l3);

                (l1, l2, l3) = (alm.DischargeOvercurrentProtection, alm.DischargeOvercurrentAlarm, alm.DischargeOvercurrentFault);
                UpdateOver(ref l1, ref l2, ref l3, thr.DischargeOvercurrentThreshold1!.Value, thr.DischargeOvercurrentThreshold2!.Value, thr.DischargeOvercurrentThreshold3!.Value, thr.DischargeOvercurrentRecovery1!.Value, thr.DischargeOvercurrentRecovery2!.Value, thr.DischargeOvercurrentRecovery3!.Value, (float)(-clusterState.TotalCurrent));
                (alm.DischargeOvercurrentProtection, alm.DischargeOvercurrentAlarm, alm.DischargeOvercurrentFault) = (l1, l2, l3);

                (l1, l2, l3) = (alm.CellUnderVoltageProtection, alm.CellUnderVoltageAlarm, alm.CellUnderVoltageFault);
                UpdateUnder(ref l1, ref l2, ref l3, thr.CellUndervoltageThreshold1!.Value, thr.CellUndervoltageThreshold2!.Value, thr.CellUndervoltageThreshold3!.Value, thr.CellUndervoltageRecovery1!.Value, thr.CellUndervoltageRecovery2!.Value, thr.CellUndervoltageRecovery3!.Value, minVoltList.Min());
                (alm.CellUnderVoltageProtection, alm.CellUnderVoltageAlarm, alm.CellUnderVoltageFault) = (l1, l2, l3);

                (l1, l2, l3) = (alm.LowSOCProtection, alm.LowSOCAlarm, alm.LowSOCFault);
                UpdateUnder(ref l1, ref l2, ref l3, thr.LowSOCTreshold1!.Value, thr.LowSOCTreshold2!.Value, thr.LowSOCTreshold3!.Value, thr.LowSOCRecovery1!.Value, thr.LowSOCRecovery2!.Value, thr.LowSOCRecovery3!.Value, (float)clusterState.MinPackSOC);
                (alm.LowSOCProtection, alm.LowSOCAlarm, alm.LowSOCFault) = (l1, l2, l3);

                (l1, l2, l3) = (alm.CellDischargeHighTempProtection, alm.CellDischargeHighTempAlarm, alm.CellDischargeHighTempFault);
                UpdateOver(ref l1, ref l2, ref l3, thr.DischargeHighTempThreshold1!.Value, thr.DischargeHighTempThreshold2!.Value, thr.DischargeHighTempThreshold3!.Value, thr.DischargeHighTempRecovery1!.Value, thr.DischargeHighTempRecovery2!.Value, thr.DischargeHighTempRecovery3!.Value, maxTempList.Max());
                (alm.CellDischargeHighTempProtection, alm.CellDischargeHighTempAlarm, alm.CellDischargeHighTempFault) = (l1, l2, l3);

                (l1, l2, l3) = (alm.CellDischargeLowTempProtection, alm.CellDischargeLowTempAlarm, alm.CellDischargeLowTempFault);
                UpdateUnder(ref l1, ref l2, ref l3, thr.DischargeLowTempThreshold1!.Value, thr.DischargeLowTempThreshold2!.Value, thr.DischargeLowTempThreshold3!.Value, thr.DischargeLowTempRecovery1!.Value, thr.DischargeLowTempRecovery2!.Value, thr.DischargeLowTempRecovery3!.Value, minTempList.Min());
                (alm.CellDischargeLowTempProtection, alm.CellDischargeLowTempAlarm, alm.CellDischargeLowTempFault) = (l1, l2, l3);
            }

            if (isCharging)
            {
                (l1, l2, l3) = (alm.OvervoltageProtection, alm.OvervoltageAlarm, alm.OvervoltageFault);
                UpdateOver(ref l1, ref l2, ref l3, thr.OvervoltageThreshold1!.Value, thr.OvervoltageThreshold2!.Value, thr.OvervoltageThreshold3!.Value, thr.OvervoltageRecovery1!.Value, thr.OvervoltageRecovery2!.Value, thr.OvervoltageRecovery3!.Value, (float)clusterState.TotalVoltage);
                (alm.OvervoltageProtection, alm.OvervoltageAlarm, alm.OvervoltageFault) = (l1, l2, l3);

                (l1, l2, l3) = (alm.ChargeOvercurrentProtection, alm.ChargeOvercurrentAlarm, alm.ChargeOvercurrentFault);
                UpdateOver(ref l1, ref l2, ref l3, thr.ChargeOvercurrentThreshold1!.Value, thr.ChargeOvercurrentThreshold2!.Value, thr.ChargeOvercurrentThreshold3!.Value, thr.ChargeOvercurrentRecovery1!.Value, thr.ChargeOvercurrentRecovery2!.Value, thr.ChargeOvercurrentRecovery3!.Value, (float)clusterState.TotalCurrent);
                (alm.ChargeOvercurrentProtection, alm.ChargeOvercurrentAlarm, alm.ChargeOvercurrentFault) = (l1, l2, l3);

                (l1, l2, l3) = (alm.CellOverVoltageProtection, alm.CellOverVoltageAlarm, alm.CellOverVoltageFault);
                UpdateOver(ref l1, ref l2, ref l3, thr.CellOvervoltageThreshold1!.Value, thr.CellOvervoltageThreshold2!.Value, thr.CellOvervoltageThreshold3!.Value, thr.CellOvervoltageRecovery1!.Value, thr.CellOvervoltageRecovery2!.Value, thr.CellOvervoltageRecovery3!.Value, maxVoltList.Max());
                (alm.CellOverVoltageProtection, alm.CellOverVoltageAlarm, alm.CellOverVoltageFault) = (l1, l2, l3);

                (l1, l2, l3) = (alm.CellChargeHighTempProtection, alm.CellChargeHighTempAlarm, alm.CellChargeHighTempFault);
                UpdateOver(ref l1, ref l2, ref l3, thr.ChargeHighTempThreshold1!.Value, thr.ChargeHighTempThreshold2!.Value, thr.ChargeHighTempThreshold3!.Value, thr.ChargeHighTempRecovery1!.Value, thr.ChargeHighTempRecovery2!.Value, thr.ChargeHighTempRecovery3!.Value, maxTempList.Max());
                (alm.CellChargeHighTempProtection, alm.CellChargeHighTempAlarm, alm.CellChargeHighTempFault) = (l1, l2, l3);

                (l1, l2, l3) = (alm.CellChargeLowTempProtection, alm.CellChargeLowTempAlarm, alm.CellChargeLowTempFault);
                UpdateUnder(ref l1, ref l2, ref l3, thr.ChargeLowTempThreshold1!.Value, thr.ChargeLowTempThreshold2!.Value, thr.ChargeLowTempThreshold3!.Value, thr.ChargeLowTempRecovery1!.Value, thr.ChargeLowTempRecovery2!.Value, thr.ChargeLowTempRecovery3!.Value, minTempList.Min());
                (alm.CellChargeLowTempProtection, alm.CellChargeLowTempAlarm, alm.CellChargeLowTempFault) = (l1, l2, l3);
            }

            (l1, l2, l3) = (alm.VoltageDifferenceProtection, alm.VoltageDifferenceAlarm, alm.VoltageDifferenceFault);
            UpdateOver(ref l1, ref l2, ref l3, thr.CellVoltageDifferenceThreshold1!.Value, thr.CellVoltageDifferenceThreshold2!.Value, thr.CellVoltageDifferenceThreshold3!.Value, thr.CellVoltageDifferenceRecovery1!.Value, thr.CellVoltageDifferenceRecovery2!.Value, thr.CellVoltageDifferenceRecovery3!.Value, maxVoltList.Max() - minVoltList.Min());
            (alm.VoltageDifferenceProtection, alm.VoltageDifferenceAlarm, alm.VoltageDifferenceFault) = (l1, l2, l3);

            (l1, l2, l3) = (alm.TempDifferenceProtection, alm.TempDifferenceAlarm, alm.TempDifferenceFault);
            UpdateOver(ref l1, ref l2, ref l3, thr.CellTempDifferenceThreshold1!.Value, thr.CellTempDifferenceThreshold2!.Value, thr.CellTempDifferenceThreshold3!.Value, thr.CellTempDifferenceRecovery1!.Value, thr.CellTempDifferenceRecovery2!.Value, thr.CellTempDifferenceRecovery3!.Value, maxTempList.Max() - minTempList.Min());
            (alm.TempDifferenceProtection, alm.TempDifferenceAlarm, alm.TempDifferenceFault) = (l1, l2, l3);

            (l1, l2, l3) = (alm.InsulationProtection, alm.InsulationAlarm, alm.InsulationFault);
            UpdateUnder(ref l1, ref l2, ref l3, thr.InsulationThreshold1!.Value, thr.InsulationThreshold2!.Value, thr.InsulationThreshold3!.Value, thr.InsulationRecovery1!.Value, thr.InsulationRecovery2!.Value, thr.InsulationRecovery3!.Value, insulationValue);
            (alm.InsulationProtection, alm.InsulationAlarm, alm.InsulationFault) = (l1, l2, l3);

            // 端子/极柱高温 → TerminalHighTemp*（汇总 bit11）；门限用 PoleHighTemp*
            (l1, l2, l3) = (alm.TerminalHighTempProtection, alm.TerminalHighTempAlarm, alm.TerminalHighTempFault);
            UpdateOver(ref l1, ref l2, ref l3, thr.PoleHighTempThreshold1!.Value, thr.PoleHighTempThreshold2!.Value, thr.PoleHighTempThreshold3!.Value, thr.PoleHighTempRecovery1!.Value, thr.PoleHighTempRecovery2!.Value, thr.PoleHighTempRecovery3!.Value, poleTempC);
            (alm.TerminalHighTempProtection, alm.TerminalHighTempAlarm, alm.TerminalHighTempFault) = (l1, l2, l3);

            // 高压箱连接器高温 → HVBHighTemp*（汇总 bit12）；同步铜排位便于 summary2
            (l1, l2, l3) = (alm.HVBHighTempProtection, alm.HVBHighTempAlarm, alm.HVBHighTempFault);
            UpdateOver(ref l1, ref l2, ref l3, thr.HVBHighTempThreshold1!.Value, thr.HVBHighTempThreshold2!.Value, thr.HVBHighTempThreshold3!.Value, thr.HVBHighTempRecovery1!.Value, thr.HVBHighTempRecovery2!.Value, thr.HVBHighTempRecovery3!.Value, busbarTempC);
            (alm.HVBHighTempProtection, alm.HVBHighTempAlarm, alm.HVBHighTempFault) = (l1, l2, l3);
            (alm.BatteryBoxBusbarHighTempProtection, alm.BatteryBoxBusbarHighTempAlarm, alm.BatteryBoxBusbarHighTempFault) = (l1, l2, l3);

            // 簇内模组总压差 → TotalVoltageDifference* 门限；写入 summary2 的电压极差位
            (l1, l2, l3) = (alm.BatteryBoxVoltageExtremaDifferenceProtection, alm.BatteryBoxVoltageExtremaDifferenceAlarm, alm.BatteryBoxVoltageExtremaDifferenceFault);
            UpdateOver(ref l1, ref l2, ref l3, thr.TotalVoltageDifferenceThreshold1!.Value, thr.TotalVoltageDifferenceThreshold2!.Value, thr.TotalVoltageDifferenceThreshold3!.Value, thr.TotalVoltageDifferenceRecovery1!.Value, thr.TotalVoltageDifferenceRecovery2!.Value, thr.TotalVoltageDifferenceRecovery3!.Value, (float)clusterState.VoltageImbalance);
            (alm.BatteryBoxVoltageExtremaDifferenceProtection, alm.BatteryBoxVoltageExtremaDifferenceAlarm, alm.BatteryBoxVoltageExtremaDifferenceFault) = (l1, l2, l3);
        }

        /// <summary>
        /// Rack 级汇总：读取各簇告警汇总与 stack 级 SOC/SOH 规则，写回 <see cref="RackState"/> 故障态。
        /// </summary>
        public static void ApplyRackFaultSummary(
            BmsStackProtectionSnapshot stack,
            RackState rack)
        {
            double current = rack.TotalCurrent;
            bool isCharging = IsCharging(current);
            bool isDischarging = IsDischarging(current);
            bool chargeFault = stack.IsChargeFault && isCharging;
            bool dischargeFault = stack.IsDischargeFault && isDischarging;

            if (stack.FaultSummary != 0 && (chargeFault || dischargeFault))
            {
                rack.IsFault = (ushort)((chargeFault, dischargeFault) switch
                {
                    (true, true) => 3,
                    (true, false) => 1,
                    (false, true) => 2,
                    (false, false) => 3
                });
            }
            else
            {
                rack.IsFault = 0;
            }

            if (isCharging && stack.Soc >= 0.95f)
            {
                rack.IsFault = (ushort)(rack.IsFault == 0 ? 1
                                      : rack.IsFault == 2 ? 3
                                      : rack.IsFault);
            }

            if (isDischarging && stack.Soc <= 0.05f)
            {
                rack.IsFault = (ushort)(rack.IsFault == 0 ? 2
                                      : rack.IsFault == 1 ? 3
                                      : rack.IsFault);
            }

            if (stack.Soh <= 0.05f)
                rack.IsFault = 3;

            rack.IsAlarm = stack.AlarmSummary != 0;
            rack.IsProtection = stack.ProtectionSummary != 0;
        }

        /// <summary>电流 &gt; 0 为充电（与物理模型 rack/电芯一致）。</summary>
        internal static bool IsCharging(double current) => current > 0;

        /// <summary>电流 &lt; 0 为放电。</summary>
        internal static bool IsDischarging(double current) => current < 0;

        /// <summary>
        /// 欠量阈值状态机（SOC/欠压等）。清除后再次评估时按当前值<strong>一次性落到对应等级</strong>，
        /// 避免清故障后仍超限却只能从一级慢慢爬升、表现为「清完就不再触发」。
        /// </summary>
        public static void UpdateUnder(ref bool? l1, ref bool? l2, ref bool? l3,
            float t1, float t2, float t3, float r1, float r2, float r3, double val)
        {
            if (l3 == true)
            { if (val > r3) { l3 = false; l2 = true; } }
            else if (l2 == true)
            { if (val <= t3) { l3 = true; l2 = false; } else if (val > r2) { l2 = false; l1 = true; } }
            else if (l1 == true)
            { if (val <= t2) { l2 = true; l1 = false; } else if (val > r1) { l1 = false; } }
            else
            { SnapUnder(ref l1, ref l2, ref l3, t1, t2, t3, val); }
        }

        /// <summary>
        /// 过量阈值状态机（过压/过流等）。清除后再次评估时同样按当前值一次性落到对应等级。
        /// </summary>
        public static void UpdateOver(ref bool? l1, ref bool? l2, ref bool? l3,
            float t1, float t2, float t3, float r1, float r2, float r3, double val)
        {
            if (l3 == true)
            { if (val < r3) { l3 = false; l2 = true; } }
            else if (l2 == true)
            { if (val >= t3) { l3 = true; l2 = false; } else if (val < r2) { l2 = false; l1 = true; } }
            else if (l1 == true)
            { if (val >= t2) { l2 = true; l1 = false; } else if (val < r1) { l1 = false; } }
            else
            { SnapOver(ref l1, ref l2, ref l3, t1, t2, t3, val); }
        }

        private static void SnapUnder(ref bool? l1, ref bool? l2, ref bool? l3, float t1, float t2, float t3, double val)
        {
            if (val <= t3) { l3 = true; l2 = false; l1 = false; }
            else if (val <= t2) { l2 = true; l1 = false; l3 = false; }
            else if (val <= t1) { l1 = true; }
        }

        private static void SnapOver(ref bool? l1, ref bool? l2, ref bool? l3, float t1, float t2, float t3, double val)
        {
            if (val >= t3) { l3 = true; l2 = false; l1 = false; }
            else if (val >= t2) { l2 = true; l1 = false; l3 = false; }
            else if (val >= t1) { l1 = true; }
        }
    }
}
