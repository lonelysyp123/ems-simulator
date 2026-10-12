using System;
using System.Collections.Generic;
using EssSimulator.EssDeviceSimModel.Bms;

namespace EssSimulator.EssSimModelApi.BatteryManagementSystem
{
    public partial class BatteryStack
    {
        private static bool? AggregateClusterAlarms(
            List<BatteryCluster>? clusters,
            Func<ClusterAlarms, bool?> selector)
        {
            if (clusters == null || clusters.Count == 0)
                return null;

            foreach (var cluster in clusters)
            {
                if (selector(cluster.Alarms) == true)
                    return true;
            }

            return false;
        }

        /// <summary>堆级告警聚合/下发：读=任一簇为 true；写=同步到全部簇。</summary>
        public sealed class StackSystemAlarms
        {
            private readonly BatteryStack _stack;

            internal StackSystemAlarms(BatteryStack stack) => _stack = stack;

            private bool? Any(Func<ClusterAlarms, bool?> selector) =>
                AggregateClusterAlarms(_stack.Cluseter, selector);

            private void SetAll(Action<ClusterAlarms> setter)
            {
                if (_stack.Cluseter == null)
                    return;

                foreach (var cluster in _stack.Cluseter)
                    setter(cluster.Alarms);
            }

            public bool? OvervoltageFault
            {
                get => Any(a => a.OvervoltageFault);
                set => SetAll(a => a.OvervoltageFault = value);
            }

            public bool? UndervoltageFault
            {
                get => Any(a => a.UndervoltageFault);
                set => SetAll(a => a.UndervoltageFault = value);
            }

            public bool? CellOverVoltageFault
            {
                get => Any(a => a.CellOverVoltageFault);
                set => SetAll(a => a.CellOverVoltageFault = value);
            }

            public bool? CellUnderVoltageFault
            {
                get => Any(a => a.CellUnderVoltageFault);
                set => SetAll(a => a.CellUnderVoltageFault = value);
            }

            public bool? DischargeOvercurrentFault
            {
                get => Any(a => a.DischargeOvercurrentFault);
                set => SetAll(a => a.DischargeOvercurrentFault = value);
            }

            public bool? ChargeOvercurrentFault
            {
                get => Any(a => a.ChargeOvercurrentFault);
                set => SetAll(a => a.ChargeOvercurrentFault = value);
            }

            public bool? CellDischargeHighTempFault
            {
                get => Any(a => a.CellDischargeHighTempFault);
                set => SetAll(a => a.CellDischargeHighTempFault = value);
            }

            public bool? CellDischargeLowTempFault
            {
                get => Any(a => a.CellDischargeLowTempFault);
                set => SetAll(a => a.CellDischargeLowTempFault = value);
            }

            public bool? CellChargeHighTempFault
            {
                get => Any(a => a.CellChargeHighTempFault);
                set => SetAll(a => a.CellChargeHighTempFault = value);
            }

            public bool? CellChargeLowTempFault
            {
                get => Any(a => a.CellChargeLowTempFault);
                set => SetAll(a => a.CellChargeLowTempFault = value);
            }

            public bool? InsulationFault
            {
                get => Any(a => a.InsulationFault);
                set => SetAll(a => a.InsulationFault = value);
            }

            public bool? TerminalHighTempFault
            {
                get => Any(a => a.TerminalHighTempFault);
                set => SetAll(a => a.TerminalHighTempFault = value);
            }

            public bool? HVBHighTempFault
            {
                get => Any(a => a.HVBHighTempFault);
                set => SetAll(a => a.HVBHighTempFault = value);
            }

            public bool? VoltageDifferenceFault
            {
                get => Any(a => a.VoltageDifferenceFault);
                set => SetAll(a => a.VoltageDifferenceFault = value);
            }

            public bool? TempDifferenceFault
            {
                get => Any(a => a.TempDifferenceFault);
                set => SetAll(a => a.TempDifferenceFault = value);
            }

            public bool? LowSOCFault
            {
                get => Any(a => a.LowSOCFault);
                set => SetAll(a => a.LowSOCFault = value);
            }

            public bool? OvervoltageAlarm
            {
                get => Any(a => a.OvervoltageAlarm);
                set => SetAll(a => a.OvervoltageAlarm = value);
            }

            public bool? UndervoltageAlarm
            {
                get => Any(a => a.UndervoltageAlarm);
                set => SetAll(a => a.UndervoltageAlarm = value);
            }

            public bool? CellOverVoltageAlarm
            {
                get => Any(a => a.CellOverVoltageAlarm);
                set => SetAll(a => a.CellOverVoltageAlarm = value);
            }

            public bool? CellUnderVoltageAlarm
            {
                get => Any(a => a.CellUnderVoltageAlarm);
                set => SetAll(a => a.CellUnderVoltageAlarm = value);
            }

            public bool? DischargeOvercurrentAlarm
            {
                get => Any(a => a.DischargeOvercurrentAlarm);
                set => SetAll(a => a.DischargeOvercurrentAlarm = value);
            }

