using System;
using System.Runtime.CompilerServices;
using EssSimulator.EssDeviceSimModel.Pv;

namespace EssSimulator.EssSimModelApi.Mappers
{
    /// <summary>
    /// 光伏单元有功缺额转移。逻辑命令（卡片 P设、Logger 下发的均分额）保持不变；
    /// 运行中的逆变器执行设定改成分配结果。停机或最大可发低于命令时，差额分给仍有余量的运行逆变器。
    /// 辐照限制仍由模型内部的缺额转移处理。
    /// </summary>
    internal static class PvExecutionShare
    {
        private static readonly object Gate = new();
        private static readonly ConditionalWeakTable<PvUnitDevice, Slot> LogicalKw = new();

        private sealed class Slot
        {
            public double[] Values = Array.Empty<double>();
        }

        public static bool TryGet(PvUnitDevice unit, int inverter0Based, out double kw)
        {
            lock (Gate)
            {
                if (LogicalKw.TryGetValue(unit, out var slot) &&
                    (uint)inverter0Based < (uint)slot.Values.Length)
                {
                    kw = slot.Values[inverter0Based];
                    return true;
                }
            }

            kw = 0;
            return false;
        }

        public static void CaptureAll(PvUnitDevice unit)
        {
            var values = new double[unit.Inverters.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = unit.Inverters[i].ActivePowerSettingKw;
            lock (Gate)
                SlotFor(unit).Values = values;
        }

        /// <summary>还没记录逻辑设定时，用当前各台设定初始化；已记录则不改，避免把重分后的执行值当成设定。</summary>
        public static void EnsureCaptured(PvUnitDevice unit) => SnapshotLogical(unit);

        public static void CaptureOne(PvUnitDevice unit, int inverter0Based, double kw)
        {
            int inverterCount = unit.Inverters.Count;
            if (inverterCount <= 0 || (uint)inverter0Based >= (uint)inverterCount)
                return;
            lock (Gate)
            {
                var slot = SlotFor(unit);
                if (slot.Values.Length != inverterCount)
                {
                    var values = new double[inverterCount];
                    for (int i = 0; i < inverterCount; i++)
                        values[i] = unit.Inverters[i].ActivePowerSettingKw;
                    slot.Values = values;
                }

                slot.Values[inverter0Based] = kw;
            }
        }

        /// <summary>
        /// capKw 返回该台最大可发；没有上限时返回 null。调用方不要在持有上限锁时进入这里。
        /// </summary>
        public static void Rebalance(PvUnitDevice unit, Func<int, double?> capKw)
        {
            int count = unit.Inverters.Count;
            if (count == 0)
                return;

            double[] logical = SnapshotLogical(unit);
            var deliver = new bool[count];
            var caps = new double?[count];
            var requests = new PvActiveShareRequest[count];
            bool anyDeliver = false;
            for (int i = 0; i < count; i++)
            {
                var inv = unit.Inverters[i];
                deliver[i] = inv.CanDeliverActivePower;
                anyDeliver |= deliver[i];
                caps[i] = capKw(i);
                double available = 0;
                if (deliver[i])
                {
                    available = inv.RatedPowerKw;
                    if (caps[i] is double cap)
                        available = Math.Min(available, Math.Max(0, cap));
                }

                requests[i] = new PvActiveShareRequest(Math.Max(0, logical[i]), Math.Max(0, available), true);
            }

            double[] allocated = anyDeliver
                ? PvActiveShareAllocator.Allocate(requests)
                : logical;

            for (int i = 0; i < count; i++)
            {
                double next = logical[i];
                if (anyDeliver && deliver[i])
                    next = allocated[i];
                else if (caps[i] is double cap)
                    next = Math.Min(Math.Max(0, logical[i]), Math.Max(0, cap));

                var inv = unit.Inverters[i];
                if (Math.Abs(inv.ActivePowerSettingKw - next) <= 1e-4)
                    continue;
                inv.SetPowerCommand(next, inv.ReactivePowerSettingKvar);
            }
        }

        private static Slot SlotFor(PvUnitDevice unit)
        {
            if (!LogicalKw.TryGetValue(unit, out var slot))
            {
                slot = new Slot();
                LogicalKw.Add(unit, slot);
            }

            return slot;
        }

        private static double[] SnapshotLogical(PvUnitDevice unit)
        {
            lock (Gate)
            {
                var slot = SlotFor(unit);
                if (slot.Values.Length != unit.Inverters.Count)
                {
                    var stored = new double[unit.Inverters.Count];
                    for (int i = 0; i < stored.Length; i++)
                        stored[i] = unit.Inverters[i].ActivePowerSettingKw;
                    slot.Values = stored;
                }

                return (double[])slot.Values.Clone();
            }
        }
    }
}
