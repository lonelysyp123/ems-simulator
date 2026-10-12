namespace EssSimulator.EssSimModelApi.EnergyManagementSystem
{

    /// <summary>
    /// 完整的能量管理系统数据模型
    /// </summary>
    public class EnergyManagementData
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public EmuData Emu { get; set; } = new EmuData();
        public List<PcsData> PcsList { get; set; } = new List<PcsData>();
        public List<XbckData> XbckList { get; set; } = new List<XbckData>();
        public List<GbwkData> GbwkList { get; set; } = new List<GbwkData>();
        public GyzbData Gyzb { get; set; } = new GyzbData();
        public ElectricityMeterData ElectricityMeter { get; set; } = new ElectricityMeterData();

        /// <summary>EMU 内协议分组（EMU → group → PCS 支路）；扁平构成时为空。</summary>
        public List<EmuGroupData> Groups { get; set; } = new List<EmuGroupData>();
        /// <summary>单元变镜像（本期仅 Transformers[0]，对应电气层单元变）。</summary>
        public List<TransformerMirrorData> Transformers { get; set; } = new List<TransformerMirrorData>();
        /// <summary>EMU 级高压断路器（Closed 为控制/遥信源；PowerOnOff 保持同步别名）。</summary>
        public BreakerMirrorData Breaker { get; set; } = new BreakerMirrorData();

        /// <summary>上一拍已下发的控制指纹；未初始化时强制走命令路径。</summary>
        internal int LastCommandSyncFingerprint { get; set; }
        internal bool HasCommandSyncFingerprint { get; set; }
    }
}
