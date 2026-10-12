using System;
using System.Collections.Generic;
using System.Linq;

namespace EssSimulator.EssDeviceSimModel.Bms
{
public class ClusterAlarms
    {
        public ClusterAlarms()
        {
            MildAlarm = false;
            ModerateAlarm = false;
            SevereAlarm = false;
            UndervoltageAlarm = false;
            UndervoltageFault = false;
            UndervoltageProtection = false;
            OvervoltageAlarm = false;
            OvervoltageFault = false;
            OvervoltageProtection = false;
            ChargeOvercurrentAlarm = false;
            ChargeOvercurrentFault = false;
            ChargeOvercurrentProtection = false;
            DischargeOvercurrentAlarm = false;
            DischargeOvercurrentFault = false;
            DischargeOvercurrentProtection = false;
            InsulationAlarm = false;
            InsulationFault = false;
            InsulationProtection = false;
            CellUnderVoltageAlarm = false;
            CellUnderVoltageFault = false;
            CellUnderVoltageProtection = false;
            CellOverVoltageAlarm = false;
            CellOverVoltageFault = false;
            CellOverVoltageProtection = false;
            VoltageDifferenceAlarm = false;
            VoltageDifferenceFault = false;
            VoltageDifferenceProtection = false;
            resv1 = false;
            resv2 = false;
            resv3 = false;
            resv4 = false;
            resv5 = false;
            resv6 = false;
            TempDifferenceAlarm = false;
            TempDifferenceFault = false;
            TempDifferenceProtection = false;
            LowSOCAlarm = false;
            LowSOCFault = false;
            LowSOCProtection = false;
            resv21 = false;
            resv22 = false;
            resv23 = false;
            resv24 = false;
            resv25 = false;
            resv26 = false;
            TerminalHighTempAlarm = false;
            TerminalHighTempFault = false;
            TerminalHighTempProtection = false;
            CellChargeLowTempAlarm = false;
            CellChargeLowTempFault = false;
            CellChargeLowTempProtection = false;
            CellChargeHighTempAlarm = false;
            CellChargeHighTempFault = false;
            CellChargeHighTempProtection = false;
            CellDischargeLowTempAlarm = false;
            CellDischargeLowTempFault = false;
            CellDischargeLowTempProtection = false;
            CellDischargeHighTempAlarm = false;
            CellDischargeHighTempFault = false;
            CellDischargeHighTempProtection = false;
            HVBHighTempAlarm = false;
            HVBHighTempFault = false;
            HVBHighTempProtection = false;
            TempRiseAlarm = false;
            TempRiseFault = false;
            TempRiseProtection = false;
            TempSamplingFault = false;
            VoltageSamplingFault = false;
            MasterCommFault = false;
            SlaveCommFault = false;
            MainPositiveContactor = false;
            MainNegativeContactor = false;
            resv31 = false;
            ChargeProhibited = false;
            DischargeProhibited = false;
            BatteryBoxOvervoltageProtection = false;
            BatteryBoxOvervoltageAlarm = false;
            BatteryBoxOvervoltageFault = false;
            BatteryBoxUndervoltageProtection = false;
            BatteryBoxUndervoltageAlarm = false;
            BatteryBoxUndervoltageFault = false;
            BatteryBoxTempDifferenceAlarm = false;
            BatteryBoxTempDifferenceFault = false;
            BatteryBoxTempDifferenceProtection = false;
            BatteryBoxPositivePoleTempDifferenceAlarm = false;
            BatteryBoxPositivePoleTempDifferenceFault = false;
            BatteryBoxPositivePoleTempDifferenceProtection = false;
            BatteryBoxNegativePoleTempDifferenceAlarm = false;
            BatteryBoxNegativePoleTempDifferenceFault = false;
            BatteryBoxNegativePoleTempDifferenceProtection = false;
            BatteryBoxBusbarHighTempAlarm = false;
            BatteryBoxBusbarHighTempFault = false;
            BatteryBoxBusbarHighTempProtection = false;
            BatteryBoxVoltageExtremaDifferenceAlarm = false;
            BatteryBoxVoltageExtremaDifferenceFault = false;
            BatteryBoxVoltageExtremaDifferenceProtection = false;
        }

        // 告警级别
        public bool? MildAlarm { get; set; } // 轻微告警
        public bool? ModerateAlarm { get; set; } // 中等告警
        public bool? SevereAlarm { get; set; } // 严重告警

