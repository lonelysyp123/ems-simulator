using System;
using System.Collections.Generic;
using System.Linq;

namespace EssSimulator.EssDeviceSimModel.Bms
{
public class ClusterThresholds
    {
        public ClusterThresholds()
        {
            // 单体欠压阈值: 保护/告警/故障
            CellUndervoltageThreshold1 = 3.1f;
            CellUndervoltageThreshold2 = 2.9f;
            CellUndervoltageThreshold3 = 2.6f;

            // 单体过压阈值: 保护/告警/故障
            CellOvervoltageThreshold1 = 3.4f;
            CellOvervoltageThreshold2 = 3.6f;
            CellOvervoltageThreshold3 = 3.9f;

            // 簇欠压阈值: 保护/告警/故障
            UndervoltageThreshold1 = 416*3.1f;
            UndervoltageThreshold2 = 416*2.9f;
            UndervoltageThreshold3 = 416*2.6f;

            // 簇过压阈值: 保护/告警/故障
            OvervoltageThreshold1 = 416*3.4f;
            OvervoltageThreshold2 = 416*3.6f;
            OvervoltageThreshold3 = 416*3.9f;

            // 充电过流阈值: 保护/告警/故障
            ChargeOvercurrentThreshold1 = 300f;
            ChargeOvercurrentThreshold2 = 350f;
            ChargeOvercurrentThreshold3 = 400f;

            // 放电过流阈值: 保护/告警/故障
            DischargeOvercurrentThreshold1 = 300f;
            DischargeOvercurrentThreshold2 = 350f;
            DischargeOvercurrentThreshold3 = 400f;

            // 充电高温阈值: 保护/告警/故障
            ChargeHighTempThreshold1 = 55f;
            ChargeHighTempThreshold2 = 60f;
            ChargeHighTempThreshold3 = 65f;

            // 充电低温阈值: 保护/告警/故障
            ChargeLowTempThreshold1 = 15f;
            ChargeLowTempThreshold2 = 10f;
            ChargeLowTempThreshold3 = 5f;

            // 放电高温阈值: 保护/告警/故障
            DischargeHighTempThreshold1 = 55f;
            DischargeHighTempThreshold2 = 60f;
            DischargeHighTempThreshold3 = 65f;

            // 放电低温阈值: 保护/告警/故障
            DischargeLowTempThreshold1 = 15f;
            DischargeLowTempThreshold2 = 10f;
            DischargeLowTempThreshold3 = 5f;

            // 低SOC阈值: 保护/告警/故障
            LowSOCTreshold1 = 0.15f;
            LowSOCTreshold2 = 0.10f;
            LowSOCTreshold3 = 0.05f;

            // 端子高温阈值: 保护/告警/故障
            PoleHighTempThreshold1 = 65f;
            PoleHighTempThreshold2 = 70f;
            PoleHighTempThreshold3 = 75f;

            // 绝缘阈值: 保护/告警/故障
            InsulationThreshold1 = 500f;
            InsulationThreshold2 = 300f;
            InsulationThreshold3 = 100f;

            // 单体压差阈值: 保护/告警/故障
            CellVoltageDifferenceThreshold1 = 0.2f;
            CellVoltageDifferenceThreshold2 = 0.25f;
            CellVoltageDifferenceThreshold3 = 0.3f;

            // 簇压差阈值: 保护/告警/故障
            TotalVoltageDifferenceThreshold1 = 10f;
            TotalVoltageDifferenceThreshold2 = 15f;
            TotalVoltageDifferenceThreshold3 = 20f;

            // 单体温差阈值: 保护/告警/故障
            CellTempDifferenceThreshold1 = 3f;
            CellTempDifferenceThreshold2 = 5f;
            CellTempDifferenceThreshold3 = 7f;

            // 高压箱连接器高温阈值: 保护/告警/故障
            HVBHighTempThreshold1 = 65f;
            HVBHighTempThreshold2 = 70f;
            HVBHighTempThreshold3 = 75f;

            // 单体欠压恢复阈值: 保护/告警/故障
            CellUndervoltageRecovery1 = CellUndervoltageThreshold1.Value + 0.1f;
            CellUndervoltageRecovery2 = CellUndervoltageThreshold2.Value + 0.1f;
            CellUndervoltageRecovery3 = CellUndervoltageThreshold3.Value + 0.1f;
            
            // 单体过压恢复阈值: 保护/告警/故障
            CellOvervoltageRecovery1 = CellOvervoltageThreshold1.Value - 0.1f;
            CellOvervoltageRecovery2 = CellOvervoltageThreshold2.Value - 0.1f;
            CellOvervoltageRecovery3 = CellOvervoltageThreshold3.Value - 0.1f;

            // 簇欠压恢复阈值: 保护/告警/故障
            UndervoltageRecovery1 = UndervoltageThreshold1.Value + 5f;
            UndervoltageRecovery2 = UndervoltageThreshold2.Value + 5f;
            UndervoltageRecovery3 = UndervoltageThreshold3.Value + 5f;

            // 簇过压恢复阈值: 保护/告警/故障
            OvervoltageRecovery1 = OvervoltageThreshold1.Value - 5f;
            OvervoltageRecovery2 = OvervoltageThreshold2.Value - 5f;
            OvervoltageRecovery3 = OvervoltageThreshold3.Value - 5f;

            // 充电过流恢复阈值: 保护/告警/故障
            ChargeOvercurrentRecovery1 = ChargeOvercurrentThreshold1.Value - 10f;
            ChargeOvercurrentRecovery2 = ChargeOvercurrentThreshold2.Value - 10f;
            ChargeOvercurrentRecovery3 = ChargeOvercurrentThreshold3.Value - 10f;

            // 放电过流恢复阈值: 保护/告警/故障
            DischargeOvercurrentRecovery1 = DischargeOvercurrentThreshold1.Value - 10f;
            DischargeOvercurrentRecovery2 = DischargeOvercurrentThreshold2.Value - 10f;
            DischargeOvercurrentRecovery3 = DischargeOvercurrentThreshold3.Value - 10f;

            // 充电高温恢复阈值: 保护/告警/故障
            ChargeHighTempRecovery1 = ChargeHighTempThreshold1.Value - 3f;
            ChargeHighTempRecovery2 = ChargeHighTempThreshold2.Value - 3f;
            ChargeHighTempRecovery3 = ChargeHighTempThreshold3.Value - 3f;

            // 充电低温恢复阈值: 保护/告警/故障
            ChargeLowTempRecovery1 = ChargeLowTempThreshold1.Value + 3f;
            ChargeLowTempRecovery2 = ChargeLowTempThreshold2.Value + 3f;
            ChargeLowTempRecovery3 = ChargeLowTempThreshold3.Value + 3f;

            // 放电高温恢复阈值: 保护/告警/故障
            DischargeHighTempRecovery1 = DischargeHighTempThreshold1.Value - 3f;
            DischargeHighTempRecovery2 = DischargeHighTempThreshold2.Value - 3f;
            DischargeHighTempRecovery3 = DischargeHighTempThreshold3.Value - 3f;

            // 放电低温恢复阈值: 保护/告警/故障
            DischargeLowTempRecovery1 = DischargeLowTempThreshold1.Value + 3f;
            DischargeLowTempRecovery2 = DischargeLowTempThreshold2.Value + 3f;
            DischargeLowTempRecovery3 = DischargeLowTempThreshold3.Value + 3f;

            // SOC过低恢复阈值（SOC 为 0~1，回滞约 3%）
            LowSOCRecovery1 = LowSOCTreshold1.Value + 0.03f;
            LowSOCRecovery2 = LowSOCTreshold2.Value + 0.03f;
            LowSOCRecovery3 = LowSOCTreshold3.Value + 0.03f;

            // 端子高温恢复阈值: 保护/告警/故障
            PoleHighTempRecovery1 = PoleHighTempThreshold1.Value - 3f;
            PoleHighTempRecovery2 = PoleHighTempThreshold2.Value - 3f;
            PoleHighTempRecovery3 = PoleHighTempThreshold3.Value - 3f;

            // 绝缘恢复阈值: 保护/告警/故障
            InsulationRecovery1 = InsulationThreshold1.Value + 10f;
            InsulationRecovery2 = InsulationThreshold2.Value + 10f;
            InsulationRecovery3 = InsulationThreshold3.Value + 10f;

            // 单体压差恢复阈值: 保护/告警/故障
            CellVoltageDifferenceRecovery1 = CellVoltageDifferenceThreshold1.Value - 0.05f;
            CellVoltageDifferenceRecovery2 = CellVoltageDifferenceThreshold2.Value - 0.05f;
            CellVoltageDifferenceRecovery3 = CellVoltageDifferenceThreshold3.Value - 0.05f;

            // 簇压差恢复阈值: 保护/告警/故障
            TotalVoltageDifferenceRecovery1 = TotalVoltageDifferenceThreshold1.Value - 2f;
            TotalVoltageDifferenceRecovery2 = TotalVoltageDifferenceThreshold2.Value - 2f;
            TotalVoltageDifferenceRecovery3 = TotalVoltageDifferenceThreshold3.Value - 2f;

            // 单体温差恢复阈值: 保护/告警/故障
            CellTempDifferenceRecovery1 = CellTempDifferenceThreshold1.Value - 1f;
            CellTempDifferenceRecovery2 = CellTempDifferenceThreshold2.Value - 1f;
            CellTempDifferenceRecovery3 = CellTempDifferenceThreshold3.Value - 1f;

            // 高压箱连接器高温恢复阈值: 保护/告警/故障
            HVBHighTempRecovery1 = HVBHighTempThreshold1.Value - 3f;
            HVBHighTempRecovery2 = HVBHighTempThreshold2.Value - 3f;
            HVBHighTempRecovery3 = HVBHighTempThreshold3.Value - 3f;
        }

