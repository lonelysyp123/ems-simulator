using System;
using System.Collections.Generic;
using System.Linq;

namespace EssSimulator.EssSimModelApi.BatteryManagementSystem
{
public partial class BatteryStack
    {
        public BatteryStack()
        {
            SystemAlarms = new StackSystemAlarms(this);
        }

        /// <summary>堆级系统告警视图（聚合各簇 Alarms；Modbus bank yx 可读写）。</summary>
        [System.Runtime.Serialization.IgnoreDataMember]
        public StackSystemAlarms SystemAlarms { get; }
        [System.Runtime.Serialization.IgnoreDataMember]
        public float NominalEnergyKWh { get; set; } = 5016f;

        [System.Runtime.Serialization.IgnoreDataMember]
        public float MaxCRate { get; set; } = 0.5f;

        public int StackId { get; set; } // 堆编号

        /// <summary>一键并离网脉冲命令：写 1 并网、写 2 离网，处理后自动回 0。</summary>
        public ushort GridConnectCommand { get; set; }

        /// <summary>一键复归脉冲命令：写 1 触发故障清除，处理后自动回 0。</summary>
        public ushort FaultClearCommand { get; set; }

        /// <summary>一键并网状态：0 未开始 / 1 进行中 / 2 成功 / 3 失败。</summary>
        public ushort GridConnectStatus { get; set; }

        /// <summary>BMS 与 PCS 直流侧是否已关联。</summary>
        public bool IsPcsLinked { get; set; }

        /// <summary>黑启动模式指令（param12）：写 1 进入，写 0 退出。</summary>
        public ushort BlackStartCommand { get; set; }

        /// <summary>进入黑启动模式是否成功（param5）：0 否 / 1 是。</summary>
        public ushort BlackStartEnterSuccess { get; set; }

        /// <summary>当前黑启动状态（param6）：3 已进入 / 4 进入失败 / 5 已退出。</summary>
        public ushort BlackStartStatus { get; set; }

        // 堆基本信息
        public float? TotalVoltage { get; set; } // 堆端总电压
        public float? Current { get; set; } // 堆端电流
        public float? Power { get; set; } // 堆端功率
        // 系统充放电状态 0-未充放电，1-放电，2-充电
        public int? ChargeDischargeStatus 
        {
            get
            {
                if (Current.HasValue)
                {
                    if (Current.Value > 0)
                        return 1; // 放电
                    else if (Current.Value < 0)
                        return 2; // 充电
                    else
                        return 0; // 未充放电
                }
                else
                {
                    return null; // 电流无效时返回null
                }
            }
        }
        public float? SOC { get; set; } // 堆SOC
        public float? SOH { get; set; } // 堆SOH
        public float? InsulationPlus { get; set; } // 正极对地绝缘值
        public float? InsulationMinus { get; set; } // 负极对地绝缘值
        /// <summary>
        /// 系统运行状态（bank yc3，由 <c>BmsMapper.UpdateStackOperationStatus</c> 周期刷新）：
        /// 0 正常；1 禁充；2 禁放；3 待机（非充放电方向二级告警）；4 停机（三级告警或 BMS 下电）。
        /// </summary>
        public int? OperationStatus { get; set; }
        public float Cycles { get; set; } // 循环次数
        public float res2 { get; set; } // 预留
        public float res3 { get; set; } // 预留
        public float res4 { get; set; } // 预留
        public float res5 { get; set; } // 预留


        // 堆内统计信息
        public float? MaxCellVoltage { get; set; } // 堆内最大单体电压
        public int? MaxCellVoltageClusterId { get; set; } // 所在簇编号
        public int? MaxCellVoltagePackId { get; set; } // 所在组编号
        public int? MaxCellVoltageCellId { get; set; } // 所在单体编号
        public float? MinCellVoltage { get; set; } // 堆内最小单体电压
        public int? MinCellVoltageClusterId { get; set; } // 所在簇编号
        public int? MinCellVoltagePackId { get; set; } // 所在组编号
        public int? MinCellVoltageCellId { get; set; } // 所在单体编号
        public float? AvgCellVoltage { get; set; } // 堆内单体平均电压
        public float? CellVoltageDiff { get; set; } // 堆内单体电压差

        public float? MaxCellTemp { get; set; } // 堆内最大单体温度
        public int? MaxCellTempClusterId { get; set; } // 所在簇编号
        public int? MaxCellTempPackId { get; set; } // 所在组编号
        public int? MaxCellTempCellId { get; set; } // 所在单体编号
        public float? MinCellTemp { get; set; } // 堆内最小单体温度
        public int? MinCellTempClusterId { get; set; } // 所在簇编号
        public int? MinCellTempPackId { get; set; } // 所在组编号
        public int? MinCellTempCellId { get; set; } // 所在单体编号
        public float? AvgCellTemp { get; set; } // 堆内单体平均温度
        public float? CellTempDiff { get; set; } // 堆内单体温差