        // 电压相关告警
        public bool? UndervoltageAlarm { get; set; } // 簇欠压报警
        public bool? UndervoltageFault { get; set; } // 簇欠压故障
        public bool? UndervoltageProtection { get; set; } // 簇欠压保护动作
        public bool? OvervoltageAlarm { get; set; } // 簇过压报警
        public bool? OvervoltageFault { get; set; } // 簇过压故障
        public bool? OvervoltageProtection { get; set; } // 簇过压保护动作

        // 电流相关告警
        public bool? ChargeOvercurrentAlarm { get; set; } // 充电过流报警
        public bool? ChargeOvercurrentFault { get; set; } // 充电过流故障
        public bool? ChargeOvercurrentProtection { get; set; } // 充电过流保护
        public bool? DischargeOvercurrentAlarm { get; set; } // 放电过流报警
        public bool? DischargeOvercurrentFault { get; set; } // 放电过流故障
        public bool? DischargeOvercurrentProtection { get; set; } // 放电过流保护

        // 绝缘相关
        public bool? InsulationAlarm { get; set; } // 绝缘报警
        public bool? InsulationFault { get; set; } // 绝缘故障
        public bool? InsulationProtection { get; set; } // 绝缘保护

        //单体欠压
        public bool? CellUnderVoltageAlarm { get; set; } // 单体欠压报警
        public bool? CellUnderVoltageFault { get; set; } // 单体欠压故障
        public bool? CellUnderVoltageProtection { get; set; } // 单体欠压保护
        //单体过压
        public bool? CellOverVoltageAlarm { get; set; } // 单体过压报警
        public bool? CellOverVoltageFault { get; set; } // 单体过压故障
        public bool? CellOverVoltageProtection { get; set; } // 单体过压保护

        // 压差相关
        public bool? VoltageDifferenceAlarm { get; set; } // 单体压差报警
        public bool? VoltageDifferenceFault { get; set; } // 单体压差故障
        public bool? VoltageDifferenceProtection { get; set; } // 单体压差保护

        public bool? resv1 { get; set; } // 预留
        public bool? resv2 { get; set; } // 预留
        public bool? resv3 { get; set; } // 预留
        public bool? resv4 { get; set; } // 预留
        public bool? resv5 { get; set; } // 预留
        public bool? resv6 { get; set; } // 预留

        // 温差相关
        public bool? TempDifferenceAlarm { get; set; } // 单体温差报警
        public bool? TempDifferenceFault { get; set; } // 单体温差故障
        public bool? TempDifferenceProtection { get; set; } // 单体温差保护

        // SOC相关
        public bool? LowSOCAlarm { get; set; } // SOC过低报警
        public bool? LowSOCFault { get; set; } // SOC过低故障
        public bool? LowSOCProtection { get; set; } // SOC过低保护
        // public bool? HighSOCAlarm { get; set; } // SOC过高报警
        // public bool? HighSOCFault { get; set; } // SOC过高故障
        // public bool? HighSOCProtection { get; set; } // SOC过高保护
        public bool? resv21 { get; set; } // 预留
        public bool? resv22 { get; set; } // 预留
        public bool? resv23 { get; set; } // 预留
        public bool? resv24 { get; set; } // 预留
        public bool? resv25 { get; set; } // 预留
        public bool? resv26 { get; set; } // 预留

        // 温度相关
        public bool? TerminalHighTempAlarm { get; set; } // 端子高温报警
        public bool? TerminalHighTempFault { get; set; } // 端子高温故障
        public bool? TerminalHighTempProtection { get; set; } // 端子高温保护

        public bool? CellChargeLowTempAlarm { get; set; } // 充电低温报警
        public bool? CellChargeLowTempFault { get; set; } // 充电低温故障
        public bool? CellChargeLowTempProtection { get; set; } // 充电低温保护
        public bool? CellChargeHighTempAlarm { get; set; } // 充电高温报警
        public bool? CellChargeHighTempFault { get; set; } // 充电高温故障
        public bool? CellChargeHighTempProtection { get; set; } // 充电高温保护
        public bool? CellDischargeLowTempAlarm { get; set; } // 放电低温报警
        public bool? CellDischargeLowTempFault { get; set; } // 放电低温故障
        public bool? CellDischargeLowTempProtection { get; set; } // 放电低温保护
        public bool? CellDischargeHighTempAlarm { get; set; } // 放电高温报警
        public bool? CellDischargeHighTempFault { get; set; } // 放电高温故障
        public bool? CellDischargeHighTempProtection { get; set; } // 放电高温保护
        public bool? HVBHighTempAlarm { get; set; } // 高压箱连接器高温报警
        public bool? HVBHighTempFault { get; set; } // 高压箱连接器高温故障
        public bool? HVBHighTempProtection { get; set; } // 高压箱连接器高温保护