        // 保护阈值
        public float? CellUndervoltageThreshold1 { get; set; } // 单体欠压保护阈值
        public float? CellOvervoltageThreshold1 { get; set; } // 单体过压保护阈值
        public float? UndervoltageThreshold1 { get; set; } // 簇欠压保护阈值
        public float? OvervoltageThreshold1 { get; set; } // 簇过压保护阈值
        public float? ChargeOvercurrentThreshold1 { get; set; } // 充电过流保护阈值
        public float? DischargeOvercurrentThreshold1 { get; set; } // 放电过流保护阈值
        public float? ChargeHighTempThreshold1 { get; set; } // 充电高温保护阈值
        public float? ChargeLowTempThreshold1 { get; set; } // 充电低温保护阈值
        public float? LowSOCTreshold1 { get; set; } // SOC过低保护阈值

        /// <summary>develop 点表别名（bms_rack yt16/50/84）。</summary>
        public float? LowSOCThreshold1
        {
            get => LowSOCTreshold1;
            set => LowSOCTreshold1 = value;
        }
        public float? PoleHighTempThreshold1 { get; set; } // 极柱温度过高保护阈值
        public float? InsulationThreshold1 { get; set; } // 绝缘保护阈值
        public float? CellVoltageDifferenceThreshold1 { get; set; } // 单体压差过高保护阈值
        public float? TotalVoltageDifferenceThreshold1 { get; set; } // 总电压压差过高保护阈值
        public float? DischargeHighTempThreshold1 { get; set; } // 放电高温保护阈值
        public float? DischargeLowTempThreshold1 { get; set; } // 放电低温保护阈值
        public float? CellTempDifferenceThreshold1 { get; set; } // 单体温差过高保护阈值
        public float? HVBHighTempThreshold1 { get; set; } // 高压箱连接器温度过高保护阈值

