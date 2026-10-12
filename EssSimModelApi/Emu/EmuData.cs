namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{
    /// <summary>
    /// 能量管理单元(EMU)数据模型
    /// </summary>
    public class EmuData
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;

        // 高压断路器开合别名：0=分，1=合（与 EnergyManagementData.Breaker.Closed 同步；新点表绑 Breaker.Closed）
        public ushort PowerOnOff { get; set; } = 1;

        // 系统级（EMU 聚合）控制目标（点表 SYSTEM 工作表 syst4~7、syst1010/1011）
        /// <summary>远程&本地控制使能（syst4：0禁止 1使能）。</summary>
        public int RemoteControlEnable { get; set; }
        /// <summary>远程&本地控制模式（syst5：0本地 1远程）。</summary>
        public int RemoteControlMode { get; set; }
        /// <summary>系统操作（syst6：3启动 4停止 5待机 6重置），边沿生效。</summary>
        public int SystemOperation { get; set; }
        /// <summary>黑启动模式写入（syst7：0关闭 1开启），边沿生效并批量下发所属 PCS。</summary>
        public int BlackStartModeWrite { get; set; }
        /// <summary>交流侧目标有功功率（syst1010，kW，正放负充）；远程使能时按台数简单均分。</summary>
        public float TargetActivePower { get; set; }
        /// <summary>交流侧目标无功功率（syst1011，kvar）；远程使能时按台数简单均分。</summary>
        public float TargetReactivePower { get; set; }

        /// <summary>已应用的系统操作码（边沿检测用，不对外绑定）。</summary>
        internal int AppliedSystemOperation { get; set; }
        /// <summary>已应用的黑启动写入值（边沿检测用，不对外绑定）。</summary>
        internal int AppliedBlackStartWrite { get; set; } = -1;

        // 基本功率信息
        public float OutputActivePower { get; set; }       // EMU-输出总有功功率
        public float OutputReactivePower { get; set; }     // EMU-输出总无功功率

        // 功率能力信息
        public float MaxChargePower { get; set; }          // EMU-最大可充电功率
        public float MaxDischargePower { get; set; }       // EMU-最大可放电功率
        public float MaxInductiveReactivePower { get; set; } // EMU-最大可用感性无功
        public float MaxCapacitiveReactivePower { get; set; } // EMU-最大可用容性无功

        // 运行状态
        public int OperationStatus { get; set; }           // EMU-工作状态 1、停机 2、待机 4、充电运行 5、放电运行 6、未知状态
        public int TotalPcsCount { get; set; }             // EMU-PCS总数
        public int OnlinePcsCount { get; set; }            // EMU-在线PCS数
        public int GridConnectedPcsCount { get; set; }     // EMU-并网PCS数

        // 电池系统信息
        public int TotalBatteryCount { get; set; }         // EMU-电池总数
        public int OnlineBatteryCount { get; set; }        // EMU-在线电池数
        public float AverageBatterySoc { get; set; }       // EMU-电池平均SOC（0–1）

        // PCS状态统计
        public int AlarmPcsCount { get; set; }             // EMU-告警PCS台数
        public int FaultPcsCount { get; set; }             // EMU-故障PCS台数
        public int ChargeProhibitedPcsCount { get; set; }  // EMU-禁止充电PCS台数
        public int DischargeProhibitedPcsCount { get; set; } // EMU-禁止放电PCS台数

        // 输入信号状态
        public List<DigitalInputStatus> DigitalInputs { get; set; } = new List<DigitalInputStatus>();

        // 输出控制信号
        public bool SauTripLowVoltageBreaker { get; set; }  // EMU-SAU跳闸低压断路器
        public bool SauTripHighVoltageBreaker1 { get; set; } // EMU-SAU跳闸高压断路器-1
        public bool SauTripHighVoltageBreaker2 { get; set; } // EMU-SAU跳闸高压断路器-2
        public bool FireSignal { get; set; }               // EMU-消防信号
        public bool FanControl { get; set; }               // EMU-风扇控制

        // 系统状态标志
        public bool SocBalanceSchedulingEnabled { get; set; } // EMU-SOC均衡调度投入中
        public bool PwmBlockingEnabled { get; set; }        // EMU-PWM封波投入中
        public bool AllPcsShutdown { get; set; }           // EMU-所有PCS关机
        public bool AnyPcsStarted { get; set; }            // EMU-任意PCS开机
        public bool AllPcsAlarmed { get; set; }            // EMU-所有PCS告警
        public bool AnyPcsAlarmed { get; set; }            // EMU-任意PCS告警
        public bool AllPcsFaulted { get; set; }            // EMU-所有PCS故障
        public bool AnyPcsFaulted { get; set; }            // EMU-任意PCS故障
        public bool AllPcsChargeProhibited { get; set; }   // EMU-所有PCS禁止充电
        public bool AnyPcsChargeProhibited { get; set; }   // EMU-任意PCS禁止充电
        public bool AllPcsDischargeProhibited { get; set; } // EMU-所有PCS禁止放电
        public bool AnyPcsDischargeProhibited { get; set; } // EMU-任意PCS禁止放电

        // 预留字段
        public List<float> ReservedTelemetry { get; set; } = new List<float>(); // 遥测预留
        public List<bool> ReservedSignals { get; set; } = new List<bool>();     // 遥信预留
        public List<float> ReservedAdjustments { get; set; } = new List<float>(); // 遥调预留
    }
}
