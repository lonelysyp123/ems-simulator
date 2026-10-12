using System;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;

namespace EssSimulator.EssSimModelApi.Mappers
{
    /// <summary>
    /// EMU 机组命令应用链：EMU 级系统操作/黑启动写入与功率均分 → 下发各 PCS → 设备状态回写 DTO。
    /// 协议层控制效果（EmuPcsControlEffect）与内部设备直控门面（DeviceControlFacade）共用，
    /// 保证外部 EMS 写点与内部控制走同一套联锁逻辑。
    /// </summary>
    public static class EmuCommandPipeline
    {
        /// <summary>
        /// 将 emu{n} 镜像当前命令态应用到物理模型；模型或镜像缺失返回 false。
        /// <paramref name="refreshTelemetry"/> 为协议会话注入的遥测即时刷新回调（内部控制可为 null）。
        /// </summary>
        public static bool TryApplyUnit(int unit1Based, Action? refreshTelemetry = null)
        {
            var ess = SimulatorHost.Instance.Get<EnergyStorageSystem>("ess");
            var emu = SimulatorHost.Instance.Get<EnergyManagementData>($"emu{unit1Based}");
            if (ess == null || emu == null)
                return false;

            int pcsBase = ess.PcsBaseIndexOfUnit(unit1Based - 1);
            // 先跑 EMU 级系统操作/黑启动写入与功率均分，再下发到各 PCS
            EmuSystemOperationApplier.Apply(emu);
            EmuPowerDispatcher.Dispatch(emu);
            PcsMapper.ApplyEmuCommands(emu, ess, pcsBase);
            PcsEmuSynchronizer.SyncPcsStateFromModel(ess, emu, pcsBase);
            refreshTelemetry?.Invoke();
            UiSnapshotNotifier.RequestImmediatePush();
            return true;
        }
    }
}