        // 温升相关
        public bool? TempRiseAlarm { get; set; } // 温升报警
        public bool? TempRiseFault { get; set; } // 温升故障
        public bool? TempRiseProtection { get; set; } // 温升保护

        // 采样故障
        public bool? TempSamplingFault { get; set; } // 温度采样故障
        public bool? VoltageSamplingFault { get; set; } // 电压采样故障

        // 通信故障
        public bool? MasterCommFault { get; set; } // 主机通信故障
        public bool? SlaveCommFault { get; set; } // 从机通信故障

        // 接触器状态
        public bool? MainPositiveContactor { get; set; } // 主正接触器状态
        public bool? MainNegativeContactor { get; set; } // 主负接触器状态

        public bool? resv31 { get; set; } // 预留

        // 充放电禁止
        public bool? ChargeProhibited { get; set; } // 禁止充电
        public bool? DischargeProhibited { get; set; } // 禁止放电

        // 电池箱相关
        public bool? BatteryBoxOvervoltageProtection { get; set; } // 电池箱过压保护
        public bool? BatteryBoxOvervoltageAlarm { get; set; } // 电池箱过压报警
        public bool? BatteryBoxOvervoltageFault { get; set; } // 电池箱过压故障
        public bool? BatteryBoxUndervoltageProtection { get; set; } // 电池箱欠压保护
        public bool? BatteryBoxUndervoltageAlarm { get; set; } // 电池箱欠压报警
        public bool? BatteryBoxUndervoltageFault { get; set; } // 电池箱欠压故障
        // 电池箱温差
        public bool? BatteryBoxTempDifferenceAlarm { get; set; } // 电池箱温差报警
        public bool? BatteryBoxTempDifferenceFault { get; set; } // 电池箱温差故障
        public bool? BatteryBoxTempDifferenceProtection { get; set; } // 电池箱温差保护

        // 电池箱正极柱温差
        public bool? BatteryBoxPositivePoleTempDifferenceAlarm { get; set; } // 电池箱正极柱温差报警
        public bool? BatteryBoxPositivePoleTempDifferenceFault { get; set; } // 电池箱正极柱温差故障
        public bool? BatteryBoxPositivePoleTempDifferenceProtection { get; set; } // 电池箱正极柱温差保护

        // 电池箱负极柱温差
        public bool? BatteryBoxNegativePoleTempDifferenceAlarm { get; set; } // 电池箱负极柱温差报警
        public bool? BatteryBoxNegativePoleTempDifferenceFault { get; set; } // 电池箱负极柱温差故障
        public bool? BatteryBoxNegativePoleTempDifferenceProtection { get; set; } // 电池箱负极柱温差保护

        // 电池箱铜牌高温
        public bool? BatteryBoxBusbarHighTempAlarm { get; set; } // 电池箱铜排高温报警
        public bool? BatteryBoxBusbarHighTempFault { get; set; } // 电池箱铜排高温故障
        public bool? BatteryBoxBusbarHighTempProtection { get; set; } // 电池箱铜排高温保护

        // 电池箱电压极差
        public bool? BatteryBoxVoltageExtremaDifferenceAlarm { get; set; } // 电池箱电压极差报警
        public bool? BatteryBoxVoltageExtremaDifferenceFault { get; set; } // 电池箱电压极差故障
        public bool? BatteryBoxVoltageExtremaDifferenceProtection { get; set; } // 电池箱电压极差保护

