using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Diagnostics;
using EssSimulator.EssDeviceSimModel.Devices;
using EssSimulator.EssSimModelApi.BatteryManagementSystem;
using EssSimulator.EssSimModelApi.Mappers;

namespace EssSimulator.EssSimModelApi.Bms
{
    /// <summary>清除 BMS 充放电方向故障（供 esscmd bmsN fault clear）。</summary>
    public static class BmsFaultClearEngine
    {
        /// <summary>
        /// 待机状态下清除充放电方向内部故障，恢复可并网条件。
        /// 清除仅为一次性复位；若工况恢复充/放电且仍超限，保护状态机会按当前值重新落入对应等级。
        /// </summary>
        public static bool TryClearFaults(int bmsIndex0, out string message)
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

            var bms = ess._bmsRackDevices[bmsIndex0];
            var stack = bmsData.BatteryStacks[0];
            var rackState = bms.Rack.GetRackState();
            ushort prevFaultSummary = stack.BMSFaultSummary;
            ushort prevRackFault = rackState?.IsFault ?? 0;

            if (!BmsMapper.TryClearChargeDischargeFaults(bmsData, bms, out message))
                return false;

            if (rackState == null)
                return true;

            var label = string.IsNullOrEmpty(bms.DisplayLabel) ? $"bms{bmsIndex0 + 1}" : bms.DisplayLabel;

            SimStateChangeLogger.BmsStateChanged(
                label,
                "FaultClear",
                $"BMSFaultSummary=0x{prevFaultSummary:X}, RackIsFault={SimStateChangeLogger.FormatRackFault(prevRackFault)}",
                "已清除充放电方向故障",
                "esscmd bmsN fault clear");

            return true;
        }
    }
}
