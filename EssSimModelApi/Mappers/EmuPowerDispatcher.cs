using EssSimulator.Core;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;

namespace EssSimulator.EssSimModelApi.Mappers
{
    /// <summary>
    /// EMU 虚拟模型功率派发：系统级总有功/无功目标按所属 PCS 台数简单均分。
    /// 仅在远程使能（syst4=1）且远程模式（syst5=1）时生效；
    /// 本地模式下保留单 PCS 点位直写值（点表 yt 系列）。
    /// </summary>
    public static class EmuPowerDispatcher
    {
        /// <summary>EMU 是否处于远程集中控制（使能 + 远程模式）。</summary>
        public static bool IsRemoteDispatchActive(EnergyManagementData emu) =>
            emu.Emu.RemoteControlEnable == 1 && emu.Emu.RemoteControlMode == 1;

        /// <summary>
        /// 将 EMU 级目标 P/Q 均分写入各 PCS 的功率设定。
        /// 限幅由 <see cref="EssDeviceSimModel.Devices.PcsDevice.SetPowerCommand"/> 既有逻辑兜底。
        /// </summary>
        public static void Dispatch(EnergyManagementData emu)
        {
            var list = emu.PcsList;
            if (list == null || list.Count == 0)
                return;
            if (ExternalControlGate.Owner == ExternalControlOwner.EmsStrategy)
                return;
            if (!IsRemoteDispatchActive(emu))
                return;

            float pEach = emu.Emu.TargetActivePower / list.Count;
            float qEach = emu.Emu.TargetReactivePower / list.Count;
            foreach (var pcs in list)
            {
                pcs.PCSActivePowerSetting = pEach;
                pcs.PCSReactivePowerSetting = qEach;
            }
        }
    }

    /// <summary>
    /// EMU 系统操作（syst6）与黑启动写入（syst7）的批量语义应用（边沿生效）。
    /// syst6：3启动=全部 PCS 开机；4停止=全部停机；5待机=保持运行但目标功率清零；
    ///        6重置=全部停机（启停写 0 同时清除 PCS 故障锁存）。
    /// syst7：按单元批量置位所属 PCS 的 BlackStartEnabled（安全联锁仍由 ApplyEmuCommands 把关）。
    /// </summary>
    public static class EmuSystemOperationApplier
    {
        public const int OpStart = 3;
        public const int OpStop = 4;
        public const int OpStandby = 5;
        public const int OpReset = 6;

        public static void Apply(EnergyManagementData emu)
        {
            ApplySystemOperation(emu);
            ApplyBlackStartWrite(emu);
        }

        private static void ApplySystemOperation(EnergyManagementData emu)
        {
            int op = emu.Emu.SystemOperation;
            if (op == 0 || op == emu.Emu.AppliedSystemOperation)
                return;
            emu.Emu.AppliedSystemOperation = op;

            var list = emu.PcsList;
            if (list == null || list.Count == 0)
                return;

            switch (op)
            {
                case OpStart:
                    foreach (var pcs in list)
                        pcs.pcsOnOffSwitch = true;
                    break;

                case OpStop:
                case OpReset:
                    // 启停写 0 → ApplyEmuCommands 触发 SyncExternalRunCommand(false)，
                    // 该路径会清除故障锁存，因此重置无需额外设备层 API
                    foreach (var pcs in list)
                        pcs.pcsOnOffSwitch = false;
                    break;

                case OpStandby:
                    foreach (var pcs in list)
                    {
                        pcs.PCSActivePowerSetting = 0;
                        pcs.PCSReactivePowerSetting = 0;
                    }
                    emu.Emu.TargetActivePower = 0;
                    emu.Emu.TargetReactivePower = 0;
                    break;
            }
        }

        private static void ApplyBlackStartWrite(EnergyManagementData emu)
        {
            int write = emu.Emu.BlackStartModeWrite;
            if (write == emu.Emu.AppliedBlackStartWrite)
                return;
            emu.Emu.AppliedBlackStartWrite = write;

            var list = emu.PcsList;
            if (list == null || list.Count == 0)
                return;

            bool enable = write == 1;
            foreach (var pcs in list)
                pcs.BlackStartEnabled = enable;
        }
    }
}