        // 恢复阈值
        public float? CellUndervoltageRecovery1 { get; set; } // 单体欠压保护恢复阈值
        public float? CellOvervoltageRecovery1 { get; set; } // 单体过压保护恢复阈值
        public float? UndervoltageRecovery1 { get; set; } // 簇欠压保护恢复阈值
        public float? OvervoltageRecovery1 { get; set; } // 簇过压保护恢复阈值
        public float? ChargeOvercurrentRecovery1 { get; set; } // 充电过流保护恢复阈值
        public float? DischargeOvercurrentRecovery1 { get; set; } // 放电过流保护恢复阈值
        public float? ChargeHighTempRecovery1 { get; set; } // 充电高温保护恢复阈值
        public float? ChargeLowTempRecovery1 { get; set; } // 充电低温保护恢复阈值
        public float? LowSOCRecovery1 { get; set; } // SOC过低保护恢复阈值
        public float? PoleHighTempRecovery1 { get; set; } // 极柱温度过高保护恢复阈值
        public float? InsulationRecovery1 { get; set; } // 绝缘保护恢复阈值
        public float? CellVoltageDifferenceRecovery1 { get; set; } // 单体压差过高保护恢复阈值
        public float? TotalVoltageDifferenceRecovery1 { get; set; } // 总电压压差过高保护恢复阈值
        public float? DischargeHighTempRecovery1 { get; set; } // 放电高温保护恢复阈值
        public float? DischargeLowTempRecovery1 { get; set; } // 放电低温保护恢复阈值
        public float? CellTempDifferenceRecovery1 { get; set; } // 单体温差过高保护恢复阈值
        public float? HVBHighTempRecovery1 { get; set; } // 高压箱连接器温度过高保护恢复阈值

