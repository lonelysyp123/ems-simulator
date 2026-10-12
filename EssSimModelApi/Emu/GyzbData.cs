namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{

    /// <summary>
    /// 公用测控(GYZB)数据模型
    /// </summary>
    public class GyzbData
    {
        // 电气参数
        public float PhaseACurrent { get; set; }           // GYZB-A相电流 Ia
        public float PhaseBCurrent { get; set; }           // GYZB-B相电流 Ib
        public float PhaseCCurrent { get; set; }           // GYZB-C相电流 Ic
        public float LineVoltageAB { get; set; }           // GYZB-线电压 UAB
        public float LineVoltageBC { get; set; }           // GYZB-线电压 UBC
        public float LineVoltageCA { get; set; }           // GYZB-线电压 UCA
        public float ActivePower { get; set; }             // GYZB-有功功率 P 
        public float ReactivePower { get; set; }           // GYZB-无功功率 Q 
        public float PowerFactor { get; set; }             // GYZB-功率因数 PF 
        public float Frequency { get; set; }               // GYZB-频率

        // 状态信号
        public bool CircuitBreakerClosed { get; set; }     // GYZB-断路器合闸
        public bool CircuitBreakerOpened { get; set; }     // GYZB-断路器分闸
        public bool SpringNotCharged { get; set; }         // GYZB-弹簧未储能
        public bool RemoteIndicator { get; set; }          // GYZB-远方指示
        public bool GroundSwitchClosed { get; set; }       // GYZB-接地刀合闸
        public bool HighTempWarning { get; set; }          // GYZB-高温
        public bool OverTempWarning { get; set; }          // GYZB-超温

        // 装置状态
        public bool DeviceAbnormal { get; set; }           // GYZB-装置异常
        public bool HasErrorRecord { get; set; }           // GYZB-装置是否有出错记录
    }
}
