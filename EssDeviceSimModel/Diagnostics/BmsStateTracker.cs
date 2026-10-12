using EssSimulator.EssDeviceSimModel.Devices;

namespace EssSimulator.EssDeviceSimModel.Diagnostics
{
    /// <summary>检测 BMS 保护/汇总字段变化并输出原因。</summary>
    internal static class BmsStateTracker
    {
        private sealed class Snapshot
        {
            public ushort BmsFaultSummary;
            public ushort BmsAlarmSummary;
            public ushort IsFault;
            public float Soc;
        }

        private static readonly Dictionary<string, Snapshot> LastByLabel = new();

        public static void ReportProtectionChanges(
            string label,
            BmsStackProtectionSnapshot stack,
            RackState rack)
        {
            var cur = new Snapshot
            {
                BmsFaultSummary = stack.FaultSummary,
                BmsAlarmSummary = stack.AlarmSummary,
                IsFault = rack.IsFault,
                Soc = stack.Soc
            };

            if (!LastByLabel.TryGetValue(label, out var prev))
            {
                LastByLabel[label] = cur;
                return;
            }

            if (prev.BmsFaultSummary != cur.BmsFaultSummary)
            {
                SimStateChangeLogger.BmsStateChanged(
                    label,
                    "BMSFaultSummary(三级报警)",
                    $"0x{prev.BmsFaultSummary:X}",
                    $"0x{cur.BmsFaultSummary:X}",
                    InferFaultSummaryReason(stack, cur.BmsFaultSummary));
            }

            if (prev.BmsAlarmSummary != cur.BmsAlarmSummary)
            {
                SimStateChangeLogger.BmsStateChanged(
                    label,
                    "BMSAlarmSummary(二级告警)",
                    $"0x{prev.BmsAlarmSummary:X}",
                    $"0x{cur.BmsAlarmSummary:X}",
                    "簇级二级告警汇总变化");
            }

            if (prev.IsFault != cur.IsFault)
            {
                SimStateChangeLogger.BmsStateChanged(
                    label,
                    "RackIsFault",
                    SimStateChangeLogger.FormatRackFault(prev.IsFault),
                    SimStateChangeLogger.FormatRackFault(cur.IsFault),
                    InferRackFaultReason(stack, prev.IsFault, cur.IsFault));
            }

            LastByLabel[label] = cur;
        }

        private static string InferFaultSummaryReason(BmsStackProtectionSnapshot stack, ushort summary)
        {
            if (summary == 0)
                return "三级报警已恢复";

            var parts = new List<string> { $"三级报警位=0x{summary:X}" };
            if (stack.IsChargeFault)
                parts.Add("含充电方向三级故障");
            if (stack.IsDischargeFault)
                parts.Add("含放电方向三级故障");
            parts.Add($"SOC={stack.Soc * 100:F1}%");
            return string.Join(", ", parts);
        }

        private static string InferRackFaultReason(BmsStackProtectionSnapshot stack, ushort oldFault, ushort newFault)
        {
            if (newFault == 0)
                return "Rack 故障态恢复";

            var parts = new List<string>();
            if (stack.FaultSummary != 0)
                parts.Add($"BMSFaultSummary=0x{stack.FaultSummary:X}");

            if (stack.Soc >= 0.95f && (newFault == 1 || newFault == 3) && oldFault != newFault)
                parts.Add($"SOC={stack.Soc * 100:F1}%≥95% 充电限充");
            if (stack.Soc <= 0.05f && (newFault == 2 || newFault == 3) && oldFault != newFault)
                parts.Add($"SOC={stack.Soc * 100:F1}%≤5% 放电限放");
            if (stack.Soh <= 0.05f)
                parts.Add($"SOH={stack.Soh * 100:F1}%≤5%");

            if (stack.IsChargeFault)
                parts.Add("簇充电三级故障");
            if (stack.IsDischargeFault)
                parts.Add("簇放电三级故障");

            return parts.Count > 0 ? string.Join(", ", parts) : "保护逻辑回写 Rack 故障态";
        }
    }
}