        // 告警阈值
        public float? CellUndervoltageThreshold2 { get; set; } // 单体欠压告警阈值
        public float? CellOvervoltageThreshold2 { get; set; } // 单体过压告警阈值
        public float? UndervoltageThreshold2 { get; set; } // 簇欠压告警阈值
        public float? OvervoltageThreshold2 { get; set; } // 簇过压告警阈值
        public float? ChargeOvercurrentThreshold2 { get; set; } // 充电过流告警阈值
        public float? DischargeOvercurrentThreshold2 { get; set; } // 放电过流告警阈值
        public float? ChargeHighTempThreshold2 { get; set; } // 充电高温告警阈值
        public float? ChargeLowTempThreshold2 { get; set; } // 充电低温告警阈值
        public float? LowSOCTreshold2 { get; set; } // SOC过低告警阈值

        /// <summary>develop 点表别名。</summary>
        public float? LowSOCThreshold2
        {
            get => LowSOCTreshold2;
            set => LowSOCTreshold2 = value;
        }
        public float? PoleHighTempThreshold2 { get; set; } // 极柱温度过高告警阈值
        public float? InsulationThreshold2 { get; set; } // 绝缘告警阈值
        public float? CellVoltageDifferenceThreshold2 { get; set; } // 单体压差过高告警阈值
        public float? TotalVoltageDifferenceThreshold2 { get; set; } // 总电压压差过高告警阈值
        public float? DischargeHighTempThreshold2 { get; set; } // 放电高温告警阈值
        public float? DischargeLowTempThreshold2 { get; set; } // 放电低温告警阈值
        public float? CellTempDifferenceThreshold2 { get; set; } // 单体温差过高告警阈值
        public float? HVBHighTempThreshold2 { get; set; } // 高压箱连接器温度过高告警阈值

        // 恢复阈值
        public float? CellUndervoltageRecovery2 { get; set; } // 单体欠压告警恢复阈值
        public float? CellOvervoltageRecovery2 { get; set; } // 单体过压告警恢复阈值
        public float? UndervoltageRecovery2 { get; set; } // 簇欠压告警恢复阈值
        public float? OvervoltageRecovery2 { get; set; } // 簇过压告警恢复阈值
        public float? ChargeOvercurrentRecovery2 { get; set; } // 充电过流告警恢复阈值
        public float? DischargeOvercurrentRecovery2 { get; set; } // 放电过流告警恢复阈值
        public float? ChargeHighTempRecovery2 { get; set; } // 充电高温告警恢复阈值
        public float? ChargeLowTempRecovery2 { get; set; } // 充电低温告警恢复阈值
        public float? LowSOCRecovery2 { get; set; } // SOC过低告警恢复阈值
        public float? PoleHighTempRecovery2 { get; set; } // 极柱温度过高告警恢复阈值
        public float? InsulationRecovery2 { get; set; } // 绝缘告警恢复阈值
        public float? CellVoltageDifferenceRecovery2 { get; set; } // 单体压差过高告警恢复阈值
        public float? TotalVoltageDifferenceRecovery2 { get; set; } // 总电压压差过高告警恢复阈值
        public float? DischargeHighTempRecovery2 { get; set; } // 放电高温告警恢复阈值
        public float? DischargeLowTempRecovery2 { get; set; } // 放电低温告警恢复阈值
        public float? CellTempDifferenceRecovery2 { get; set; } // 单体温差过高告警恢复阈值
        public float? HVBHighTempRecovery2 { get; set; } // 高压箱连接器温度过高告警恢复阈值

