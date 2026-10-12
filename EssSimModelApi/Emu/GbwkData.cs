namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{

    /// <summary>
    /// 干变温控(GBWK)数据模型
    /// </summary>
    public class GbwkData
    {
        // 绕组温度
        public float Winding1PhaseATemp { get; set; }      // GBWK-绕组1 A相温度
        public float Winding1PhaseBTemp { get; set; }      // GBWK-绕组1 B相温度
        public float Winding1PhaseCTemp { get; set; }      // GBWK-绕组1 C相温度
        public float Winding1PhaseDTemp { get; set; }      // GBWK-绕组1 D路温度(预留)

        public float Winding2PhaseATemp { get; set; }      // GBWK-绕组2 A相温度(双分裂时有效)
        public float Winding2PhaseBTemp { get; set; }      // GBWK-绕组2 B相温度(双分裂时有效)
        public float Winding2PhaseCTemp { get; set; }      // GBWK-绕组2 C相温度(双分裂时有效)
        public float Winding2PhaseDTemp { get; set; }      // GBWK-绕组2 D路温度(预留)

        // 控制输出
        public bool FanControlOutput { get; set; }         // GBWK-风机控制输出位
        public bool OverTempTripOutput { get; set; }       // GBWK-超温跳闸输出位
        public bool OverTempAlarmOutput { get; set; }      // GBWK-超温报警输出位
        public bool FaultAlarmOutput { get; set; }        // GBWK-故障报警输出位

        // 预留字段
        public List<bool> ReservedOutputs { get; set; } = new List<bool>();
    }
}
