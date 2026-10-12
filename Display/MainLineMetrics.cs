using System.Globalization;

namespace EssSimulator.Display
{
    /// <summary>从 <see cref="MainLineSnapshot"/> 读取主接线图可显示量。</summary>
    public static class MainLineMetrics
    {
        public static bool TryRead(string metric, MainLineSnapshot snap, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(metric))
                return false;

            var parts = metric.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return false;

            if (parts[0].Equals("mainBreaker", StringComparison.OrdinalIgnoreCase) &&
                parts.Length >= 2 &&
                parts[1].Equals("closed", StringComparison.OrdinalIgnoreCase))
            {
                value = snap.MainBreakerClosed ? 1 : 0;
                return true;
            }

            if (parts[0].Equals("load", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
            {
                if (parts[1].Equals("activePowerKw", StringComparison.OrdinalIgnoreCase))
                {
                    value = snap.LoadActivePowerKw;
                    return true;
                }
                if (parts[1].Equals("reactivePowerKvar", StringComparison.OrdinalIgnoreCase))
                {
                    value = snap.LoadReactivePowerKvar;
                    return true;
                }
            }

            if (parts[0].Equals("unit", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3 &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int unitIndex))
            {
                var unit = snap.Units.FirstOrDefault(u => u.UnitIndex == unitIndex);
                if (unit == null)
                    return false;

                if (parts[2].Equals("breaker", StringComparison.OrdinalIgnoreCase) &&
                    parts.Length >= 4 &&
                    parts[3].Equals("closed", StringComparison.OrdinalIgnoreCase))
                {
                    value = unit.UnitBreakerClosed ? 1 : 0;
                    return true;
                }

                // pcsA/pcsB/pcsN（N 为槽位号，1 基）→ 单元内对应槽位 PCS
                if (parts[2].StartsWith("pcs", StringComparison.OrdinalIgnoreCase) &&
                    parts.Length >= 4 &&
                    parts[3].Equals("activePowerKw", StringComparison.OrdinalIgnoreCase) &&
                    TryParseSlotToken(parts[2], out int pcsSlot) &&
                    pcsSlot < unit.PcsChannels.Count &&
                    unit.PcsChannels[pcsSlot] is { } pcsSnap)
                {
                    value = pcsSnap.ActivePowerKw;
                    return true;
                }

                if (parts[2].StartsWith("bms", StringComparison.OrdinalIgnoreCase) &&
                    parts.Length >= 4 &&
                    parts[3].Equals("soc", StringComparison.OrdinalIgnoreCase) &&
                    TryParseSlotToken(parts[2], out int bmsSlot))
                {
                    var layout = GuiSimDataAccess.GetPcsPerUnit();
                    int bmsIndex = EssDeviceSimModel.PcsUnitLayout.BaseIndexOfUnit(layout, unit.UnitIndex) + bmsSlot;
                    value = 100 * GuiSimDataAccess.SafeGetDouble($"ess._batteryRacks[{bmsIndex}]._currentState.MinClusterSOC");
                    return true;
                }
            }

            return false;
        }

        public static bool Changed(double before, double after, double tolerance) =>
            Math.Abs(before - after) > tolerance;

        /// <summary>解析 pcsA/pcs3 类后缀：单字母按 A=0 起；数字按 1 基槽位。</summary>
        private static bool TryParseSlotToken(string token, out int slot)
        {
            slot = 0;
            string tail = token.Length > 3 ? token.Substring(3) : string.Empty;
            if (tail.Length == 1 && char.IsLetter(tail[0]))
            {
                slot = char.ToUpperInvariant(tail[0]) - 'A';
                return slot >= 0;
            }
            if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out int num) && num >= 1)
            {
                slot = num - 1;
                return true;
            }
            return false;
        }

        public static void PrintCatalog()
        {
            Console.WriteLine("主接线可观测 metric（用于 perftest drive / observe.snapshot）:");
            Console.WriteLine("  mainBreaker.closed");
            Console.WriteLine("  load.activePowerKw | load.reactivePowerKvar");
            Console.WriteLine("  unit.{N}.breaker.closed");
            Console.WriteLine("  unit.{N}.pcs{S}.activePowerKw | unit.{N}.bms{S}.soc   (N=0,1,…；S=A/B 或槽位号 1,2,…)");
        }
    }
}
