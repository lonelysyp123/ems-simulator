namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{

    /// <summary>
    /// 电表(DB)数据模型
    /// </summary>
    public class ElectricityMeterData
    {
        // 电压电流
        public float PhaseAVoltage { get; set; }           // DB-电网A相电压
        public float PhaseBVoltage { get; set; }           // DB-电网B相电压
        public float PhaseCVoltage { get; set; }           // DB-电网C相电压
        public float LineVoltageAB { get; set; }           // DB-电网AB线电压
        public float LineVoltageBC { get; set; }           // DB-电网BC线电压
        public float LineVoltageCA { get; set; }           // DB-电网CA线电压
        public float PhaseACurrent { get; set; }           // DB-电网A相电流
        public float PhaseBCurrent { get; set; }           // DB-电网B相电流
        public float PhaseCCurrent { get; set; }           // DB-电网C相电流

        // 功率
        public float PhaseAActivePower { get; set; }       // DB-A相有功功率
        public float PhaseBActivePower { get; set; }       // DB-B相有功功率
        public float PhaseCActivePower { get; set; }       // DB-C相有功功率
        public float TotalActivePower { get; set; }        // DB-总有功功率
        public float PhaseAReactivePower { get; set; }     // DB-A相无功功率
        public float PhaseBReactivePower { get; set; }     // DB-B相无功功率
        public float PhaseCReactivePower { get; set; }     // DB-C相无功功率
        public float TotalReactivePower { get; set; }      // DB-总无功功率
        public float TotalApparentPower { get; set; }      // DB-总视在功率
        public float PowerFactor { get; set; }             // DB-功率因数
        public float Frequency { get; set; }               // DB-电网频率

        // 电能
        public float ForwardActiveEnergy { get; set; }     // DB-正向有功电能
        public float ReverseActiveEnergy { get; set; }     // DB-反向有功电能
        public float InductiveReactiveEnergy { get; set; } // DB-感性无功电能
        public float CapacitiveReactiveEnergy { get; set; } // DB-容性无功电能
    }
}
