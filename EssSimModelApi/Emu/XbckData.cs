namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{

    /// <summary>
    /// 箱变测控(XBCK)数据模型
    /// </summary>
    public class XbckData
    {
        public int GroupId { get; set; }

        // 电气参数
        public float PhaseACurrent { get; set; }           // XBCK-第一组A相电流
        public float PhaseBCurrent { get; set; }           // XBCK-第一组B相电流
        public float PhaseCCurrent { get; set; }           // XBCK-第一组C相电流
        public float LineVoltageAB { get; set; }           // XBCK-第一组AB线电压
        public float LineVoltageBC { get; set; }           // XBCK-第一组BC线电压
        public float LineVoltageCA { get; set; }           // XBCK-第一组CA线电压
        public float ThreePhaseActivePower { get; set; }   // XBCK-第一组3相有功功率
        public float ThreePhaseReactivePower { get; set; } // XBCK-第一组3相无功功率
        public float PowerFactor { get; set; }             // XBCK-第一组功率因数
        public float Frequency { get; set; }               // XBCK-第一组频率
        public float ZeroSequenceCurrent { get; set; }     // XBCK-第一组零序电流

        // 温度监测
        public float TransformerRoomTemp { get; set; }     // XBCK-X4-10-11-PT100变压器室在线测温
        public float BusbarTemp { get; set; }              // XBCK-X4-12-13-PT100领排陀铜排在线测温

        // 模拟量输入
        public float AnalogInput1 { get; set; }            // XBCK-4-20mA 1
        public float AnalogInput2 { get; set; }            // XBCK-4-20mA 2
        public float AnalogInput3 { get; set; }            // XBCK-4-20mA 3

        // 数字输入状态
        public List<DigitalInputStatus> DigitalInputs { get; set; } = new List<DigitalInputStatus>();

        // 预留字段
        public List<float> ReservedValues { get; set; } = new List<float>();
    }
}
