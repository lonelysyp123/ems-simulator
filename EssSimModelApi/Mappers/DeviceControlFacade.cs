using EssSimulator.Core;
using EssSimulator.Display;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssSimModelApi.EnergyManagementSystem;

namespace EssSimulator.EssSimModelApi.Mappers
{
    /// <summary>
    /// 内部设备直控门面：Web/CLI 内部控制不经过点表写点，直接作用于仿真设备与 EMU 镜像 DTO，
    /// 复用 <see cref="EmuCommandPipeline"/> 联锁链；点表存在对应点位时由
    /// ControlFeedbackPipeline/TelemetryPipeline 自动冒泡回 Modbus 寄存器，点位缺失则安全跳过。
    /// 外部 EMS 写 Modbus 的下行路径（ControlPipeline → Effects）在第三方 EMS 占用时由
    /// <see cref="ExternalControlGate"/> 拦截。
    /// </summary>
    public static class DeviceControlFacade
    {
        /// <summary>PCS 启停：写 EMU 镜像启停位后经共享命令链下发（联锁/故障锁存由链内把关）。</summary>
        public static bool TrySetPcsRun(int pcs1Based, bool run, out string message)
        {
            if (!ExternalControlGate.TryAllow(out message))
                return false;
            message = string.Empty;
            if (!TryResolvePcsMirror(pcs1Based, out var emu, out int unit1Based, out int slot, out message))
                return false;

            emu!.PcsList[slot].pcsOnOffSwitch = run;
            if (!EmuCommandPipeline.TryApplyUnit(unit1Based))
            {
                message = "找不到 ess 模型，请确认仿真已启动";
                return false;
            }

            message = $"PCS{pcs1Based} 启停位已写 {(run ? 1 : 0)}（emu 单元 {unit1Based}）";
            return true;
        }

        /// <summary>PCS 有功/无功设定（kW/kvar，工程值；缺省项保留镜像现值），经共享命令链下发。</summary>
        public static bool TrySetPcsPower(int pcs1Based, double? activeKw, double? reactiveKvar, out string message)
        {
            if (!ExternalControlGate.TryAllow(out message))
                return false;
            message = string.Empty;
            if (activeKw == null && reactiveKvar == null)
            {
                message = "有功与无功至少提供一项";
                return false;
            }

            if (!TryResolvePcsMirror(pcs1Based, out var emu, out int unit1Based, out int slot, out message))
                return false;

            var pcs = emu!.PcsList[slot];
            if (activeKw.HasValue)
                pcs.PCSActivePowerSetting = (float)activeKw.Value;
            if (reactiveKvar.HasValue)
                pcs.PCSReactivePowerSetting = (float)reactiveKvar.Value;

            if (!EmuCommandPipeline.TryApplyUnit(unit1Based))
            {
                message = "找不到 ess 模型，请确认仿真已启动";
                return false;
            }

            message = $"PCS{pcs1Based} 设定 P={pcs.PCSActivePowerSetting:0.##} kW · Q={pcs.PCSReactivePowerSetting:0.##} kvar（emu 单元 {unit1Based}）";
            return true;
        }

        /// <summary>单元高压断路器：写 EMU 级 Breaker.Closed（并同步 PowerOnOff 别名）并驱动电气网络。</summary>
        public static bool TrySetUnitBreaker(int unit1Based, bool closed, out string message)
        {
            if (!ExternalControlGate.TryAllow(out message))
                return false;
            message = string.Empty;
            if (unit1Based < 1)
            {
                message = "单元号须 ≥ 1";
                return false;
            }

            var ess = SimulatorHost.Instance.TryGetEss();
            var emu = SimulatorHost.Instance.TryGetEmu(unit1Based);
            if (ess == null || emu == null)
            {
                message = $"找不到 emu{unit1Based} 或 ess（请确认仿真已启动且单元号在配置范围内）";
                return false;
            }

            ushort value = (ushort)(closed ? 1 : 0);
            if (closed && ess.IsUnitBreakerTripped(unit1Based - 1))
            {
                emu.Breaker.Closed = 0;
                emu.Emu.PowerOnOff = 1;
                UiSnapshotNotifier.RequestImmediatePush();
                message = $"单元 {unit1Based} 高压断路器跳闸锁存，需复位后再合";
                return true;
            }

            emu.Breaker.Closed = value;
            emu.Emu.PowerOnOff = value;
            ess.SetUnitBreakerClosed(unit1Based - 1, closed);
            UiSnapshotNotifier.RequestImmediatePush();

            message = $"单元 {unit1Based} 高压断路器{(closed ? "合闸" : "分闸")}";
            return true;
        }

