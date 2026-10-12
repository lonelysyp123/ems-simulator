using System;
using System.Collections.Generic;
using System.Linq;
using EssSimulator.EssDeviceSimModel.Bms;

namespace EssSimulator.EssSimModelApi.BatteryManagementSystem
{
public class BatteryCluster
    {
        public int ClusterId { get; set; } // 簇编号
        public ClusterBasicMeasurements Measurements { get; set; } = new ClusterBasicMeasurements(); // 基础测量
        public ClusterAlarms Alarms { get; set; } = new ClusterAlarms(); // 告警状态
        public ClusterBasicCellVoltages ClusterCellVoltages { get; set; } = new ClusterBasicCellVoltages(); // 单体电压
        public ClusterBasicCellTemperatures ClusterCellTemperatures { get; set; } = new ClusterBasicCellTemperatures(); // 单体温度
        public ClusterThresholds Thresholds { get; set; } = new ClusterThresholds(); // 阈值与恢复阈值
    }
}