        public float? MaxCellSOC { get; set; } // 堆内最大单体SOC
        public float? MinCellSOC { get; set; } // 堆内最小单体SOC

        // 电池簇管理
        public int? ManagedClusterCount { get; set; } // 管理的簇数量

        /// <summary>堆内总簇数（yc46）。</summary>
        public int TotalClusterCount =>
            Cluseter?.Count > 0
                ? Cluseter.Count
                : ManagedClusterCount ?? 0;

        /// <summary>当前在网簇数（yc45）：并网时为总簇数，离网时为 0。</summary>
        public int OnlineClusterCount => IsPcsLinked ? TotalClusterCount : 0;

        public int? ManagedCellCount { get; set; } // 管理的单体数量
        public int? ManagedTempSensorCount { get; set; } // 管理的温感数量

        // 能量统计
        public float? CumulativeChargeEnergy { get; set; } // 堆累计充电电量
        public float? CumulativeDischargeEnergy { get; set; } // 堆累计放电电量
        // public float? SingleChargeEnergy { get; set; } // 堆本次充电电量
        // public float? SingleDischargeEnergy { get; set; } // 堆本次放电电量
        // public float? DailyChargeEnergy { get; set; } // 堆当日充电电量
        // public float? DailyDischargeEnergy { get; set; } // 堆当日放电电量
        // public float? AvailableChargeEnergy { get; set; } // 堆可用充电容量
        // public float? AvailableDischargeEnergy { get; set; } // 堆可用放电容量

        // 限制参数
        /// <summary>
        /// 最大允许充电功率：各簇 <see cref="ClusterBasicMeasurements.MaxChargePower"/> 之和。
        /// 是否降功率只由 PCS/BMS 告警与故障决定，不再乘温度降额因子。
        /// </summary>
        public float? MaxChargePower =>
            SumClusterLimits(c => c.Measurements?.MaxChargePower);

        /// <summary>
        /// 最大允许放电功率：各簇最大允许放电功率之和。
        /// </summary>
        public float? MaxDischargePower =>
            SumClusterLimits(c => c.Measurements?.MaxDischargePower);

        /// <summary>
        /// 最大允许充电电流：各簇最大允许充电电流之和（并联簇电流累加）。
        /// </summary>
        public float? MaxChargeCurrent =>
            SumClusterLimits(c => c.Measurements?.MaxChargeCurrent);

        /// <summary>
        /// 最大允许放电电流：各簇最大允许放电电流之和。
        /// </summary>
        public float? MaxDischargeCurrent =>
            SumClusterLimits(c => c.Measurements?.MaxDischargeCurrent);

        private float? SumClusterLimits(Func<BatteryCluster, float?> selector)
        {
            if (Cluseter == null || Cluseter.Count == 0)
                return null;

            float sum = 0f;
            bool any = false;
            foreach (var cluster in Cluseter)
            {
                float? v = selector(cluster);
                if (!v.HasValue)
                    continue;
                sum += v.Value;
                any = true;
            }

            if (!any)
                return null;

            return sum;
        } 

        public float? AvailableChargeCapacity 
        { 
            get
            {
                if (!SOC.HasValue) return null;
                return NominalEnergyKWh * (1 - SOC.Value);
            }
        } // 可用充电容量
        public float? AvailableDischargeCapacity 
        { 
            get
            {
                if (!SOC.HasValue) return null;
                return NominalEnergyKWh * SOC.Value;
            }
        } // 可用放电容量

        // 告警信息
        public bool? BMSSystemChannelStatus { get; set; } // BMS系统通道状态
        public ushort BMSProtectionSummary 
        { 
            get
            {
                ushort res = 0;
                // 统计所有簇的总电压过压三级保护状态
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                    {
                        res |= cluster.Alarms.RackProtectionSummary1;
                    }
                }
                return res;
            }
        } // 一级告警汇总