            public bool? ChargeOvercurrentAlarm
            {
                get => Any(a => a.ChargeOvercurrentAlarm);
                set => SetAll(a => a.ChargeOvercurrentAlarm = value);
            }

            public bool? CellDischargeHighTempAlarm
            {
                get => Any(a => a.CellDischargeHighTempAlarm);
                set => SetAll(a => a.CellDischargeHighTempAlarm = value);
            }

            public bool? CellDischargeLowTempAlarm
            {
                get => Any(a => a.CellDischargeLowTempAlarm);
                set => SetAll(a => a.CellDischargeLowTempAlarm = value);
            }

            public bool? CellChargeHighTempAlarm
            {
                get => Any(a => a.CellChargeHighTempAlarm);
                set => SetAll(a => a.CellChargeHighTempAlarm = value);
            }

            public bool? CellChargeLowTempAlarm
            {
                get => Any(a => a.CellChargeLowTempAlarm);
                set => SetAll(a => a.CellChargeLowTempAlarm = value);
            }

            public bool? InsulationAlarm
            {
                get => Any(a => a.InsulationAlarm);
                set => SetAll(a => a.InsulationAlarm = value);
            }

            public bool? TerminalHighTempAlarm
            {
                get => Any(a => a.TerminalHighTempAlarm);
                set => SetAll(a => a.TerminalHighTempAlarm = value);
            }

            public bool? HVBHighTempAlarm
            {
                get => Any(a => a.HVBHighTempAlarm);
                set => SetAll(a => a.HVBHighTempAlarm = value);
            }

            public bool? VoltageDifferenceAlarm
            {
                get => Any(a => a.VoltageDifferenceAlarm);
                set => SetAll(a => a.VoltageDifferenceAlarm = value);
            }

            public bool? TempDifferenceAlarm
            {
                get => Any(a => a.TempDifferenceAlarm);
                set => SetAll(a => a.TempDifferenceAlarm = value);
            }

            public bool? LowSOCAlarm
            {
                get => Any(a => a.LowSOCAlarm);
                set => SetAll(a => a.LowSOCAlarm = value);
            }

            public bool? OvervoltageProtection
            {
                get => Any(a => a.OvervoltageProtection);
                set => SetAll(a => a.OvervoltageProtection = value);
            }

            public bool? UndervoltageProtection
            {
                get => Any(a => a.UndervoltageProtection);
                set => SetAll(a => a.UndervoltageProtection = value);
            }

            public bool? CellOverVoltageProtection
            {
                get => Any(a => a.CellOverVoltageProtection);
                set => SetAll(a => a.CellOverVoltageProtection = value);
            }

            public bool? CellUnderVoltageProtection
            {
                get => Any(a => a.CellUnderVoltageProtection);
                set => SetAll(a => a.CellUnderVoltageProtection = value);
            }

            public bool? DischargeOvercurrentProtection
            {
                get => Any(a => a.DischargeOvercurrentProtection);
                set => SetAll(a => a.DischargeOvercurrentProtection = value);
            }

            public bool? ChargeOvercurrentProtection
            {
                get => Any(a => a.ChargeOvercurrentProtection);
                set => SetAll(a => a.ChargeOvercurrentProtection = value);
            }

            public bool? CellDischargeHighTempProtection
            {
                get => Any(a => a.CellDischargeHighTempProtection);
                set => SetAll(a => a.CellDischargeHighTempProtection = value);
            }

            public bool? CellDischargeLowTempProtection
            {
                get => Any(a => a.CellDischargeLowTempProtection);
                set => SetAll(a => a.CellDischargeLowTempProtection = value);
            }

            public bool? CellChargeHighTempProtection
            {
                get => Any(a => a.CellChargeHighTempProtection);
                set => SetAll(a => a.CellChargeHighTempProtection = value);
            }

            public bool? CellChargeLowTempProtection
            {
                get => Any(a => a.CellChargeLowTempProtection);
                set => SetAll(a => a.CellChargeLowTempProtection = value);
            }

            public bool? InsulationProtection
            {
                get => Any(a => a.InsulationProtection);
                set => SetAll(a => a.InsulationProtection = value);
            }

            public bool? TerminalHighTempProtection
            {
                get => Any(a => a.TerminalHighTempProtection);
                set => SetAll(a => a.TerminalHighTempProtection = value);
            }

            public bool? HVBHighTempProtection
            {
                get => Any(a => a.HVBHighTempProtection);
                set => SetAll(a => a.HVBHighTempProtection = value);
            }

            public bool? VoltageDifferenceProtection
            {
                get => Any(a => a.VoltageDifferenceProtection);
                set => SetAll(a => a.VoltageDifferenceProtection = value);
            }

            public bool? TempDifferenceProtection
            {
                get => Any(a => a.TempDifferenceProtection);
                set => SetAll(a => a.TempDifferenceProtection = value);
            }

            public bool? LowSOCProtection
            {
                get => Any(a => a.LowSOCProtection);
                set => SetAll(a => a.LowSOCProtection = value);
            }
        }
    }
}
