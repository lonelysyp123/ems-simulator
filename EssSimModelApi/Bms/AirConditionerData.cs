using System;
using System.Collections.Generic;
using System.Linq;

namespace EssSimulator.EssSimModelApi.BatteryManagementSystem
{
public class AirConditionerData
    {
        public int UnitId { get; set; } // 机组编号

        // 控制命令（由 EMS/BMS 点表写入）
        /// <summary>空调开机命令：true=开机（制冷），false=停机。</summary>
        public bool? OnCommand { get; set; }
        /// <summary>空调制冷设定温度命令（°C），如 20。</summary>
        public float? CoolingSetpointCommand { get; set; }

        // 状态信息
        public bool? DeviceOperationStatus { get; set; } // 设备运行状态
        public bool? IndoorFanStatus { get; set; } // 室内风机状态
        public bool? OutdoorFanStatus { get; set; } // 室外风机状态
        public bool? CompressorStatus { get; set; } // 压缩机状态
        public bool? ElectricHeaterStatus { get; set; } // 电加热状态

        // 温度信息
        public float? DefrostTemp { get; set; } // 除霜温度
        public float? CondensationTemp { get; set; } // 冷凝温度
        public float? CabinetTemp { get; set; } // 机柜温度
        public float? CabinetHumidity { get; set; } // 机柜湿度

        // 设置参数
        public float? CoolingSetTemp { get; set; } // 制冷设定温度
        public float? CoolingControlHysteresis { get; set; } // 制冷回差
        public float? HeatingSetTemp { get; set; } // 制热设定温度
        public float? HeatingControlHysteresis { get; set; } // 制热回差
        public float? HumiditySetValue { get; set; } // 湿度设定值
        public float? HumidityControlHysteresis { get; set; } // 湿度回差

        // 告警信息
        public bool? CabinetOverheat { get; set; } // 机柜过热
        public bool? CabinetUnderheat { get; set; } // 机柜欠热
        public bool? HighHumidity { get; set; } // 高湿
        public bool? LowHumidity { get; set; } // 低湿
        public bool? CoilFreezeProtection { get; set; } // 盘管防冻

        // 故障信息
        public bool? DefrostSensorFault { get; set; } // 除霜传感器故障
        public bool? CondensationTempSensorFault { get; set; } // 冷凝温度传感器故障
        public bool? CabinetTempSensorFault { get; set; } // 机柜温度传感器故障
        public bool? OutletTempSensorFault { get; set; } // 出口温度传感器故障
        public bool? HumiditySensorFault { get; set; } // 湿度传感器故障
        public bool? IndoorFanFault { get; set; } // 室内风机故障
        public bool? CompressorFault { get; set; } // 压缩机故障
        public bool? HighPressureAlarm { get; set; } // 高压报警
        public bool? LowPressureAlarm { get; set; } // 低压报警
        public bool? PhaseSequenceAlarm { get; set; } // 相序报警
    }
}