        // 故障阈值
        public float? CellUndervoltageThreshold3 { get; set; } // 单体欠压故障阈值
        public float? CellOvervoltageThreshold3 { get; set; } // 单体过压故障阈值
        public float? UndervoltageThreshold3 { get; set; } // 簇欠压故障阈值
        public float? OvervoltageThreshold3 { get; set; } // 簇过压故障阈值
        public float? ChargeOvercurrentThreshold3 { get; set; } // 充电过流故障阈值
        public float? DischargeOvercurrentThreshold3 { get; set; } // 放电过流故障阈值
        public float? ChargeHighTempThreshold3 { get; set; } // 充电高温故障阈值
        public float? ChargeLowTempThreshold3 { get; set; } // 充电低温故障阈值
        public float? LowSOCTreshold3 { get; set; } // SOC过低故障阈值

        /// <summary>develop 点表别名。</summary>
        public float? LowSOCThreshold3
        {
            get => LowSOCTreshold3;
            set => LowSOCTreshold3 = value;
        }
        public float? PoleHighTempThreshold3 { get; set; } // 极柱温度过高故障阈值
        public float? InsulationThreshold3 { get; set; } // 绝缘故障阈值
        public float? CellVoltageDifferenceThreshold3 { get; set; } // 单体压差过高故障阈值
        public float? TotalVoltageDifferenceThreshold3 { get; set; } // 总电压压差过高故障阈值
        public float? DischargeHighTempThreshold3 { get; set; } // 放电高温故障阈值
        public float? DischargeLowTempThreshold3 { get; set; } // 放电低温故障阈值
        public float? CellTempDifferenceThreshold3 { get; set; } // 单体温差过高故障阈值
        public float? HVBHighTempThreshold3 { get; set; } // 高压箱连接器温度过高故障阈值

        // 恢复阈值
        public float? CellUndervoltageRecovery3 { get; set; } // 单体欠压故障恢复阈值
        public float? CellOvervoltageRecovery3 { get; set; } // 单体过压故障恢复阈值
        public float? UndervoltageRecovery3 { get; set; } // 簇欠压故障恢复阈值
        public float? OvervoltageRecovery3 { get; set; } // 簇过压故障恢复阈值
        public float? ChargeOvercurrentRecovery3 { get; set; } // 充电过流故障恢复阈值
        public float? DischargeOvercurrentRecovery3 { get; set; } // 放电过流故障恢复阈值
        public float? ChargeHighTempRecovery3 { get; set; } // 充电高温故障恢复阈值
        public float? ChargeLowTempRecovery3 { get; set; } // 充电低温故障恢复阈值
        public float? LowSOCRecovery3 { get; set; } // SOC过低故障恢复阈值
        public float? PoleHighTempRecovery3 { get; set; } // 极柱温度过高故障恢复阈值
        public float? InsulationRecovery3 { get; set; } // 绝缘故障恢复阈值
        public float? CellVoltageDifferenceRecovery3 { get; set; } // 单体压差过高故障恢复阈值
        public float? TotalVoltageDifferenceRecovery3 { get; set; } // 总电压压差过高故障恢复阈值
        public float? DischargeHighTempRecovery3 { get; set; } // 放电高温故障恢复阈值
        public float? DischargeLowTempRecovery3 { get; set; } // 放电低温故障恢复阈值
        public float? CellTempDifferenceRecovery3 { get; set; } // 单体温差过高故障恢复阈值
        public float? HVBHighTempRecovery3 { get; set; } // 高压箱连接器温度过高故障恢复阈值
    }
}
