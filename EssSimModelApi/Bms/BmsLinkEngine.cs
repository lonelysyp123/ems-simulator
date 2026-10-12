using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Diagnostics;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;

namespace EssSimulator.EssSimModelApi.Bms
{
    /// <summary>BMS 并网脉冲与黑启动状态机（供 BmsLinkService 与 DataExchange ControlEffect 共用）。</summary>
    public static class BmsLinkEngine
    {
        private const ushort GridStatusIdle = 0;
        private const ushort GridStatusRunning = 1;
        private const ushort GridStatusSuccess = 2;
        private const ushort GridStatusFailed = 3;

        private const ushort GridConnectCmdConnect = 1;
        private const ushort GridConnectCmdDisconnect = 2;

        private const ushort BlackStartStatusActive = 3;
        private const ushort BlackStartStatusEnterFailed = 4;
        private const ushort BlackStartStatusExited = 5;

        // BatteryStack.SOC 为 0~1 标幺值（与电芯 CellState.SOC、点表 yc11 Scale=1000 一致）
        private const float BlackStartMinSoc = 0.25f;
        private const float BlackStartMaxCoolingSetpointC = 25f;
        private const double VoltageEnergizedThresholdV = 1.0;

        private static readonly Dictionary<int, ushort> LastBlackStartCommand = new();

        public static void ApplyForChannel(int bmsIndex)
        {
            var store = SimulatorHost.Instance;
            var ess = store.Get<EnergyStorageSystem>("ess");
            var bmsData = store.Get<BatteryManagementSystemData>($"bms{bmsIndex + 1}");
            if (ess == null || bmsData?.BatteryStacks == null || bmsData.BatteryStacks.Count == 0)
                return;
            if (bmsIndex >= ess._bmsRackDevices.Count)
                return;

            var stack = bmsData.BatteryStacks[0];
            var bms = ess._bmsRackDevices[bmsIndex];
            ApplyLinkLogic(bmsIndex, stack, bms, ess);
            ApplyBlackStartLogic(bmsIndex, stack, bms, ess, bmsData);
            ApplyFaultClearLogic(bmsIndex, stack, bms);
        }

        public static void ApplyAllChannels()
        {
            var ess = SimulatorHost.Instance.Get<EnergyStorageSystem>("ess");
            if (ess == null) return;

            int count = Math.Min(ess._bmsRackDevices.Count, ess._pcsList.Count);
            for (int i = 0; i < count; i++)
                ApplyForChannel(i);
        }

        /// <summary>手动并网/离网（供 esscmd setbmsN power on|off）。</summary>
        public static bool TrySetGridPower(int bmsIndex0, bool connect, out string message)
        {
            message = string.Empty;
            var store = SimulatorHost.Instance;
            var ess = store.Get<EnergyStorageSystem>("ess");
            var bmsData = store.Get<BatteryManagementSystemData>($"bms{bmsIndex0 + 1}");
            if (ess == null)
            {
                message = "找不到 ess 模型，请确认仿真已启动";
                return false;
            }

            if (bmsData?.BatteryStacks == null || bmsData.BatteryStacks.Count == 0)
            {
                message = $"找不到 bms{bmsIndex0 + 1} 数据";
                return false;
            }

            if (bmsIndex0 >= ess._bmsRackDevices.Count)
            {
                message = $"bms{bmsIndex0 + 1} 超出当前配置范围";
                return false;
            }

            var stack = bmsData.BatteryStacks[0];
            var bms = ess._bmsRackDevices[bmsIndex0];
            var label = BmsLabel(bmsIndex0, bms);

            if (connect)
            {
                if (stack.IsPcsLinked && bms.IsLinked)
                {
                    message = "已处于并网状态";
                    return true;
                }

                if (stack.BMSFaultSummary > 0 || bms.HasBlockingFault)
                {
                    message = bms.HasBlockingFault
                        ? $"并网失败：Rack 故障码={bms.FaultCode}，请先清除故障"
                        : $"并网失败：三级报警汇总={stack.BMSFaultSummary}，请先清除故障";
                    return false;
                }

                AssignGridConnectStatus(label, stack, GridStatusIdle, "esscmd 手动并网，准备执行");
                stack.GridConnectCommand = GridConnectCmdConnect;
                ApplyForChannel(bmsIndex0);

                if (!stack.IsPcsLinked || !bms.IsLinked)
                {
                    message = $"并网失败，GridConnectStatus={stack.GridConnectStatus}";
                    return false;
                }

                message = "并网成功";
                return true;
            }

            if (!stack.IsPcsLinked && !bms.IsLinked)
            {
                message = "已处于离网状态";
                return true;
            }

            stack.GridConnectCommand = GridConnectCmdDisconnect;
            ApplyForChannel(bmsIndex0);
            message = "离网成功";
            return true;
        }