        public ushort BMSAlarmSummary 
        { 
            get
            {
                ushort res = 0;
                // 统计所有簇的总电压过压三级告警状态
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                    {
                        res |= cluster.Alarms.RackAlarmSummary1;
                    }
                }
                return res;
            }
        } // 二级告警汇总

        public ushort BMSFaultSummary 
        { 
            get
            {
                ushort res = 0;
                // 统计所有簇的总电压过压三级故障状态
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                    {
                        res |= cluster.Alarms.RackFaultSummary1;
                    }
                }
                return res;
            }
        } // 三级告警汇总

        // 对外三级报警2(主控) — 匹配 Excel 0x1016
        public ushort BMSFaultSummary2
        {
            get
            {
                ushort res = 0;
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                        res |= cluster.Alarms.ExternalFaultSummary2;
                }
                return res;
            }
        }

        // 对外三级报警3(主控) — 匹配 Excel 0x1017
        public ushort BMSFaultSummary3
        {
            get
            {
                ushort res = 0;
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                        res |= cluster.Alarms.ExternalFaultSummary3;
                }
                return res;
            }
        }

        // 对外二级报警2(主控) — 匹配 Excel 0x105A
        public ushort BMSAlarmSummary2
        {
            get
            {
                ushort res = 0;
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                        res |= cluster.Alarms.ExternalAlarmSummary2;
                }
                return res;
            }
        }

        // 对外二级报警3(主控) — 匹配 Excel 0x105B
        public ushort BMSAlarmSummary3
        {
            get
            {
                ushort res = 0;
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                        res |= cluster.Alarms.ExternalAlarmSummary3;
                }
                return res;
            }
        }

        // 对外一级保护2(主控) — 匹配 Excel 0x109F
        public ushort BMSProtectionSummary2
        {
            get
            {
                ushort res = 0;
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                        res |= cluster.Alarms.ExternalProtectionSummary2;
                }
                return res;
            }
        }

        // 对外一级保护3(主控) — 匹配 Excel 0x10A0
        public ushort BMSProtectionSummary3
        {
            get
            {
                ushort res = 0;
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                        res |= cluster.Alarms.ExternalProtectionSummary3;
                }
                return res;
            }
        }

        // 对外三级报警1(总控) — 匹配 Excel 0x1038
        // bit0:簇间电流差预警 bit1:簇间压差预警 bit2:预留
        public ushort BMSFaultSummaryBAU
        {
            get
            {
                ushort res = 0;
                if (ClusterCurrentDiff.HasValue && System.Math.Abs(ClusterCurrentDiff.Value) > 0.1f)
                    res |= 1;
                if (ClusterVoltageDiff.HasValue && ClusterVoltageDiff.Value > 0.1f)
                    res |= 2;
                return res;
            }
        }

        // 对外二级报警1(总控) — 匹配 Excel 0x107C
        // bit0:簇间电流差报警 bit1:簇间压差报警 bit2:预留
        public ushort BMSAlarmSummaryBAU
        {
            get
            {
                ushort res = 0;
                if (ClusterCurrentDiff.HasValue && System.Math.Abs(ClusterCurrentDiff.Value) > 0.1f)
                    res |= 1;
                if (ClusterVoltageDiffAlarm == true)
                    res |= 2;
                return res;
            }
        }

        // 对外一级保护1(总控) — 匹配 Excel 0x10C1
        // bit0:簇间电流差保护 bit1:簇间压差保护 bit2:预留
        public ushort BMSProtectionSummaryBAU
        {
            get
            {
                ushort res = 0;
                if (ClusterCurrentDiff.HasValue && System.Math.Abs(ClusterCurrentDiff.Value) > 0.1f)
                    res |= 1;
                if (ClusterVoltageDiff.HasValue && ClusterVoltageDiff.Value > 0.1f)
                    res |= 2;
                return res;
            }
        }

        public bool IsChargeFault
        {
            get
            {
                // 判断是否存在充电故障
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                    {
                        if (cluster.Alarms.IsChargeFault)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        } // 是否存在充电故障

        public bool IsDischargeFault
        {
            get
            {
                // 判断是否存在放电故障
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    foreach (var cluster in Cluseter)
                    {
                        if (cluster.Alarms.IsDischargeFault)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        } // 是否存在放电故障

        // 簇间差异
        public float? ClusterVoltageDiff 
        { 
            get
            {
                // 计算簇间压差值
                if (MaxCellVoltage.HasValue && MinCellVoltage.HasValue)
                {
                    return MaxCellVoltage.Value - MinCellVoltage.Value;
                }
                else
                {
                    return null;
                }
            }
        } // 簇间压差值

        public bool? ClusterVoltageDiffAlarm { get; set; } // 簇间压差报警
        public float? ClusterCurrentDiff 
        { 
            get
            {
                // 计算簇间电流差值
                if (Cluseter != null && Cluseter.Count > 0)
                {
                    float? maxCurrent = null;
                    float? minCurrent = null;
                    foreach (var cluster in Cluseter)
                    {
                        if (cluster.Measurements.Current.HasValue)
                        {
                            if (!maxCurrent.HasValue || cluster.Measurements.Current.Value > maxCurrent.Value)
                            {
                                maxCurrent = cluster.Measurements.Current.Value;
                            }
                            if (!minCurrent.HasValue || cluster.Measurements.Current.Value < minCurrent.Value)
                            {
                                minCurrent = cluster.Measurements.Current.Value;
                            }
                        }
                    }
                    if (maxCurrent.HasValue && minCurrent.HasValue)
                    {
                        return maxCurrent.Value - minCurrent.Value;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
        } // 簇间电流差值
        public bool? ClusterCurrentImbalanceAlarm { get; set; } // 簇间电流不平衡报警

        // 包含的电池簇
        public List<BatteryCluster> Cluseter { get; set; } = new List<BatteryCluster>(); // 所含簇列表

        // 预留字段
        public List<float?> Reserved { get; set; } = new List<float?>(); // 预留
    }
}