        // rack保护汇总1，包含簇过压保护，簇欠压保护，单体过压保护，单体欠压保护，放电过流保护，充电过流保护，放电过温保护，放电欠温保护，充电过温保护，充电欠温保护，绝缘保护，端子高温保护，高压箱连接器高温保护，单体压差保护，单体温差保护，SOC过低保护
        public UInt16 RackProtectionSummary1
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(OvervoltageProtection) << 0);
                summary |= (UInt16)(ToBit(UndervoltageProtection) << 1);
                summary |= (UInt16)(ToBit(CellOverVoltageProtection) << 2);
                summary |= (UInt16)(ToBit(CellUnderVoltageProtection) << 3);
                summary |= (UInt16)(ToBit(DischargeOvercurrentProtection) << 4);
                summary |= (UInt16)(ToBit(ChargeOvercurrentProtection) << 5);
                summary |= (UInt16)(ToBit(CellDischargeHighTempProtection) << 6);
                summary |= (UInt16)(ToBit(CellDischargeLowTempProtection) << 7);
                summary |= (UInt16)(ToBit(CellChargeHighTempProtection) << 8);
                summary |= (UInt16)(ToBit(CellChargeLowTempProtection) << 9);
                summary |= (UInt16)(ToBit(InsulationProtection) << 10);
                summary |= (UInt16)(ToBit(TerminalHighTempProtection) << 11);
                summary |= (UInt16)(ToBit(HVBHighTempProtection) << 12);
                summary |= (UInt16)(ToBit(VoltageDifferenceProtection) << 13);
                summary |= (UInt16)(ToBit(TempDifferenceProtection) << 14);
                summary |= (UInt16)(ToBit(LowSOCProtection) << 15);
                return summary;
            }
        }

        // 同上汇总报警
        public UInt16 RackAlarmSummary1
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(OvervoltageAlarm) << 0);
                summary |= (UInt16)(ToBit(UndervoltageAlarm) << 1);
                summary |= (UInt16)(ToBit(CellOverVoltageAlarm) << 2);
                summary |= (UInt16)(ToBit(CellUnderVoltageAlarm) << 3);
                summary |= (UInt16)(ToBit(DischargeOvercurrentAlarm) << 4);
                summary |= (UInt16)(ToBit(ChargeOvercurrentAlarm) << 5);
                summary |= (UInt16)(ToBit(CellDischargeHighTempAlarm) << 6);
                summary |= (UInt16)(ToBit(CellDischargeLowTempAlarm) << 7);
                summary |= (UInt16)(ToBit(CellChargeHighTempAlarm) << 8);
                summary |= (UInt16)(ToBit(CellChargeLowTempAlarm) << 9);
                summary |= (UInt16)(ToBit(InsulationAlarm) << 10);
                summary |= (UInt16)(ToBit(TerminalHighTempAlarm) << 11);
                summary |= (UInt16)(ToBit(HVBHighTempAlarm) << 12);
                summary |= (UInt16)(ToBit(VoltageDifferenceAlarm) << 13);
                summary |= (UInt16)(ToBit(TempDifferenceAlarm) << 14);
                summary |= (UInt16)(ToBit(LowSOCAlarm) << 15);
                return summary;
            }
        }

        // 同上汇总故障
        public UInt16 RackFaultSummary1
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(OvervoltageFault) << 0);
                summary |= (UInt16)(ToBit(UndervoltageFault) << 1);
                summary |= (UInt16)(ToBit(CellOverVoltageFault) << 2);
                summary |= (UInt16)(ToBit(CellUnderVoltageFault) << 3);
                summary |= (UInt16)(ToBit(DischargeOvercurrentFault) << 4);
                summary |= (UInt16)(ToBit(ChargeOvercurrentFault) << 5);
                summary |= (UInt16)(ToBit(CellDischargeHighTempFault) << 6);
                summary |= (UInt16)(ToBit(CellDischargeLowTempFault) << 7);
                summary |= (UInt16)(ToBit(CellChargeHighTempFault) << 8);
                summary |= (UInt16)(ToBit(CellChargeLowTempFault) << 9);
                summary |= (UInt16)(ToBit(InsulationFault) << 10);
                summary |= (UInt16)(ToBit(TerminalHighTempFault) << 11);
                summary |= (UInt16)(ToBit(HVBHighTempFault) << 12);
                summary |= (UInt16)(ToBit(VoltageDifferenceFault) << 13);
                summary |= (UInt16)(ToBit(TempDifferenceFault) << 14);
                summary |= (UInt16)(ToBit(LowSOCFault) << 15);
                return summary;
            }
        }

        /// <summary>清除充放电方向的保护/告警/故障位（供 esscmd bmsN fault clear）。</summary>
        public void ClearChargeDischargeAlarms()
        {
            UndervoltageProtection = false;
            UndervoltageAlarm = false;
            UndervoltageFault = false;
            OvervoltageProtection = false;
            OvervoltageAlarm = false;
            OvervoltageFault = false;
            ChargeOvercurrentProtection = false;
            ChargeOvercurrentAlarm = false;
            ChargeOvercurrentFault = false;
            DischargeOvercurrentProtection = false;
            DischargeOvercurrentAlarm = false;
            DischargeOvercurrentFault = false;
            CellUnderVoltageProtection = false;
            CellUnderVoltageAlarm = false;
            CellUnderVoltageFault = false;
            CellOverVoltageProtection = false;
            CellOverVoltageAlarm = false;
            CellOverVoltageFault = false;
            LowSOCProtection = false;
            LowSOCAlarm = false;
            LowSOCFault = false;
            CellChargeLowTempProtection = false;
            CellChargeLowTempAlarm = false;
            CellChargeLowTempFault = false;
            CellChargeHighTempProtection = false;
            CellChargeHighTempAlarm = false;
            CellChargeHighTempFault = false;
            CellDischargeLowTempProtection = false;
            CellDischargeLowTempAlarm = false;
            CellDischargeLowTempFault = false;
            CellDischargeHighTempProtection = false;
            CellDischargeHighTempAlarm = false;
            CellDischargeHighTempFault = false;
        }

        // 汇总充电故障
        public bool IsChargeFault
        {
            get
            {
                return ChargeOvercurrentFault == true ||
                       CellChargeLowTempFault == true ||
                       CellChargeHighTempFault == true ||
                       CellOverVoltageFault == true ||
                       OvervoltageFault == true;
            }
        }

        // 汇总放电故障
        public bool IsDischargeFault
        {
            get
            {
                return DischargeOvercurrentFault == true ||
                       CellDischargeLowTempFault == true ||
                       CellDischargeHighTempFault == true ||
                       LowSOCFault == true ||
                       CellUnderVoltageFault == true ||
                       UndervoltageFault == true;
            }
        }

        // rack保护汇总2，包含电池箱过压保护，电池箱欠压保护，电池箱温差保护，电池箱正极柱温差保护，电池箱负极柱温差保护，电池箱铜排高温保护，电池箱电压极差保护
        public UInt16 RackProtectionSummary2
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BatteryBoxOvervoltageProtection) << 0);
                summary |= (UInt16)(ToBit(BatteryBoxUndervoltageProtection) << 1);
                summary |= (UInt16)(ToBit(BatteryBoxTempDifferenceProtection) << 2);
                summary |= (UInt16)(ToBit(BatteryBoxPositivePoleTempDifferenceProtection) << 3);
                summary |= (UInt16)(ToBit(BatteryBoxNegativePoleTempDifferenceProtection) << 4);
                summary |= (UInt16)(ToBit(BatteryBoxBusbarHighTempProtection) << 5);
                summary |= (UInt16)(ToBit(BatteryBoxVoltageExtremaDifferenceProtection) << 6);
                return summary;
            }
        }

        // 同上汇总报警
        public UInt16 RackAlarmSummary2
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BatteryBoxOvervoltageAlarm) << 0);
                summary |= (UInt16)(ToBit(BatteryBoxUndervoltageAlarm) << 1);
                summary |= (UInt16)(ToBit(BatteryBoxTempDifferenceAlarm) << 2);
                summary |= (UInt16)(ToBit(BatteryBoxPositivePoleTempDifferenceAlarm) << 3);
                summary |= (UInt16)(ToBit(BatteryBoxNegativePoleTempDifferenceAlarm) << 4);
                summary |= (UInt16)(ToBit(BatteryBoxBusbarHighTempAlarm) << 5);
                summary |= (UInt16)(ToBit(BatteryBoxVoltageExtremaDifferenceAlarm) << 6);
                return summary;
            }
        }

        // 同上汇总故障
        public UInt16 RackFaultSummary2
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BatteryBoxOvervoltageFault) << 0);
                summary |= (UInt16)(ToBit(BatteryBoxUndervoltageFault) << 1);
                summary |= (UInt16)(ToBit(BatteryBoxTempDifferenceFault) << 2);
                summary |= (UInt16)(ToBit(BatteryBoxPositivePoleTempDifferenceFault) << 3);
                summary |= (UInt16)(ToBit(BatteryBoxNegativePoleTempDifferenceFault) << 4);
                summary |= (UInt16)(ToBit(BatteryBoxBusbarHighTempFault) << 5);
                summary |= (UInt16)(ToBit(BatteryBoxVoltageExtremaDifferenceFault) << 6);
                return summary;
            }
        }

        // 对外三级报警2(主控) — 匹配 Excel 0x1016 位定义
        // bit0:总压压差过高 bit1:模组过压 bit2:模组欠压 bit3:温升过高
        // bit4:模组压差过大(预留) bit5:预留 bit6:铜排温度过高 bit7~12:内部预留
        public UInt16 ExternalFaultSummary2
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BatteryBoxVoltageExtremaDifferenceFault) << 0);
                summary |= (UInt16)(ToBit(BatteryBoxOvervoltageFault) << 1);
                summary |= (UInt16)(ToBit(BatteryBoxUndervoltageFault) << 2);
                summary |= (UInt16)(ToBit(TempRiseFault) << 3);
                summary |= (UInt16)(ToBit(BatteryBoxBusbarHighTempFault) << 6);
                return summary;
            }
        }

        // 对外二级报警2(主控) — 匹配 Excel 0x105A 位定义
        public UInt16 ExternalAlarmSummary2
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BatteryBoxVoltageExtremaDifferenceAlarm) << 0);
                summary |= (UInt16)(ToBit(BatteryBoxOvervoltageAlarm) << 1);
                summary |= (UInt16)(ToBit(BatteryBoxUndervoltageAlarm) << 2);
                summary |= (UInt16)(ToBit(TempRiseAlarm) << 3);
                summary |= (UInt16)(ToBit(BatteryBoxBusbarHighTempAlarm) << 6);
                return summary;
            }
        }

        // 对外一级保护2(主控) — 匹配 Excel 0x109F 位定义
        public UInt16 ExternalProtectionSummary2
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BatteryBoxVoltageExtremaDifferenceProtection) << 0);
                summary |= (UInt16)(ToBit(BatteryBoxOvervoltageProtection) << 1);
                summary |= (UInt16)(ToBit(BatteryBoxUndervoltageProtection) << 2);
                summary |= (UInt16)(ToBit(TempRiseProtection) << 3);
                summary |= (UInt16)(ToBit(BatteryBoxBusbarHighTempProtection) << 6);
                return summary;
            }
        }

        // 对外三级报警3(主控) — 匹配 Excel 0x1017 位定义
        // bit0:模组温差 bit1:负极柱温差 bit2:正极柱温差 bit3:铜排温差(预留)
        public UInt16 ExternalFaultSummary3
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BatteryBoxTempDifferenceFault) << 0);
                summary |= (UInt16)(ToBit(BatteryBoxNegativePoleTempDifferenceFault) << 1);
                summary |= (UInt16)(ToBit(BatteryBoxPositivePoleTempDifferenceFault) << 2);
                return summary;
            }
        }

        // 对外二级报警3(主控) — 匹配 Excel 0x105B 位定义
        public UInt16 ExternalAlarmSummary3
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BatteryBoxTempDifferenceAlarm) << 0);
                summary |= (UInt16)(ToBit(BatteryBoxNegativePoleTempDifferenceAlarm) << 1);
                summary |= (UInt16)(ToBit(BatteryBoxPositivePoleTempDifferenceAlarm) << 2);
                return summary;
            }
        }

        // 对外一级保护3(主控) — 匹配 Excel 0x10A0 位定义
        public UInt16 ExternalProtectionSummary3
        {
            get
            {
                UInt16 summary = 0;
                summary |= (UInt16)(ToBit(BatteryBoxTempDifferenceProtection) << 0);
                summary |= (UInt16)(ToBit(BatteryBoxNegativePoleTempDifferenceProtection) << 1);
                summary |= (UInt16)(ToBit(BatteryBoxPositivePoleTempDifferenceProtection) << 2);
                return summary;
            }
        }

        private static UInt16 ToBit(bool? flag)
        {
            return flag == true ? (UInt16)1 : (UInt16)0;
        }
    }
}