        /// <summary>启动时按 DTO 默认并网状态（GridConnectStatus=2）建立 PCS↔BMS 物理链路。</summary>
        public static void ApplyStartupGridLinks(EnergyStorageSystem ess)
        {
            var store = SimulatorHost.Instance;
            int count = Math.Min(ess._bmsRackDevices.Count, ess._pcsList.Count);
            for (int i = 0; i < count; i++)
            {
                var bmsData = store.Get<BatteryManagementSystemData>($"bms{i + 1}");
                if (bmsData?.BatteryStacks == null || bmsData.BatteryStacks.Count == 0)
                    continue;

                var stack = bmsData.BatteryStacks[0];
                if (stack.GridConnectStatus != GridStatusSuccess && !stack.IsPcsLinked)
                    continue;

                if (stack.IsPcsLinked && ess._bmsRackDevices[i].IsLinked)
                    continue;

                var label = BmsLabel(i, ess._bmsRackDevices[i]);
                SetLinked(ess, i, stack, linked: true, label, "仿真启动，恢复默认并网链路");
                AssignGridConnectStatus(label, stack, GridStatusSuccess, "仿真启动，GridConnectStatus 默认=2");
            }
        }

        private static void ApplyLinkLogic(int bmsIndex, BatteryStack stack, BmsRackDevice bms, EnergyStorageSystem ess)
        {
            var label = BmsLabel(bmsIndex, bms);

            // 故障等级处置：三级故障（BMSFaultSummary）或 Rack IsFault → BMS 下电（断开 PCS 链路）
            // 一级保护 / 二级告警仅置位遥测，不自动离网。
            if (stack.IsPcsLinked && (stack.BMSFaultSummary > 0 || bms.HasBlockingFault))
            {
                string reason = stack.BMSFaultSummary > 0
                    ? $"三级故障汇总=0x{stack.BMSFaultSummary:X}，自动下电"
                    : $"Rack 故障码={bms.FaultCode}，自动下电";
                SetLinked(ess, bmsIndex, stack, linked: false, label, reason);
                AssignGridConnectStatus(label, stack, GridStatusFailed, reason);
            }

            ushort cmd = stack.GridConnectCommand;
            if (cmd == 0)
                return;

            ClearConnectCommandPulse(stack);

            if (cmd == GridConnectCmdDisconnect)
            {
                TryExecuteGridDisconnect(bmsIndex, stack, bms, ess, label);
                return;
            }

            if (cmd != GridConnectCmdConnect)
                return;

            if (stack.GridConnectStatus == GridStatusRunning)
                return;

            if (stack.IsPcsLinked && stack.GridConnectStatus == GridStatusSuccess)
                return;

            AssignGridConnectStatus(label, stack, GridStatusRunning, "收到并网脉冲 yt0=1");
            TryExecuteGridConnect(bmsIndex, stack, bms, ess, label);
        }