        /// <summary>复位单元高压跳闸锁存（不合闸；合闸仍走 AA / TrySetUnitBreaker）。</summary>
        public static bool TryResetUnitBreakerTrip(int unit1Based, out string message)
        {
            if (!ExternalControlGate.TryAllow(out message))
                return false;
            message = string.Empty;
            if (unit1Based < 1)
            {
                message = "单元号须 ≥ 1";
                return false;
            }

            var ess = SimulatorHost.Instance.TryGetEss();
            if (ess == null)
            {
                message = "找不到 ess 模型，请确认仿真已启动";
                return false;
            }

            ess.ResetUnitBreakerTrip(unit1Based - 1);
            var emu = SimulatorHost.Instance.TryGetEmu(unit1Based);
            if (emu != null)
                emu.Breaker.Closed = (ushort)(ess.IsUnitBreakerClosed(unit1Based - 1) ? 1 : 0);
            UiSnapshotNotifier.RequestImmediatePush();
            message = $"单元 {unit1Based} 高压断路器跳闸已复位";
            return true;
        }

        /// <summary>光伏启停（直驱 PvLogger → PvUnitDevice）。</summary>
        public static bool TrySetPvRun(int pv1Based, bool run, out string message)
        {
            if (!ExternalControlGate.TryAllow(out message))
                return false;
            var ess = SimulatorHost.Instance.TryGetEss();
            if (ess == null)
            {
                message = "找不到 ess 模型，请确认仿真已启动";
                return false;
            }

            if (!ess.TrySetPvRun(pv1Based, run, out message))
                return false;

            UiSnapshotNotifier.RequestImmediatePush();
            return true;
        }

        /// <summary>光伏有功/无功设定（kW/kvar，缺省项保留现值）。</summary>
        public static bool TrySetPvPower(int pv1Based, double? activeKw, double? reactiveKvar, out string message)
        {
            if (!ExternalControlGate.TryAllow(out message))
                return false;
            var ess = SimulatorHost.Instance.TryGetEss();
            if (ess == null)
            {
                message = "找不到 ess 模型，请确认仿真已启动";
                return false;
            }

            if (!ess.TrySetPvPower(pv1Based, activeKw, reactiveKvar, out message))
                return false;

            UiSnapshotNotifier.RequestImmediatePush();
            return true;
        }

        /// <summary>PCS 全局编号（1 基）→ EMU 镜像与槽位定位。</summary>
        private static bool TryResolvePcsMirror(
            int pcs1Based,
            out EnergyManagementData? emu,
            out int unit1Based,
            out int slot,
            out string message)
        {
            emu = null;
            unit1Based = 0;
            slot = 0;
            message = string.Empty;

            if (pcs1Based < 1)
            {
                message = "PCS 编号须 ≥ 1";
                return false;
            }

            var layout = GuiSimDataAccess.GetPcsPerUnit();
            unit1Based = PcsUnitLayout.UnitIndexOf(layout, pcs1Based - 1) + 1;
            slot = PcsUnitLayout.SlotOfChannel(layout, pcs1Based - 1);

            emu = SimulatorHost.Instance.TryGetEmu(unit1Based);
            if (emu == null)
            {
                message = $"找不到 emu{unit1Based}（PCS{pcs1Based} 超出当前配置范围或仿真未启动）";
                return false;
            }

            if (slot < 0 || slot >= emu.PcsList.Count)
            {
                message = $"PCS{pcs1Based} 槽位越界（emu{unit1Based} 共 {emu.PcsList.Count} 台）";
                return false;
            }

            return true;
        }
    }
}
