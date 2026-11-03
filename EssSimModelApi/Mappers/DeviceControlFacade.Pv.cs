using System;
using System.Collections.Generic;
using System.Reflection;
using EssSimulator.Core;
using EssSimulator.EssDeviceSimModel;
using EssSimulator.EssDeviceSimModel.Model;
using EssSimulator.EssDeviceSimModel.Pv;
using EssSimulator.EssSimModelApi.Mappers;
using EssSimulator.LocalControl;

namespace EssSimulator.EssSimModelApi.Mappers
{
    /// <summary>
    /// 光伏单元/逆变器的控制门面：逻辑设定与执行设定分离、单机最大放电上限、
    /// Logger 最大可调发布。与 PCS 那半边拆成两个 partial 文件。
    /// </summary>
    public static partial class DeviceControlFacade
    {
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

            RebalancePvExecution(ess, pv1Based);
            UiSnapshotNotifier.RequestImmediatePush();
            return true;
        }

        /// <summary>光伏有功/无功设定（kW/kvar，缺省项保留现值）。上限打开时有功不超过该上限。</summary>
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

            if (activeKw != null && pv1Based >= 1 && pv1Based <= ess.PvUnits.Count)
            {
                PvExecutionShare.CaptureAll(ess.PvUnits[pv1Based - 1]);
                ReapplyActiveCap(ess, pv1Based, captureCurrent: true);
            }