        private static void ApplyBlackStartLogic(
            int bmsIndex,
            BatteryStack stack,
            BmsRackDevice bms,
            EnergyStorageSystem ess,
            BatteryManagementSystemData bmsData)
        {
            var label = BmsLabel(bmsIndex, bms);
            ushort cmd = stack.BlackStartCommand;
            LastBlackStartCommand.TryGetValue(bmsIndex, out ushort lastCmd);

            bool enterRequested = cmd == 1 && lastCmd != 1;
            bool exitRequested = cmd == 0 && lastCmd == 1;

            if (enterRequested)
            {
                // 1. 220kV 母线电压检测（实际电压，非断路器位置）
                if (Is220KvBusEnergized(ess))
                {
                    FailBlackStartEnter(label, stack, "220kV母线带电");
                    return;
                }

                // 2. SOC 检测：总 SOC 须 ≥ 25%（SOC 为 0~1 标幺值）
                if (!stack.SOC.HasValue || stack.SOC.Value < BlackStartMinSoc)
                {
                    FailBlackStartEnter(label, stack, $"系统SOC不足{BlackStartMinSoc * 100f:0}%");
                    return;
                }

                // 3. 二级报警检测（一级保护忽略）
                if (stack.BMSAlarmSummary > 0)
                {
                    FailBlackStartEnter(label, stack, $"存在二级报警=0x{stack.BMSAlarmSummary:X}");
                    return;
                }

                // 4. 三级故障检测
                if (stack.BMSFaultSummary > 0)
                {
                    FailBlackStartEnter(label, stack, $"存在三级故障=0x{stack.BMSFaultSummary:X}");
                    return;
                }

                // 5. 储能单元 AC 侧电压检测：须为 0
                int unitIndex = ess.UnitIndexOfPcs(bmsIndex);
                double acVoltage = ess.GetUnitAcBusVoltage(unitIndex);
                if (acVoltage > VoltageEnergizedThresholdV)
                {
                    FailBlackStartEnter(label, stack, $"储能单元AC侧带电（{acVoltage:F1}V）");
                    return;
                }

                // 6. 空调检测：须已启动且制冷设定温度 < 25℃
                if (!IsAirConditionerReady(bmsData))
                {
                    FailBlackStartEnter(label, stack, "空调未启动或制冷设定温度≥25℃");
                    return;
                }

                // PCS 直流链路检测
                if (!stack.IsPcsLinked)
                {
                    AssignGridConnectStatus(label, stack, GridStatusRunning, "黑启动进入前需先并网");
                    if (!TryExecuteGridConnect(bmsIndex, stack, bms, ess, label))
                        FailBlackStartEnter(label, stack, "黑启动前并网失败");
                    else
                        SucceedBlackStartEnter(label, stack);
                }
                else
                {
                    SucceedBlackStartEnter(label, stack);
                }
            }
            else if (exitRequested)
            {
                stack.BlackStartEnterSuccess = 0;
                AssignBlackStartStatus(label, stack, BlackStartStatusExited, "收到黑启动退出命令");
            }

            LastBlackStartCommand[bmsIndex] = stack.BlackStartCommand;
        }

        private static bool TryExecuteGridConnect(
            int bmsIndex,
            BatteryStack stack,
            BmsRackDevice bms,
            EnergyStorageSystem ess,
            string label)
        {
            if (stack.IsPcsLinked || bms.IsLinked)
                return true;

            if (stack.BMSFaultSummary > 0 || bms.HasBlockingFault)
            {
                string reason = stack.BMSFaultSummary > 0
                    ? $"并网拒绝：三级故障汇总=0x{stack.BMSFaultSummary:X}"
                    : $"并网拒绝：Rack 故障码={bms.FaultCode}";
                SetLinked(ess, bmsIndex, stack, linked: false, label, reason);
                AssignGridConnectStatus(label, stack, GridStatusFailed, reason);
                return false;
            }

            SetLinked(ess, bmsIndex, stack, linked: true, label, "并网脉冲执行成功");
            AssignGridConnectStatus(label, stack, GridStatusSuccess, "PCS↔BMS 直流链路已建立");
            return true;
        }

        private static void TryExecuteGridDisconnect(
            int bmsIndex,
            BatteryStack stack,
            BmsRackDevice bms,
            EnergyStorageSystem ess,
            string label)
        {
            if (!stack.IsPcsLinked && !bms.IsLinked)
            {
                AssignGridConnectStatus(label, stack, GridStatusIdle, "收到离网脉冲，当前已断开");
                return;
            }

            SetLinked(ess, bmsIndex, stack, linked: false, label, "收到离网脉冲 yt0=2");
            AssignGridConnectStatus(label, stack, GridStatusIdle, "PCS↔BMS 直流链路已断开");
        }

        private static void SucceedBlackStartEnter(string label, BatteryStack stack)
        {
            AssignBlackStartStatus(label, stack, BlackStartStatusActive, "黑启动进入成功");
            stack.BlackStartEnterSuccess = 1;
        }