            RebalancePvExecution(ess, pv1Based);
            UiSnapshotNotifier.RequestImmediatePush();
            return true;
        }

        /// <summary>设定一台光伏逆变器的有功/无功。有功受设备最大功率约束，不受可发约束。</summary>
        public static bool TrySetPvInverterPower(
            int pv1Based,
            int inverter1Based,
            double? activeKw,
            double? reactiveKvar,
            out string message)
        {
            if (!ExternalControlGate.TryAllow(out message))
                return false;
            if (!TryGetPvInverter(pv1Based, inverter1Based, out var ess, out var inv, out int inv0, out message))
                return false;

            double setP = activeKw ?? ReadDisplayedActiveSetpoint(pv1Based, inv0, inv.ActivePowerSettingKw);
            double setQ = reactiveKvar ?? inv.ReactivePowerSettingKvar;
            if (activeKw != null && !AcceptsActiveSetpoint(inv, activeKw.Value, out var maxP))
            {
                message = $"有功设定须在 0～{maxP:0.##} kW";
                return false;
            }

            double maxQ = inv.RatedReactivePowerKvar;
            if (reactiveKvar != null && Math.Abs(reactiveKvar.Value) > maxQ)
            {
                message = $"无功设定须在 ±{maxQ:0.##} kvar";
                return false;
            }

            if (activeKw != null)
            {
                var unit = ess!.PvUnits[pv1Based - 1];
                PvExecutionShare.EnsureCaptured(unit);
                PvExecutionShare.CaptureOne(unit, inv0, setP);
            }

            lock (CapGate)
            {
                if (TryGetInverterCap(pv1Based, inv0, out var cap))
                {
                    if (activeKw != null)
                        cap.RememberedActiveKw = setP;
                    ApplyInverterCap(inv, cap, setQ);
                }
                else
                {
                    inv.SetPowerCommand(setP, setQ);
                }
            }

            if (activeKw != null)
                RebalancePvExecution(ess!, pv1Based);

            UiSnapshotNotifier.RequestImmediatePush();
            message = $"光伏单元 {pv1Based} 逆变器 {inverter1Based} P={setP:0.##} kW · Q={setQ:0.##} kvar";
            return true;
        }

        /// <summary>启停一台光伏逆变器；逻辑设定不变，停机逆变器的份额由同单元运行中的逆变器分担。</summary>
        public static bool TrySetPvInverterRun(int pv1Based, int inverter1Based, bool run, out string message)
        {
            if (!ExternalControlGate.TryAllow(out message))
                return false;
            if (!TryGetPvInverter(pv1Based, inverter1Based, out var ess, out var inv, out _, out message))
                return false;

            inv.SyncExternalRunCommand(run);
            RebalancePvExecution(ess!, pv1Based);
            UiSnapshotNotifier.RequestImmediatePush();
            message = $"光伏单元 {pv1Based} 逆变器 {inverter1Based} {(run ? "启动" : "停机")}";
            return true;
        }

        /// <summary>给单元内每台逆变器各自设同一最大放电上限。已有的单台上限会被覆盖成这个值。</summary>
        public static bool TrySetPvMaxDischarge(int pv1Based, double capKw, out string message)
        {
            if (capKw < 0)
            {
                message = "最大放电功率须 ≥ 0";
                return false;
            }

            if (!ExternalControlGate.TryAllow(out message))
                return false;
            if (!TryGetPvUnit(pv1Based, out var ess, out var unit, out message))
                return false;

            PvExecutionShare.EnsureCaptured(unit);
            lock (CapGate)
            {
                for (int i = 0; i < unit.Inverters.Count; i++)
                    SetInverterCap(ess, pv1Based, i, capKw);
            }

            PublishLoggerMaxAdjustable(ess, pv1Based, force: true);
            RebalancePvExecution(ess, pv1Based);
            UiSnapshotNotifier.RequestImmediatePush();
            message = $"光伏单元 {pv1Based} 每台逆变器最大放电上限 {capKw:0.##} kW";
            return true;
        }

        /// <summary>只给一台逆变器设最大放电上限。有功设定保持不变，执行命令不超过该值。</summary>
        public static bool TrySetPvInverterMaxDischarge(
            int pv1Based,
            int inverter1Based,
            double capKw,
            out string message)
        {
            if (capKw < 0)
            {
                message = "最大放电功率须 ≥ 0";
                return false;
            }

            if (!ExternalControlGate.TryAllow(out message))
                return false;
            if (!TryGetPvInverter(pv1Based, inverter1Based, out var ess, out _, out int inv0, out message))
                return false;

            PvExecutionShare.EnsureCaptured(ess!.PvUnits[pv1Based - 1]);
            lock (CapGate)
                SetInverterCap(ess!, pv1Based, inv0, capKw);

            PublishLoggerMaxAdjustable(ess!, pv1Based, force: true);
            RebalancePvExecution(ess!, pv1Based);
            UiSnapshotNotifier.RequestImmediatePush();
            message = $"光伏单元 {pv1Based} 逆变器 {inverter1Based} 最大放电上限 {capKw:0.##} kW";
            return true;
        }

        /// <summary>关闭单元内全部逆变器的上限，并写回各自记住的有功设定。</summary>
        public static bool TryClearPvMaxDischarge(int pv1Based, out string message)
        {
            if (!ExternalControlGate.TryAllow(out message))
                return false;
            if (!TryGetPvUnit(pv1Based, out var ess, out var unit, out message))
                return false;

            lock (CapGate)
            {
                if (!DischargeCaps.TryGetValue(pv1Based, out var caps) || caps.Count == 0)
                {
                    message = $"光伏单元 {pv1Based} 最大放电上限未打开";
                    return true;
                }

                foreach (var pair in caps)
                {
                    if (pair.Key < 0 || pair.Key >= unit.Inverters.Count)
                        continue;
                    var inv = unit.Inverters[pair.Key];
                    inv.SetPowerCommand(pair.Value.RememberedActiveKw, inv.ReactivePowerSettingKvar);
                }

                DischargeCaps.Remove(pv1Based);
            }

            PublishLoggerMaxAdjustable(ess, pv1Based, force: true);
            RebalancePvExecution(ess, pv1Based);
            UiSnapshotNotifier.RequestImmediatePush();
            message = $"光伏单元 {pv1Based} 已取消最大放电上限";
            return true;
        }

        /// <summary>关闭一台逆变器的上限，并写回它记住的有功设定。</summary>
        public static bool TryClearPvInverterMaxDischarge(int pv1Based, int inverter1Based, out string message)
        {
            if (!ExternalControlGate.TryAllow(out message))
                return false;
            if (!TryGetPvInverter(pv1Based, inverter1Based, out var ess, out var inv, out int inv0, out message))
                return false;

            lock (CapGate)
            {
                if (!DischargeCaps.TryGetValue(pv1Based, out var caps) ||
                    !caps.Remove(inv0, out var cap))
                {
                    message = $"光伏单元 {pv1Based} 逆变器 {inverter1Based} 最大放电上限未打开";
                    return true;
                }

                if (caps.Count == 0)
                    DischargeCaps.Remove(pv1Based);
                inv.SetPowerCommand(cap.RememberedActiveKw, inv.ReactivePowerSettingKvar);
            }

            PublishLoggerMaxAdjustable(ess!, pv1Based, force: true);
            RebalancePvExecution(ess!, pv1Based);
            UiSnapshotNotifier.RequestImmediatePush();
            message = $"光伏单元 {pv1Based} 逆变器 {inverter1Based} 已取消最大放电上限";
            return true;
        }

        public static bool TryReadPvMaxDischarge(int pv1Based, out double capKw)
        {
            lock (CapGate)
            {
                capKw = 0;
                if (!DischargeCaps.TryGetValue(pv1Based, out var caps) || caps.Count == 0)
                    return false;

                var ess = SimulatorHost.Instance.TryGetEss();
                if (ess == null || pv1Based < 1 || pv1Based > ess.PvUnits.Count)
                    return false;
                if (caps.Count != ess.PvUnits[pv1Based - 1].Inverters.Count)
                    return false;

                double first = caps.Values.First().CapKw;
                if (caps.Values.Any(c => Math.Abs(c.CapKw - first) > 1e-6))
                    return false;
                capKw = first;
                return true;
            }
        }

        public static bool TryReadPvInverterMaxDischarge(int pv1Based, int inverter0Based, out double capKw)
        {
            lock (CapGate)
            {
                if (TryGetInverterCap(pv1Based, inverter0Based, out var cap))
                {
                    capKw = cap.CapKw;
                    return true;
                }
            }

            capKw = 0;
            return false;
        }

        /// <summary>卡片上显示的有功设定。上限打开时是记住的原值，不是缩小后的执行命令。</summary>
        public static bool TryReadPvInverterActiveSetpoint(int pv1Based, int inverter0Based, out double activeKw)
        {
            var essForShare = SimulatorHost.Instance.TryGetEss();
            if (essForShare != null &&
                pv1Based >= 1 && pv1Based <= essForShare.PvUnits.Count &&
                PvExecutionShare.TryGet(essForShare.PvUnits[pv1Based - 1], inverter0Based, out activeKw))
                return true;

            lock (CapGate)
            {
                if (TryGetInverterCap(pv1Based, inverter0Based, out var cap))
                {
                    activeKw = cap.RememberedActiveKw;
                    return true;
                }
            }

            var ess = SimulatorHost.Instance.TryGetEss();
            if (ess == null || pv1Based < 1 || pv1Based > ess.PvUnits.Count)
            {
                activeKw = 0;
                return false;
            }

            var inverters = ess.PvUnits[pv1Based - 1].Inverters;
            if (inverter0Based < 0 || inverter0Based >= inverters.Count)
            {
                activeKw = 0;
                return false;
            }

            activeKw = inverters[inverter0Based].ActivePowerSettingKw;
            return true;
        }

        private static readonly MethodInfo? MaxAdjustableSetter =
            typeof(PvLogger).GetProperty(nameof(PvLogger.MaxAdjustableActivePowerKw))?.GetSetMethod(nonPublic: true);

        /// <summary>
        /// 数采最大可调有功：某台逆变器的最大可发打开时用该上限，关闭时用辐照算出的可发。
        /// 模型每步会按辐照重写，步进结束后只改写仍有上限的单元。
        /// </summary>
        internal static void PublishLoggerMaxAdjustable(EnergyStorageSystem? ess)
        {
            if (ess == null)
                return;
            for (int i = 0; i < ess.PvUnits.Count; i++)
                PublishLoggerMaxAdjustable(ess, i + 1, force: false);
        }

        /// <summary>force 为 true 时即使上限都已关闭也按辐照重算，供开关刚关掉时立刻恢复。</summary>
        private static void PublishLoggerMaxAdjustable(EnergyStorageSystem ess, int pv1Based, bool force)
        {
            if (MaxAdjustableSetter == null || pv1Based < 1 || pv1Based > ess.PvUnits.Count)
                return;

            var unit = ess.PvUnits[pv1Based - 1];
            lock (CapGate)
            {
                DischargeCaps.TryGetValue(pv1Based, out var caps);
                if ((caps == null || caps.Count == 0) && !force)
                    return;

                double sum = 0;
                for (int i = 0; i < unit.Inverters.Count; i++)
                {
                    var inv = unit.Inverters[i];
                    if (!inv.IsExternalRunCommand || inv.GetCurrentState().Mode != OperationMode.Normal)
                        continue;
                    string reason = inv.LimitReason;
                    if (reason == "电网断开" || reason == "停机")
                        continue;

                    double basis = inv.AvailableAcPowerKw;
                    if (caps != null && caps.TryGetValue(i, out var cap))
                        basis = cap.CapKw;
                    sum += Math.Min(inv.RatedPowerKw, Math.Max(0, basis));
                }

                MaxAdjustableSetter.Invoke(unit.Logger, new object[] { sum });
            }
        }

        /// <summary>Modbus 写点已经落到模型之后调用。Logger 有功会先被均分到各台，这里再按停机和最大可发重分执行设定。</summary>
        internal static void OnPvModelControlWritten(string fullPath)
        {
            var ess = SimulatorHost.Instance.TryGetEss();
            if (ess == null || string.IsNullOrEmpty(fullPath))
                return;

            int dot = fullPath.IndexOf('.');
            if (dot <= 2 || !fullPath.StartsWith("pv", StringComparison.Ordinal))
                return;
            if (!int.TryParse(fullPath.AsSpan(2, dot - 2), out int pv1Based))
                return;
            if (pv1Based < 1 || pv1Based > ess.PvUnits.Count)
                return;

            string prop = fullPath[(dot + 1)..];
            var unit = ess.PvUnits[pv1Based - 1];
            if (prop is "Logger.SubarrayActivePowerKw" or "Logger.SubarrayActivePowerPercent")
            {
                PvExecutionShare.CaptureAll(unit);
            }
            else if (TryParseInverterControl(prop, out int index, out bool activePower))
            {
                if (activePower && (uint)index < (uint)unit.Inverters.Count)
                    PvExecutionShare.CaptureOne(unit, index, unit.Inverters[index].ActivePowerSettingKw);
            }
            else if (prop != "Logger.SubarrayOnOff")
            {
                return;
            }

            RebalancePvExecution(ess, pv1Based);
        }

        internal static void RebalancePvExecution(EnergyStorageSystem ess)
        {
            for (int i = 0; i < ess.PvUnits.Count; i++)
                RebalancePvExecution(ess, i + 1);
        }

        private static void RebalancePvExecution(EnergyStorageSystem ess, int pv1Based)
        {
            if (pv1Based < 1 || pv1Based > ess.PvUnits.Count)
                return;
            PvExecutionShare.Rebalance(
                ess.PvUnits[pv1Based - 1],
                index => TryReadPvInverterMaxDischarge(pv1Based, index, out double cap) ? cap : null);
        }

        private static bool TryParseInverterControl(string prop, out int index, out bool activePower)
        {
            index = -1;
            activePower = false;
            const string prefix = "Inverters[";
            if (!prop.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            int end = prop.IndexOf(']');
            if (end <= prefix.Length ||
                !int.TryParse(prop.AsSpan(prefix.Length, end - prefix.Length), out index))
                return false;

            string tail = prop[(end + 1)..];
            if (tail == ".Protocol.RunCommand")
                return true;
            if (tail == ".Protocol.ActivePowerSettingKw")
            {
                activePower = true;
                return true;
            }

            return false;
        }

        private static void ReapplyActiveCap(EnergyStorageSystem ess, int pv1Based, bool captureCurrent)
        {
            lock (CapGate)
            {
                if (!DischargeCaps.TryGetValue(pv1Based, out var caps) || caps.Count == 0)
                    return;
                var unit = ess.PvUnits[pv1Based - 1];
                foreach (var pair in caps)
                {
                    if (pair.Key < 0 || pair.Key >= unit.Inverters.Count)
                        continue;
                    if (captureCurrent)
                        pair.Value.RememberedActiveKw = unit.Inverters[pair.Key].ActivePowerSettingKw;
                    ApplyInverterCap(unit.Inverters[pair.Key], pair.Value, null);
                }
            }
        }

        private static void SetInverterCap(EnergyStorageSystem ess, int pv1Based, int inverter0Based, double capKw)
        {
            var inv = ess.PvUnits[pv1Based - 1].Inverters[inverter0Based];
            if (!DischargeCaps.TryGetValue(pv1Based, out var caps))
            {
                caps = new Dictionary<int, InverterDischargeCap>();
                DischargeCaps[pv1Based] = caps;
            }

            if (!caps.TryGetValue(inverter0Based, out var cap))
            {
                cap = new InverterDischargeCap { RememberedActiveKw = inv.ActivePowerSettingKw };
                caps[inverter0Based] = cap;
            }

            cap.CapKw = capKw;
            ApplyInverterCap(inv, cap, null);
        }

        private static void ApplyInverterCap(
            EssSimulator.EssDeviceSimModel.Pv.PvInverterDevice inv,
            InverterDischargeCap cap,
            double? reactiveKvar)
        {
            double target = Math.Min(Math.Max(0, cap.RememberedActiveKw), cap.CapKw);
            double q = reactiveKvar ?? inv.ReactivePowerSettingKvar;
            inv.SetPowerCommand(target, q);
        }

        private static bool TryGetInverterCap(int pv1Based, int inverter0Based, out InverterDischargeCap cap)
        {
            cap = null!;
            return DischargeCaps.TryGetValue(pv1Based, out var caps) && caps.TryGetValue(inverter0Based, out cap!);
        }

        private static bool TryGetPvUnit(
            int pv1Based,
            out EnergyStorageSystem ess,
            out PvUnitDevice unit,
            out string message)
        {
            ess = null!;
            unit = null!;
            var found = SimulatorHost.Instance.TryGetEss();
            if (found == null)
            {
                message = "找不到 ess 模型，请确认仿真已启动";
                return false;
            }

            if (pv1Based < 1 || pv1Based > found.PvUnits.Count)
            {
                message = $"光伏单元 {pv1Based} 不存在";
                return false;
            }

            ess = found;
            unit = found.PvUnits[pv1Based - 1];
            message = "";
            return true;
        }

        private static bool AcceptsActiveSetpoint(
            EssSimulator.EssDeviceSimModel.Pv.PvInverterDevice inv,
            double requested,
            out double maxKw)
        {
            double kept = inv.ActivePowerSettingKw;
            if (requested < 0)
            {
                inv.ActivePowerSettingKw = Math.Max(inv.RatedPowerKw * 2, 1);
                maxKw = inv.ActivePowerSettingKw;
                inv.ActivePowerSettingKw = kept;
                return false;
            }

            inv.ActivePowerSettingKw = requested;
            maxKw = inv.ActivePowerSettingKw;
            inv.ActivePowerSettingKw = kept;
            return maxKw >= requested - 1e-3;
        }

        private static double ReadDisplayedActiveSetpoint(int pv1Based, int inverter0Based, double fallback)
        {
            return TryReadPvInverterActiveSetpoint(pv1Based, inverter0Based, out var kw) ? kw : fallback;
        }

        private static bool TryGetPvInverter(
            int pv1Based,
            int inverter1Based,
            out EnergyStorageSystem? ess,
            out EssSimulator.EssDeviceSimModel.Pv.PvInverterDevice inv,
            out int inverter0Based,
            out string message)
        {
            ess = SimulatorHost.Instance.TryGetEss();
            inv = null!;
            inverter0Based = inverter1Based - 1;
            if (ess == null)
            {
                message = "找不到 ess 模型，请确认仿真已启动";
                return false;
            }

            if (pv1Based < 1 || pv1Based > ess.PvUnits.Count)
            {
                message = $"光伏单元 {pv1Based} 不存在";
                return false;
            }

            var inverters = ess.PvUnits[pv1Based - 1].Inverters;
            if (inverter1Based < 1 || inverter1Based > inverters.Count)
            {
                message = $"光伏逆变器 {inverter1Based} 不存在";
                return false;
            }

            inv = inverters[inverter0Based];
            message = "";
            return true;
        }

        private static readonly object CapGate = new();
        private static readonly Dictionary<int, Dictionary<int, InverterDischargeCap>> DischargeCaps = new();

        private sealed class InverterDischargeCap
        {
            public double CapKw { get; set; }
            public double RememberedActiveKw { get; set; }
        }

    }
}