        private static void FailBlackStartEnter(string label, BatteryStack stack, string reason)
        {
            AssignBlackStartStatus(label, stack, BlackStartStatusEnterFailed, reason);
            stack.BlackStartEnterSuccess = 0;
            ClearBlackStartCommand(stack);
        }

        private static bool Is220KvBusEnergized(EnergyStorageSystem ess) =>
            ess.PccLineVoltageV > VoltageEnergizedThresholdV;

        private static bool IsAirConditionerReady(BatteryManagementSystemData bmsData)
        {
            if (bmsData?.AirConditioners == null || bmsData.AirConditioners.Count == 0)
                return false;

            var ac = bmsData.AirConditioners[0];
            if (!ac.OnCommand.HasValue || !ac.OnCommand.Value)
                return false;

            if (!ac.CoolingSetpointCommand.HasValue ||
                ac.CoolingSetpointCommand.Value >= BlackStartMaxCoolingSetpointC)
                return false;

            return true;
        }

        private static void ClearConnectCommandPulse(BatteryStack stack) =>
            stack.GridConnectCommand = 0;

        private static void ClearBlackStartCommand(BatteryStack stack) =>
            stack.BlackStartCommand = 0;

        /// <summary>一键复归脉冲处理：检测到 FaultClearCommand=1 时清除充放电方向故障，随后复位脉冲。</summary>
        private static void ApplyFaultClearLogic(int bmsIndex, BatteryStack stack, BmsRackDevice bms)
        {
            if (stack.FaultClearCommand == 0)
                return;

            var label = BmsLabel(bmsIndex, bms);
            ushort prevFaultSummary = stack.BMSFaultSummary;
            var rackState = bms.Rack.GetRackState();
            ushort prevRackFault = rackState?.IsFault ?? 0;

            // 先复位脉冲，确保 ControlFeedbackPipeline 回写 0 到 Modbus
            stack.FaultClearCommand = 0;

            if (BmsFaultClearEngine.TryClearFaults(bmsIndex, out var message))
            {
                SimStateChangeLogger.BmsStateChanged(
                    label,
                    "FaultClear",
                    $"BMSFaultSummary=0x{prevFaultSummary:X}, RackIsFault={SimStateChangeLogger.FormatRackFault(prevRackFault)}",
                    "已清除充放电方向故障",
                    "一键复归");
            }
            else
            {
                SimStateChangeLogger.BmsStateChanged(
                    label,
                    "FaultClear",
                    $"BMSFaultSummary=0x{prevFaultSummary:X}, RackIsFault={SimStateChangeLogger.FormatRackFault(prevRackFault)}",
                    $"清除失败：{message}",
                    "一键复归");
            }
        }

        private static void SetLinked(
            EnergyStorageSystem ess,
            int bmsIndex,
            BatteryStack stack,
            bool linked,
            string label,
            string reason)
        {
            bool prev = stack.IsPcsLinked;
            stack.IsPcsLinked = linked;
            ess.SetBmsPcsLinked(bmsIndex, linked);
            SimStateChangeLogger.BmsStateChanged(label, "IsPcsLinked", prev, linked, reason);
        }

        private static void AssignGridConnectStatus(string label, BatteryStack stack, ushort status, string reason)
        {
            ushort prev = stack.GridConnectStatus;
            stack.GridConnectStatus = status;
            SimStateChangeLogger.BmsStateChanged(
                label,
                "GridConnectStatus",
                SimStateChangeLogger.FormatGridConnectStatus(prev),
                SimStateChangeLogger.FormatGridConnectStatus(status),
                reason);
        }

        private static void AssignBlackStartStatus(string label, BatteryStack stack, ushort status, string reason)
        {
            ushort prev = stack.BlackStartStatus;
            stack.BlackStartStatus = status;
            SimStateChangeLogger.BmsStateChanged(
                label,
                "BlackStartStatus",
                SimStateChangeLogger.FormatBlackStartStatus(prev),
                SimStateChangeLogger.FormatBlackStartStatus(status),
                reason);
        }

        private static string BmsLabel(int bmsIndex, BmsRackDevice bms) =>
            string.IsNullOrEmpty(bms.DisplayLabel) ? $"bms{bmsIndex + 1}" : bms.DisplayLabel;
    }
}
